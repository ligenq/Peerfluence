using PeerSharp.Core;

namespace Peerfluence.Core.Services.Rpc;

/// <summary>What a torrent was last seen transferring.</summary>
/// <param name="DownloadSpeed">Bytes per second, as of the last alert.</param>
/// <param name="UploadSpeed">Bytes per second, as of the last alert.</param>
/// <param name="Downloaded">Bytes received for this torrent.</param>
/// <param name="Uploaded">Bytes sent for this torrent.</param>
/// <param name="ConnectedPeers">How many peers were connected.</param>
public readonly record struct TorrentTransferSnapshot(
    long DownloadSpeed,
    long UploadSpeed,
    long Downloaded,
    long Uploaded,
    int ConnectedPeers);

/// <summary>
/// The most recent transfer figures per torrent.
///
/// <para>
/// Exists because rates are not readable from a torrent - they arrive as alerts, and the list on
/// screen keeps its own copy by listening to them. Anything else that needs to answer "how fast is
/// this going" has nowhere to look, and a remote client polling every few seconds is exactly that.
/// </para>
/// </summary>
public interface ITorrentTransferSnapshots
{
    /// <summary>The last figures seen for a torrent, or an empty snapshot if none have arrived.</summary>
    TorrentTransferSnapshot GetSnapshot(InfoHash hash);

    void Record(InfoHash hash, TorrentTransferSnapshot snapshot);
}
