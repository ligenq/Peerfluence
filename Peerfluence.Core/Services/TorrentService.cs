using Peerfluence.Core.Messaging;
using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public sealed class TorrentService : ITorrentService
{
    private readonly ITorrentEngineService _engineService;
    private readonly IAppMessenger _messenger;

    private readonly HttpClient _httpClient;

    public TorrentService(ITorrentEngineService engineService, IAppMessenger messenger, HttpClient httpClient)
    {
        _engineService = engineService;
        _messenger = messenger;
        _httpClient = httpClient;
    }

    public IReadOnlyList<ITorrent> GetTorrents()
    {
        return _engineService.Engine.GetTorrents();
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

        return await _engineService.Engine.AddMagnetAsync(magnet, options, cancellationToken).ConfigureAwait(false);
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

        return await _engineService.Engine.AddTorrentAsync(torrentFile, options, cancellationToken).ConfigureAwait(false);
    }

    public static Task StartAsync(ITorrent torrent, CancellationToken cancellationToken = default)
    {
        return torrent.StartAsync(cancellationToken);
    }

    public static Task StopAsync(ITorrent torrent, CancellationToken cancellationToken = default)
    {
        return torrent.StopAsync(cancellationToken);
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
}
