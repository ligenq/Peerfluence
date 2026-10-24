using Microsoft.Extensions.Logging;
using Peerfluence.Core.Config;
using PeerSharp.Clients;
using PeerSharp.Config;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public sealed partial class TorrentEngineService : ITorrentEngineService
{
    private readonly IAppSettingsService _settingsService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TorrentEngineService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TorrentClientOptions, IClientEngine> _createEngine;
    private IClientEngine? _engine;
    private Task? _shutdownTask;

    public TorrentEngineService(
        IAppSettingsService settingsService,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
        : this(settingsService, loggerFactory, timeProvider, ClientEngineFactory.Create)
    {
    }

    /// <summary>
    /// For tests that need to see what is asked of the engine, and in what order.
    /// </summary>
    internal TorrentEngineService(
        IAppSettingsService settingsService,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        Func<TorrentClientOptions, IClientEngine> createEngine)
    {
        _settingsService = settingsService;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TorrentEngineService>();
        _timeProvider = timeProvider;
        _createEngine = createEngine;
        _settingsService.SettingsSaved += OnSettingsSavedAsync;
    }

    public IClientEngine Engine => _engine ?? throw new InvalidOperationException("Torrent engine is not initialized.");

    /// <summary>
    /// Whether the configured proxy cost this session DHT or uTP. Read once the engine is built, so
    /// the user can be told rather than left wondering why no peers arrive.
    /// </summary>
    public bool ProxyRestrictionApplied { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _engine ??= CreateEngine();
        // PeerSharp does not persist SSL private keys. Defer restored starts so a missing
        // certificate cannot make the add fail and discard the restored torrent entirely.
        var queueEnabled = _engine.Settings.Queue.Enabled;
        _engine.Settings.Queue.Enabled = false;
        long blocklistElapsedMs;
        try
        {
            await _engine.PauseAsync(cancellationToken).ConfigureAwait(false);
            await _engine.InitializeAsync(cancellationToken).ConfigureAwait(false);

            // While the session is still paused: the blocklist has nowhere to go before the engine
            // is initialized, and after resuming is too late. It is consulted only as a peer is
            // added or connects, so restored torrents that resumed first could reach peers it
            // would have refused, and nothing drops those connections once it arrives.
            var blocklistStartedMs = stopwatch.ElapsedMilliseconds;
            await LoadBlocklistAsync(cancellationToken).ConfigureAwait(false);
            blocklistElapsedMs = stopwatch.ElapsedMilliseconds - blocklistStartedMs;

            await _engine.ResumeAsync(cancellationToken).ConfigureAwait(false);
            KeepSslTorrentsWithoutCertificatesFromAutoStarting(_engine);
        }
        finally
        {
            // Only this, so a failure above is the exception that leaves here. The SSL pass used to
            // run here too, and a throw from it while initialization was already failing replaced
            // the reason startup failed with a secondary one.
            _engine.Settings.Queue.Enabled = queueEnabled;
        }
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var torrentCount = _engine.GetTorrents().Count;
            LogTorrentEngineInitialized(
                _logger,
                stopwatch.ElapsedMilliseconds,
                torrentCount,
                blocklistElapsedMs);
        }
    }

    /// <summary>
    /// Stops the queue retrying restored SSL torrents, which cannot start until a certificate is
    /// supplied again.
    /// </summary>
    /// <remarks>
    /// Only torrents still set to auto-start are examined. A failed resume leaves its torrent set,
    /// and clearing the flag on any other torrent changes nothing - which matters, because the
    /// only way to ask whether a torrent is SSL is <see cref="ITorrent.ExportTorrentFile"/>, which
    /// serializes and re-parses its whole metadata. Asked of every stopped torrent, that was a
    /// startup cost growing with the size of a seeding library, for an answer that was not needed.
    /// </remarks>
    private static void KeepSslTorrentsWithoutCertificatesFromAutoStarting(IClientEngine engine)
    {
        foreach (var torrent in engine.GetTorrents())
        {
            if (!torrent.Started && torrent.QueueAutoStart && torrent.HasMetadata && torrent.ExportTorrentFile().IsSsl)
            {
                torrent.QueueAutoStart = false;
            }
        }
    }

    public async Task LoadOptionalDataAsync(CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await LoadGeoIpAsync(cancellationToken).ConfigureAwait(false);
        LogOptionalDataLoaded(_logger, stopwatch.ElapsedMilliseconds);
    }

    public ValueTask DisposeAsync()
    {
        _settingsService.SettingsSaved -= OnSettingsSavedAsync;
        if (_shutdownTask != null)
        {
            // ShutdownAsync already initiated disposal. In particular, do not
            // synchronously re-wait after the host's shutdown deadline elapsed.
            return ValueTask.CompletedTask;
        }

        var engine = Interlocked.Exchange(ref _engine, null);
        return engine?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        _settingsService.SettingsSaved -= OnSettingsSavedAsync;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var engine = Interlocked.Exchange(ref _engine, null);
        if (engine != null)
        {
            _shutdownTask = engine.DisposeAsync().AsTask();
            _ = ObserveLateShutdownFailureAsync(_shutdownTask);
        }

        if (_shutdownTask != null)
        {
            try
            {
                await _shutdownTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                LogTorrentEngineShutdown(_logger, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogTorrentEngineShutdownDeadlineExceeded(_logger, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    private async Task ObserveLateShutdownFailureAsync(Task shutdownTask)
    {
        try
        {
            await shutdownTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTorrentEngineDisposalFailed(_logger, ex);
        }
    }

    private IClientEngine CreateEngine()
    {
        var settings = _settingsService.Current;
        // Restored torrents can begin transferring during initialization, before the schedule's
        // minute timer has ticked. Start with the limits that are in force now rather than briefly
        // exposing the ordinary limits inside a scheduled window.
        var (downloadLimit, uploadLimit) = BandwidthSchedule.LimitsFor(settings, _timeProvider.GetLocalNow());

        var udpPlan = ProxyUdpPolicy.Decide(settings.Proxy, settings.Network.EnableDht);
        var bindAddress = ParseBindAddress(settings.Network.BindAddress);
        if (udpPlan.RestrictedByProxy)
        {
            // Warned rather than thrown. The engine would refuse to start at all, and an application
            // that will not open is a worse answer to "your proxy cannot carry UDP" than one that
            // opens with less of the network available and says so.
            LogHttpProxyRestrictsUdp(_logger, udpPlan.EnableUtp ? "on" : "off");
            ProxyRestrictionApplied = true;
        }

        var clientSettings = new Settings
        {
            Dht =
            {
                Enabled = udpPlan.EnableDht,
                AnswerInfoHashSampling = settings.Network.AnswerInfoHashSampling
            },
            Files =
            {
                DefaultDownloadPath = settings.Storage.DownloadPath,
                MaxDiskReadSpeed = ToSpeed(settings.Network.MaxDiskReadSpeedBytesPerSecond),
                MaxDiskWriteSpeed = ToSpeed(settings.Network.MaxDiskWriteSpeedBytesPerSecond)
            },
            Connection =
            {
                I2pSamHost = settings.Network.EnableI2p
                    ? NetworkSettings.NormalizeI2pSamHost(settings.Network.I2pSamHost)
                    : null,
                I2pSamPort = NetworkSettings.ClampI2pSamPort(settings.Network.I2pSamPort),
                I2pAllowMixedNetworks = settings.Network.EnableI2p && settings.Network.I2pAllowMixedNetworks,
                TcpPort = GetListeningPort(settings.Network),
                UdpPort = GetListeningPort(settings.Network),
                // Port mapping discovers the interface independently and opens its own sockets, so
                // it cannot honour a single-address kill switch.
                NatPmpPortMapping = bindAddress is null && settings.Network.EnableNatPmp,
                UpnpPortMapping = bindAddress is null && settings.Network.EnableUpnp,
                AllowMultipleConnectionsPerIp = settings.Network.AllowMultipleConnectionsPerIp,
                MaxConnectionsPerIp = Math.Max(0, settings.Network.MaxConnectionsPerIp),
                BindAddress = bindAddress,
                EnableUtpIn = udpPlan.EnableUtp,
                EnableUtpOut = udpPlan.EnableUtp,
                Encryption = ParseEncryption(settings.EncryptionMode)
            },
            Session =
            {
                Enabled = settings.Storage.EnableSessionPersistence,
                SessionPath = settings.Storage.SessionPath
            },
            Queue =
            {
                Enabled = settings.Queue.EnableQueueManagement,
                MaxActiveDownloads = Math.Max(0, settings.Queue.MaxActiveDownloads),
                MaxActiveSeeds = Math.Max(0, settings.Queue.MaxActiveSeeds),
                EnforceAutoStop = true
            },
            Transfer =
            {
                MaxDownloadSpeed = ToSpeed(downloadLimit),
                MaxUploadSpeed = ToSpeed(uploadLimit)
            }
        };
        ApplyStreamingSettings(settings.Streaming, clientSettings.Streaming);
        ApplyEncryptionSettings(settings, clientSettings.Connection);
        ProxyUdpPolicy.ApplySettings(settings.Proxy, clientSettings.Proxy);

        var options = new TorrentClientOptions
        {
            LoggerFactory = _loggerFactory,
            Settings = clientSettings
        };

        return _createEngine(options);
    }

    public void ApplySpeedLimits()
    {
        if (_engine is not { } engine)
        {
            // Nothing running yet, and nothing to do: whatever is stored is read when it is created.
            return;
        }

        // Properties rather than a replacement Transfer object. The engine reads these from its own
        // loops and does not support having a sub-settings object swapped underneath it.
        // Through the schedule, which answers with the ordinary limits outside its window. So this
        // one method is what the settings screen calls when a limit changes and what the clock calls
        // when the window opens, and there is only ever one answer to what the limits are.
        var (download, upload) = BandwidthSchedule.LimitsFor(_settingsService.Current, _timeProvider.GetLocalNow());
        engine.Settings.Transfer.MaxDownloadSpeed = ToSpeed(download);
        engine.Settings.Transfer.MaxUploadSpeed = ToSpeed(upload);
    }

    public void ApplyStreamingSettings()
    {
        if (_engine is { } engine)
        {
            ApplyStreamingSettings(_settingsService.Current.Streaming, engine.Settings.Streaming);
        }
    }

    public void ApplyEncryptionSettings()
    {
        if (_engine is { } engine)
        {
            ApplyEncryptionSettings(_settingsService.Current, engine.Settings.Connection);
        }
    }

    /// <summary>
    /// Puts every setting the running engine can take into force, whoever saved them.
    /// </summary>
    /// <remarks>
    /// The settings screen also applies these the moment a field changes, ahead of its delayed
    /// write; this is for the saves that have no screen behind them. Doing only encryption here
    /// meant a speed limit or streaming policy changed over MCP was stored but ignored until the
    /// next restart.
    /// </remarks>
    private Task OnSettingsSavedAsync(CancellationToken cancellationToken)
    {
        ApplySpeedLimits();
        ApplyStreamingSettings();
        ApplyEncryptionSettings();
        return Task.CompletedTask;
    }

    private static void ApplyEncryptionSettings(AppSettings source, ConnectionSettings target)
    {
        target.Encryption = ParseEncryption(source.EncryptionMode);
        target.IncomingEncryption = ParseEncryptionOverride(source.Network.IncomingEncryptionMode);
        target.OutgoingEncryption = ParseEncryptionOverride(source.Network.OutgoingEncryptionMode);
        target.PreferRc4 = source.Network.PreferRc4;
    }

    private static Encryption? ParseEncryptionOverride(string mode) =>
        NetworkSettings.IsEncryptionMode(mode) ? ParseEncryption(mode) : null;

    private static void ApplyStreamingSettings(Peerfluence.Core.Config.StreamingSettings source, PeerSharp.Config.StreamingSettings target)
    {
        // Clamped again on the way in, rather than trusted: the engine rejects an out-of-range
        // value, and this runs against whatever is in Current, not only against a freshly loaded
        // file. The bounds are the stored ones, so the two cannot disagree.
        target.ReadAheadBytes = Math.Clamp(source.ReadAheadBytes,
            Config.StreamingSettings.MinReadAheadBytes, Config.StreamingSettings.MaxReadAheadBytes);
        target.ReadAheadSeconds = Math.Clamp(source.ReadAheadSeconds, 0, Config.StreamingSettings.MaxReadAheadSeconds);
        target.RequestQueueSecondsWhileBuffering = Math.Clamp(
            source.RequestQueueSecondsWhileBuffering, 0, Config.StreamingSettings.MaxRequestQueueSeconds);
        target.DataWaitTimeoutSeconds = Math.Clamp(source.DataWaitTimeoutSeconds, 0, Config.StreamingSettings.MaxWaitTimeoutSeconds);
        target.MetadataWaitTimeoutSeconds = Math.Clamp(source.MetadataWaitTimeoutSeconds, 0, Config.StreamingSettings.MaxWaitTimeoutSeconds);
    }

    /// <summary>
    /// Clamps a stored limit into what the engine takes. A negative value is meaningless, and the
    /// engine now rejects one with <see cref="ArgumentOutOfRangeException"/> rather than clamping it,
    /// so a stored minus sign would otherwise stop the engine being built at all.
    /// </summary>
    /// <remarks>
    /// The ceiling used to be <c>uint.MaxValue</c>, because the engine took a <c>uint</c>. PeerSharp
    /// 3.2.0 widened every limit to <c>long</c>, so there is no ceiling to clamp to any more: a limit
    /// above 4 GB/s is now carried rather than quietly becoming 4 GB/s.
    /// </remarks>
    private static long ToSpeed(long bytesPerSecond)
    {
        return Math.Max(0, bytesPerSecond);
    }

    private static ushort GetListeningPort(NetworkSettings settings)
    {
        return settings.UseAutomaticListeningPort
            ? (ushort)0
            : (ushort)Math.Clamp(settings.ListeningPort, 1, 65535);
    }

    private static Encryption ParseEncryption(string mode) => mode switch
    {
        "Refuse" => Encryption.Refuse,
        "Require" => Encryption.Require,
        _ => Encryption.Allow
    };

    /// <summary>
    /// Reads the address the engine should bind to, which is what makes a VPN a kill switch rather
    /// than a preference: PeerSharp fails socket creation instead of falling back to an unbound one.
    /// </summary>
    /// <remarks>
    /// Blank means "listen on everything", which the engine spells <see langword="null"/> - passing
    /// it <see cref="System.Net.IPAddress.Any"/> throws, because that is not a single-address
    /// guarantee it could keep. An unparseable address is treated the same as blank rather than
    /// stopping startup, and is warned about where it is entered.
    /// </remarks>
    private static System.Net.IPAddress? ParseBindAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        if (!System.Net.IPAddress.TryParse(address, out var parsed))
        {
            return null;
        }

        return parsed.Equals(System.Net.IPAddress.Any) || parsed.Equals(System.Net.IPAddress.IPv6Any)
            ? null
            : parsed;
    }

    private async Task LoadBlocklistAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current;
        if (!settings.EnableBlocklist || string.IsNullOrWhiteSpace(settings.BlocklistPath))
        {
            return;
        }

        if (!File.Exists(settings.BlocklistPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(settings.BlocklistPath);
            await Engine.LoadBlocklistAsync(stream, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogBlocklistLoadFailed(_logger, ex, settings.BlocklistPath);
        }
    }

    private async Task LoadGeoIpAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current;
        if (!settings.EnableGeoIp || string.IsNullOrWhiteSpace(settings.GeoIpPath))
        {
            return;
        }

        if (!File.Exists(settings.GeoIpPath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(settings.GeoIpPath);
            await Engine.LoadGeoIpAsync(stream, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogGeoIpLoadFailed(_logger, ex, settings.GeoIpPath);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Torrent engine initialized in {ElapsedMs} ms with {TorrentCount} restored torrents (blocklist: {BlocklistElapsedMs} ms)")]
    private static partial void LogTorrentEngineInitialized(
        ILogger logger,
        long elapsedMs,
        int torrentCount,
        long blocklistElapsedMs);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Optional GeoIP data loaded after startup in {ElapsedMs} ms")]
    private static partial void LogOptionalDataLoaded(ILogger logger, long elapsedMs);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Torrent engine shutdown completed in {ElapsedMs} ms")]
    private static partial void LogTorrentEngineShutdown(ILogger logger, long elapsedMs);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Torrent engine shutdown exceeded its deadline after {ElapsedMs} ms; disposal continues in the background")]
    private static partial void LogTorrentEngineShutdownDeadlineExceeded(ILogger logger, long elapsedMs);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Torrent engine disposal failed")]
    private static partial void LogTorrentEngineDisposalFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "An HTTP proxy is configured, which cannot carry UDP. DHT is off for this session and uTP is {UtpState}. Use a SOCKS5 proxy to keep them.")]
    private static partial void LogHttpProxyRestrictsUdp(ILogger logger, string utpState);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Blocklist failed to load from {BlocklistPath}")]
    private static partial void LogBlocklistLoadFailed(ILogger logger, Exception exception, string blocklistPath);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "GeoIP data failed to load from {GeoIpPath}")]
    private static partial void LogGeoIpLoadFailed(ILogger logger, Exception exception, string geoIpPath);
}
