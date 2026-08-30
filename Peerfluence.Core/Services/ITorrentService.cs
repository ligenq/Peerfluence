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

    /// <summary>
    /// Whether the session is paused - see <see cref="PauseSessionAsync"/>.
    /// </summary>
    bool IsSessionPaused { get; }

    /// <summary>
    /// Stops every running torrent at once, remembering which were running.
    /// </summary>
    /// <remarks>
    /// For "stop everything now": the machine is going to sleep, a metered connection came up, a
    /// video call started. <see cref="ResumeSessionAsync"/> starts again exactly the torrents this
    /// stopped, leaving alone any the user had already stopped by hand - which is what stopping and
    /// starting them one by one could not do without keeping that list somewhere.
    /// </remarks>
    Task PauseSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts the torrents <see cref="PauseSessionAsync"/> stopped.</summary>
    Task ResumeSessionAsync(CancellationToken cancellationToken = default);

    void RegisterAlertMask(uint alertMask);

    IAsyncEnumerable<Alert> GetAlertsAsync(TimeSpan? pollingInterval = null, CancellationToken cancellationToken = default);

    void PublishAlert(Alert alert, CancellationToken cancellationToken = default);
}
