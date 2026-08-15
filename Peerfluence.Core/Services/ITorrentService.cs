using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public interface ITorrentService
{
    IReadOnlyList<ITorrent> GetTorrents();

    EngineStats GetStats();

    Task<ITorrent> AddMagnetAsync(string magnetUri, AddTorrentOptions? options = null, CancellationToken cancellationToken = default);

    Task<ITorrent> AddTorrentAsync(TorrentFile torrentFile, AddTorrentOptions? options = null, CancellationToken cancellationToken = default);

    Task<ITorrent> AddTorrentFileAsync(string torrentPath, AddTorrentOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a torrent published at an http address, by fetching it and handing the engine the bytes.
    ///
    /// <para>
    /// Search results are not files on this machine. Sources that carry a link rather than a magnet
    /// - the Internet Archive and Academic Torrents both do - point at a .torrent on someone's
    /// server, and the engine's loader only reads local paths.
    /// </para>
    /// </summary>
    Task<ITorrent> AddTorrentFromUrlAsync(string url, AddTorrentOptions? options = null, CancellationToken cancellationToken = default);

    Task SaveSessionAsync(CancellationToken cancellationToken = default);

    Task RemoveAsync(ITorrent torrent, RemoveOptions options = RemoveOptions.None, CancellationToken cancellationToken = default);

    void RegisterAlertMask(uint alertMask);

    IAsyncEnumerable<Alert> GetAlertsAsync(TimeSpan? pollingInterval = null, CancellationToken cancellationToken = default);

    void PublishAlert(Alert alert, CancellationToken cancellationToken = default);
}
