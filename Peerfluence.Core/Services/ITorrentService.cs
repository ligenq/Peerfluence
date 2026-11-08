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

    /// <summary>
    /// Gives a stopped SSL torrent its certificate, which it keeps a copy of, so the caller may
    /// dispose this one. The torrent stays stopped.
    /// </summary>
    Task SetSslCertificateAsync(ITorrent torrent,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a torrent, returning once it is running or once it is clear it will be a while. Asking
    /// for a torrent that is already running is not a failure.
    /// </summary>
    /// <remarks>
    /// A first start over files already on disk hashes them before it transfers anything, which can
    /// take minutes. Everything a start can be refused for fails at once and is thrown here; the
    /// start carries on after this returns, and a failure that late is published as
    /// <see cref="Messaging.TorrentStartFailedMessage"/>. Cancelling gives up the wait, not the start.
    /// </remarks>
    Task StartAsync(ITorrent torrent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a torrent because someone asked to, including one still checking its files before it
    /// starts, and keeps the queue from starting it again until someone asks for that.
    /// </summary>
    Task StopAsync(ITorrent torrent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a stopped torrent is waiting for the queue, which will start it when there is room
    /// unless it is stopped too.
    /// </summary>
    bool IsQueued(ITorrent torrent);

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

    /// <summary>
    /// Starts the torrents <see cref="PauseSessionAsync"/> stopped, returning once the session has
    /// resumed. Any of them that have files to check carry on after this returns, as
    /// <see cref="StartAsync"/> does.
    /// </summary>
    Task ResumeSessionAsync(CancellationToken cancellationToken = default);

    void RegisterAlertMask(uint alertMask);

    IAsyncEnumerable<Alert> GetAlertsAsync(TimeSpan? pollingInterval = null, CancellationToken cancellationToken = default);

    void PublishAlert(Alert alert, CancellationToken cancellationToken = default);
}
