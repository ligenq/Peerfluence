using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public interface ITorrentService
{
    IReadOnlyList<ITorrent> GetTorrents();

    EngineStats GetStats();

    Task<ITorrent> AddMagnetAsync(string magnetUri, AddTorrentOptions? options = null, CancellationToken cancellationToken = default);

    Task<ITorrent> AddTorrentFileAsync(string torrentPath, AddTorrentOptions? options = null, CancellationToken cancellationToken = default);

    Task SaveSessionAsync(CancellationToken cancellationToken = default);

    Task RemoveAsync(ITorrent torrent, RemoveOptions options = RemoveOptions.None, CancellationToken cancellationToken = default);

    void RegisterAlertMask(uint alertMask);

    IAsyncEnumerable<Alert> GetAlertsAsync(TimeSpan? pollingInterval = null, CancellationToken cancellationToken = default);

    void PublishAlert(Alert alert);
}
