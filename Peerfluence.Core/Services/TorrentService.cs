using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Messaging;
using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public sealed partial class TorrentService : ITorrentService
{
    private readonly ITorrentEngineService _engineService;
    private readonly IAppMessenger _messenger;
    private readonly IAppSettingsService _settings;

    private readonly HttpClient _httpClient;
    private readonly ILogger<TorrentService> _logger;

    public TorrentService(
        ITorrentEngineService engineService,
        IAppMessenger messenger,
        HttpClient httpClient,
        IAppSettingsService settings,
        ILogger<TorrentService>? logger = null)
    {
        _engineService = engineService;
        _messenger = messenger;
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger ?? NullLogger<TorrentService>.Instance;
    }

    /// <summary>
    /// How long a start is waited for before it is left to finish on its own. Long enough to hear
    /// about a start that cannot happen, which fails at once, so those still reach the caller.
    /// </summary>
    internal TimeSpan StartWait { get; init; } = TimeSpan.FromSeconds(2);

    public IReadOnlyList<ITorrent> GetTorrents()
    {
        return _engineService.Engine.GetTorrents();
    }

    public async Task SetSslCertificateAsync(ITorrent torrent,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        ArgumentNullException.ThrowIfNull(certificate);
        if (torrent.State != TorrentState.Stopped) throw new InvalidOperationException("Stop the torrent before changing its SSL certificate.");
        // Not redundant with the guard above. Stopped is what the caller has to ask for; this closes
        // the gap after it, where a queue that still saw auto-start set could have started the
        // torrent again, and the engine refuses a certificate for a running torrent.
        torrent.QueueAutoStart = false;
        await torrent.StopAsync(cancellationToken).ConfigureAwait(false);
        await torrent.SetSslCertificateAsync(certificate, cancellationToken).ConfigureAwait(false);
    }

    public EngineStats GetStats()
    {
        try
        {
            return _engineService.Engine.GetStats();
        }
        catch (ObjectDisposedException)
        {
            return new EngineStats();
        }
        catch (InvalidOperationException)
        {
            return new EngineStats();
        }
    }

    public async Task<ITorrent> AddMagnetAsync(string magnetUri, AddTorrentOptions? options = null, CancellationToken cancellationToken = default)
    {
        var magnet = MagnetLink.Parse(magnetUri);
        if (!HasUsableInfoHash(magnet))
        {
            throw new NotSupportedException(MagnetWithoutInfoHashMessage);
        }

        options ??= new AddTorrentOptions();
        if (string.IsNullOrEmpty(options.DownloadPath))
        {
            options.DownloadPath = _engineService.Engine.Settings.Files.DefaultDownloadPath;
        }

        ApplySeedingDefaults(options);

        return await AddThenStartAsync(
            options,
            () => _engineService.Engine.AddMagnetAsync(magnet, options, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shown when a magnet names no torrent we can fetch. In practice that means a BEP 46
    /// self-updating link (<c>xs=urn:btpk:</c>), whose current info hash lives in the DHT rather
    /// than in the link.
    /// </summary>
    public const string MagnetWithoutInfoHashMessage =
        "This magnet link is a self-updating (BEP 46) link and carries no info hash. Peerfluence cannot add these yet.";

    /// <summary>
    /// Whether a parsed magnet names something addable. PeerSharp accepts BEP 46 links that carry a
    /// public key and no info hash; adding one would register a torrent under an empty hash, which
    /// can never fetch metadata and collides with the next such link.
    /// </summary>
    public static bool HasUsableInfoHash(MagnetLink magnet)
    {
        ArgumentNullException.ThrowIfNull(magnet);
        return !magnet.InfoHash.IsEmpty || !magnet.InfoHashV2.IsEmpty;
    }

    public async Task<ITorrent> AddTorrentFileAsync(string torrentPath, AddTorrentOptions? options = null, CancellationToken cancellationToken = default)
    {
        var torrentFile = await TorrentFile.LoadAsync(torrentPath, cancellationToken).ConfigureAwait(false);

        return await AddTorrentAsync(torrentFile, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ITorrent> AddTorrentFromUrlAsync(string url, AddTorrentOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await _httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);

        // Parsed here rather than written to a temporary file and loaded back: the bytes are already
        // in hand, and something that turns out not to be a torrent should fail before anything
        // touches the disk.
        var torrentFile = TorrentFile.Parse(data);

        return await AddTorrentAsync(torrentFile, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ITorrent> AddTorrentAsync(TorrentFile torrentFile, AddTorrentOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(torrentFile);
        options ??= new AddTorrentOptions();
        if (string.IsNullOrEmpty(options.DownloadPath))
        {
            var basePath = _engineService.Engine.Settings.Files.DefaultDownloadPath;
            options.DownloadPath = Path.Combine(basePath, torrentFile.Name);
        }

        ApplySeedingDefaults(options);

        return await AddThenStartAsync(
            options,
            () => _engineService.Engine.AddTorrentAsync(torrentFile, options, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a torrent stopped and then starts it, rather than letting the engine do both.
    /// </summary>
    /// <remarks>
    /// An add that starts the torrent returns only after hashing any files already at its
    /// destination. Left together, one torrent added over 100 GB of existing data would hold the add
    /// dialog or a Transmission client's request for as long as the disk takes. A start that fails
    /// straight away still fails the add, as it does in the engine, so nothing that could not start
    /// is left in the list.
    /// </remarks>
    private async Task<ITorrent> AddThenStartAsync(AddTorrentOptions options, Func<Task<ITorrent>> add, CancellationToken cancellationToken)
    {
        var startImmediately = options.StartImmediately;
        options.StartImmediately = false;
        ITorrent torrent;
        try
        {
            torrent = await add().ConfigureAwait(false);
        }
        finally
        {
            options.StartImmediately = startImmediately;
        }

        if (!startImmediately)
        {
            return torrent;
        }

        try
        {
            await StartAsync(torrent, cancellationToken).ConfigureAwait(false);
            return torrent;
        }
        catch
        {
            try
            {
                await RemoveAsync(torrent, RemoveOptions.None, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception removeFailure)
            {
                // Kept out of the way of the failure leaving here, which is the reason.
                LogFailedAddNotDiscarded(_logger, removeFailure, torrent.Name);
            }

            throw;
        }
    }

    /// <summary>
    /// Gives a torrent the seeding goals it was not given.
    /// </summary>
    /// <remarks>
    /// Every way into the application arrives here - the add dialog, a magnet from the browser, the
    /// Transmission RPC endpoint, the MCP tools - so this is the one place a default can be applied
    /// to all of them. Only where nothing was said: a torrent added with a ratio of its own keeps it.
    /// </remarks>
    private void ApplySeedingDefaults(AddTorrentOptions options)
    {
        var seeding = _settings.Current.Seeding;

        if (seeding.LimitRatio && float.IsFinite(seeding.RatioLimit) && seeding.RatioLimit >= 0)
        {
            options.RatioLimit ??= seeding.RatioLimit;
        }

        if (seeding.LimitSeedTime && seeding.SeedTimeLimitMinutes >= 0)
        {
            options.SeedTimeLimit ??= TimeSpan.FromMinutes(seeding.SeedTimeLimitMinutes);
        }
    }

    public async Task StartAsync(ITorrent torrent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        // Already on its way. Another start would wait out the check and then fail as already started.
        if (torrent.State == TorrentState.CheckingFiles)
        {
            return;
        }

        var start = torrent.StartAsync(CancellationToken.None);
        try
        {
            // Only the wait is the caller's to cancel. A Transmission client that gives up on its
            // request has not asked for the torrent to stop.
            await start.WaitAsync(StartWait, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!start.IsCompleted)
        {
            _ = ReportLateStartFailureAsync(torrent, start);
        }
    }

    private async Task ReportLateStartFailureAsync(ITorrent torrent, Task start)
    {
        try
        {
            await start.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogLateStartFailed(_logger, ex, torrent.Name);
            _messenger.Publish(new TorrentStartFailedMessage(torrent, ex));
        }
    }

    public async Task StopAsync(ITorrent torrent, CancellationToken cancellationToken = default)
    {
        // The stop ends the check, and the start it ends has already set auto-start, so left set the
        // queue would start the torrent the person just stopped.
        if (torrent.State == TorrentState.CheckingFiles)
        {
            torrent.QueueAutoStart = false;
        }

        await torrent.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveSessionAsync(CancellationToken cancellationToken = default)
    {
        return _engineService.Engine.SaveSessionAsync(cancellationToken);
    }

    public static Task<int> ForceRecheckAsync(ITorrent torrent, IProgress<PeerSharp.Core.PieceCheckProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return torrent.ForceRecheckAsync(progress, cancellationToken);
    }

    public Task RemoveAsync(ITorrent torrent, RemoveOptions options = RemoveOptions.None, CancellationToken cancellationToken = default)
    {
        return _engineService.Engine.RemoveTorrentAsync(torrent, options, cancellationToken);
    }

    public bool IsSessionPaused
    {
        get
        {
            try
            {
                return _engineService.Engine.IsPaused;
            }
            catch (InvalidOperationException)
            {
                // Asked before the engine exists, which the toolbar does on the first bind.
                return false;
            }
        }
    }

    public Task PauseSessionAsync(CancellationToken cancellationToken = default)
    {
        return _engineService.Engine.PauseAsync(cancellationToken);
    }

    public Task ResumeSessionAsync(CancellationToken cancellationToken = default)
    {
        return _engineService.Engine.ResumeAsync(cancellationToken);
    }

    public void RegisterAlertMask(uint alertMask)
    {
        _engineService.Engine.Alerts.RegisterAlerts(alertMask);
    }

    public IAsyncEnumerable<Alert> GetAlertsAsync(TimeSpan? pollingInterval = null, CancellationToken cancellationToken = default)
    {
        return _engineService.Engine.Alerts.GetAlertsAsync(pollingInterval, cancellationToken);
    }

    public void PublishAlert(Alert alert, CancellationToken cancellationToken = default)
    {
        switch (alert)
        {
            case TorrentAlert torrentAlert:
                _messenger.Publish(new TorrentAlertMessage(torrentAlert.Torrent, alert));
                break;
            case MetadataAlert metadataAlert:
                if (metadataAlert.Id == AlertId.MetadataInitialized)
                {
                    _ = EnsureUniqueDownloadPathAsync(metadataAlert.Torrent, cancellationToken);
                }
                _messenger.Publish(new TorrentAlertMessage(metadataAlert.Torrent, alert));
                break;

            // Everything that is about the session rather than a torrent - the listener that could
            // not bind the configured port, so far. Dropped entirely before there was anywhere to
            // put it.
            default:
                _messenger.Publish(new EngineAlertMessage(alert));
                break;
        }
    }

    /// <summary>
    /// Moves a torrent that landed in the download root into its own folder, once metadata names it.
    ///
    /// <para>
    /// Runs unawaited, so the token is what stops a stop/re-path/restart sequence from continuing
    /// into engine shutdown and losing the race against disposal.
    /// </para>
    /// </summary>
    private async Task EnsureUniqueDownloadPathAsync(ITorrent torrent, CancellationToken cancellationToken)
    {
        try
        {
            var currentPath = torrent.Files.DownloadPath;
            var defaultRoot = _engineService.Engine.Settings.Files.DefaultDownloadPath;

            if (string.Equals(currentPath, defaultRoot, StringComparison.OrdinalIgnoreCase) && IsRegistered(torrent))
            {
                var uniquePath = Path.Combine(defaultRoot, torrent.Name);

                bool wasStarted = torrent.Started;
                if (wasStarted)
                {
                    await torrent.StopAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!IsRegistered(torrent))
                {
                    return;
                }

                await torrent.SetDownloadPathAsync(uniquePath, cancellationToken).ConfigureAwait(false);

                if (wasStarted && IsRegistered(torrent))
                {
                    await torrent.StartAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Best-effort
        }
    }

    private bool IsRegistered(ITorrent torrent)
    {
        var torrents = _engineService.Engine.GetTorrents();
        if (torrents == null)
        {
            return true;
        }

        return torrents.Any(existing => TorrentIdentity.SameTorrent(existing, torrent));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Could not start torrent {Name} after checking its files")]
    private static partial void LogLateStartFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Could not remove torrent {Name} after its start failed")]
    private static partial void LogFailedAddNotDiscarded(ILogger logger, Exception exception, string name);
}
