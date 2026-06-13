using Peerfluence.Core.Messaging;
using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public sealed class TorrentService : ITorrentService
{
    private readonly ITorrentEngineService _engineService;
    private readonly IAppMessenger _messenger;

    public TorrentService(ITorrentEngineService engineService, IAppMessenger messenger)
    {
        _engineService = engineService;
        _messenger = messenger;
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

        options ??= new AddTorrentOptions();
        if (string.IsNullOrEmpty(options.DownloadPath))
        {
            options.DownloadPath = _engineService.Engine.Settings.Files.DefaultDownloadPath;
        }

        return await _engineService.Engine.AddMagnetAsync(magnet, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ITorrent> AddTorrentFileAsync(string torrentPath, AddTorrentOptions? options = null, CancellationToken cancellationToken = default)
    {
        var torrentFile = await TorrentFile.LoadAsync(torrentPath, cancellationToken).ConfigureAwait(false);

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

    public void RegisterAlertMask(uint alertMask)
    {
        _engineService.Engine.Alerts.RegisterAlerts(alertMask);
    }

    public IAsyncEnumerable<Alert> GetAlertsAsync(TimeSpan? pollingInterval = null, CancellationToken cancellationToken = default)
    {
        return _engineService.Engine.Alerts.GetAlertsAsync(pollingInterval, cancellationToken);
    }

    public void PublishAlert(Alert alert)
    {
        switch (alert)
        {
            case TorrentAlert torrentAlert:
                _messenger.Publish(new TorrentAlertMessage(torrentAlert.Torrent, alert));
                break;
            case MetadataAlert metadataAlert:
                if (metadataAlert.Id == AlertId.MetadataInitialized)
                {
                    _ = EnsureUniqueDownloadPathAsync(metadataAlert.Torrent);
                }
                _messenger.Publish(new TorrentAlertMessage(metadataAlert.Torrent, alert));
                break;
        }
    }

    private async Task EnsureUniqueDownloadPathAsync(ITorrent torrent)
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
                    await torrent.StopAsync().ConfigureAwait(false);
                }

                if (!IsRegistered(torrent))
                {
                    return;
                }

                await torrent.SetDownloadPathAsync(uniquePath).ConfigureAwait(false);

                if (wasStarted && IsRegistered(torrent))
                {
                    await torrent.StartAsync().ConfigureAwait(false);
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

        return torrents.Any(existing =>
            existing.Hash == torrent.Hash
            || existing.Hash == torrent.HashV2
            || existing.HashV2 == torrent.Hash
            || existing.HashV2 == torrent.HashV2);
    }
}
