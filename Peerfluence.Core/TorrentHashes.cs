using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core;

/// <summary>
/// Which of a torrent's two hashes stands for it when exactly one of them has to.
/// </summary>
/// <remarks>
/// <para>
/// Whether a hash names a torrent is PeerSharp's question and <see cref="TorrentIdentity.HasHash"/>
/// answers it, in every form the world refers to a torrent by. This is the narrower question a
/// display field or a protocol field asks - which single hash to print - and the library has no
/// opinion on it, because it is a presentation choice rather than an identity rule.
/// </para>
/// <para>
/// It still has one right answer. A v1 torrent stores <see cref="InfoHash.Empty"/> as its v2 hash
/// and a v2 torrent stores it as its v1 hash, so reaching for <see cref="ITorrent.Hash"/> alone
/// hands back forty zero characters for every v2 only torrent: a clipboard that copies nothing, a
/// settings key that files every such torrent under the same entry, an RPC id they all share.
/// Kept in one place because a second caller reaching for <c>.Hash</c> is how that happens.
/// </para>
/// </remarks>
public static class TorrentHashes
{
    /// <summary>
    /// The torrent's v1 hash, or its v2 hash when it has no v1 one. Empty only for a torrent
    /// carrying neither, which is not a state the engine hands out: both ways of adding one refuse
    /// input without a usable hash.
    /// </summary>
    public static InfoHash PrimaryHash(this ITorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return torrent.Hash.IsEmpty ? torrent.HashV2 : torrent.Hash;
    }
}
