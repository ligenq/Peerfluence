using Microsoft.Extensions.Logging;
using Peerfluence.Core.Config;
using PeerSharp.Clients;
using PeerSharp.Config;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public sealed class TorrentEngineService : ITorrentEngineService
{
    private readonly IAppSettingsService _settingsService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TorrentEngineService> _logger;
    private IClientEngine? _engine;
    private Task? _shutdownTask;

    public TorrentEngineService(IAppSettingsService settingsService, ILoggerFactory loggerFactory)
    {
        _settingsService = settingsService;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TorrentEngineService>();
    }

    public IClientEngine Engine => _engine ?? throw new InvalidOperationException("Torrent engine is not initialized.");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _engine ??= CreateEngine();
        await _engine.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var engineElapsedMs = stopwatch.ElapsedMilliseconds;
        await LoadBlocklistAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Torrent engine initialized in {ElapsedMs} ms with {TorrentCount} restored torrents (blocklist: {BlocklistElapsedMs} ms)",
            stopwatch.ElapsedMilliseconds,
            _engine.GetTorrents().Count,
            stopwatch.ElapsedMilliseconds - engineElapsedMs);
    }

    public async Task LoadOptionalDataAsync(CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await LoadGeoIpAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Optional GeoIP data loaded after startup in {ElapsedMs} ms",
            stopwatch.ElapsedMilliseconds);
    }

    public ValueTask DisposeAsync()
    {
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
                _logger.LogInformation("Torrent engine shutdown completed in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Torrent engine shutdown exceeded its deadline after {ElapsedMs} ms; disposal continues in the background", stopwatch.ElapsedMilliseconds);
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
            _logger.LogError(ex, "Torrent engine disposal failed");
        }
    }

    private IClientEngine CreateEngine()
    {
        var settings = _settingsService.Current;

        var clientSettings = new Settings
        {
            Dht = new DhtSettings
            {
                Enabled = settings.Network.EnableDht,
                AnswerInfoHashSampling = settings.Network.AnswerInfoHashSampling
            },
            Files = new FilesSettings
            {
                DefaultDownloadPath = settings.Storage.DownloadPath,
                MaxDiskReadSpeed = (uint)Math.Max(0, settings.Network.MaxDiskReadSpeedBytesPerSecond),
                MaxDiskWriteSpeed = (uint)Math.Max(0, settings.Network.MaxDiskWriteSpeedBytesPerSecond)
            },
            Connection = new ConnectionSettings
            {
                TcpPort = GetListeningPort(settings.Network),
                UdpPort = GetListeningPort(settings.Network),
                NatPmpPortMapping = settings.Network.EnableNatPmp,
                UpnpPortMapping = settings.Network.EnableUpnp,
                AllowMultipleConnectionsPerIp = settings.Network.AllowMultipleConnectionsPerIp,
                Encryption = ParseEncryption(settings.EncryptionMode)
            },
            Session = new SessionSettings
            {
                Enabled = settings.Storage.EnableSessionPersistence,
                SessionPath = settings.Storage.SessionPath
            },
            Queue = new PeerSharp.Config.QueueSettings
            {
                Enabled = settings.Queue.EnableQueueManagement,
                MaxActiveDownloads = settings.Queue.MaxActiveDownloads,
                MaxActiveSeeds = settings.Queue.MaxActiveSeeds,
                EnforceAutoStop = true
            },
            Transfer = new TransferSettings
            {
                MaxDownloadSpeed = ToSpeed(settings.Network.MaxDownloadSpeedBytesPerSecond),
                MaxUploadSpeed = ToSpeed(settings.Network.MaxUploadSpeedBytesPerSecond)
            },
            Proxy = CreateProxySettings(settings.Proxy)
        };

        var options = new TorrentClientOptions
        {
            LoggerFactory = _loggerFactory,
            Settings = clientSettings
        };

        return ClientEngineFactory.Create(options);
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
        var network = _settingsService.Current.Network;
        engine.Settings.Transfer.MaxDownloadSpeed = ToSpeed(network.MaxDownloadSpeedBytesPerSecond);
        engine.Settings.Transfer.MaxUploadSpeed = ToSpeed(network.MaxUploadSpeedBytesPerSecond);
    }

    /// <summary>
    /// Clamps a stored limit into what the engine takes. A negative value is meaningless and would
    /// wrap into an enormous limit rather than an absent one, which is the opposite of what someone
    /// typing a minus sign meant.
    /// </summary>
    private static uint ToSpeed(long bytesPerSecond)
    {
        return (uint)Math.Clamp(bytesPerSecond, 0, uint.MaxValue);
    }

    private static ushort GetListeningPort(NetworkSettings settings)
    {
        return settings.UseAutomaticListeningPort
            ? (ushort)0
            : (ushort)Math.Clamp(settings.ListeningPort, 1, 65535);
    }

    private static PeerSharp.Config.ProxySettings CreateProxySettings(Config.ProxySettings proxy)
    {
        var proxySettings = new PeerSharp.Config.ProxySettings
        {
            Type = ParseProxyType(proxy.ProxyType),
            Host = proxy.ProxyHost,
            Port = (ushort)Math.Clamp(proxy.ProxyPort, 0, 65535),
            Username = proxy.ProxyUsername,
            Password = proxy.ProxyPassword,
            ProxyPeers = proxy.ProxyPeers,
            ProxyTrackers = proxy.ProxyTrackers
        };
        return proxySettings;
    }

    private static Encryption ParseEncryption(string mode) => mode switch
    {
        "Refuse" => Encryption.Refuse,
        "Require" => Encryption.Require,
        _ => Encryption.Allow
    };

    private static ProxyType ParseProxyType(string type) => type switch
    {
        "Socks5" => ProxyType.Socks5,
        "Http" => ProxyType.Http,
        _ => ProxyType.None
    };

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
            _logger.LogWarning(ex, "Blocklist failed to load from {BlocklistPath}", settings.BlocklistPath);
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
            _logger.LogWarning(ex, "GeoIP data failed to load from {GeoIpPath}", settings.GeoIpPath);
        }
    }
}
