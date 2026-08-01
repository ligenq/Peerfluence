using System;

namespace Peerfluence.Services;

/// <summary>
/// Knows which info hashes currently belong to a torrent the engine added on our behalf purely to
/// read metadata, rather than to one the user asked for.
///
/// <para>
/// <c>IClientEngine.GetMagnetMetadataAsync</c> works by adding the magnet, waiting for its metadata
/// and removing it again. That transient torrent is indistinguishable from a real one to everything
/// downstream of the alert queue, so without this it shows up in the downloads list, raises a
/// "metadata ready" notification, and gets its download path rewritten. Tracking the hash for the
/// duration of the fetch lets <see cref="TorrentAlertsHostedService"/> drop its alerts before any of
/// that happens.
/// </para>
/// </summary>
public interface ITransientTorrentTracker
{
    /// <summary>
    /// Marks <paramref name="infoHash"/> as belonging to a metadata fetch until the returned scope is
    /// disposed. Must be entered before the fetch starts: the engine raises
    /// <see cref="PeerSharp.Core.AlertId.TorrentAdded"/> from inside the add itself.
    /// </summary>
    IDisposable Track(InfoHash infoHash);

    /// <summary>
    /// True if <paramref name="alert"/> belongs to a tracked metadata fetch and should not reach the
    /// rest of the application. Stateful: observing the fetch's final
    /// <see cref="PeerSharp.Core.AlertId.TorrentRemoved"/> is what releases the hash again.
    /// </summary>
    bool ShouldSuppress(Alert alert);
}
