using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core;

/// <summary>
/// Decides whether two torrents, or a torrent and a hash, are the same thing.
///
/// <para>
/// A torrent carries both a V1 and a V2 hash and almost never has both: a V1 torrent's
/// <see cref="ITorrent.HashV2"/> is <see cref="InfoHash.EmptyV2"/>, a V2 torrent's
/// <see cref="ITorrent.Hash"/> is <see cref="InfoHash.Empty"/>, and only a hybrid has two real
/// ones. Empty hashes are equal to each other, so comparing the pairs directly says every V1
/// torrent is every other V1 torrent. An absent hash is not evidence of identity, and this is the
/// one place that knows it.
/// </para>
/// </summary>
public static class TorrentIdentity
{
    /// <summary>
    /// Whether two hashes name the same torrent. Two empty hashes name nothing, so they do not
    /// match - each other included.
    /// </summary>
    public static bool SameHash(InfoHash left, InfoHash right)
    {
        return !left.IsEmpty && !right.IsEmpty && left == right;
    }

    /// <summary>
    /// Whether <paramref name="torrent"/> is the one <paramref name="hash"/> names, in either
    /// version.
    /// </summary>
    public static bool HasHash(ITorrent torrent, InfoHash hash)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return SameHash(torrent.Hash, hash) || SameHash(torrent.HashV2, hash);
    }

    /// <summary>
    /// Whether two torrents are the same one. Cross-compares the versions so a hybrid torrent known
    /// by one of its hashes still matches itself known by the other.
    /// </summary>
    public static bool SameTorrent(ITorrent left, ITorrent right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        // Identity first, so a torrent with no usable hash at all is still recognised as itself.
        return ReferenceEquals(left, right)
            || SameHash(left.Hash, right.Hash)
            || SameHash(left.Hash, right.HashV2)
            || SameHash(left.HashV2, right.Hash)
            || SameHash(left.HashV2, right.HashV2);
    }
}
