using Peerfluence.Core;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Which hash stands for a torrent in a field that only has room for one.
/// </summary>
/// <remarks>
/// Every case here is one that reading <see cref="ITorrent.Hash"/> alone got wrong: a clipboard
/// that copied forty zeroes, a category filed under the empty hash and therefore dropped, an RPC id
/// shared by every v2 only torrent in the session.
/// </remarks>
public sealed class TorrentHashesTests
{
    [Fact]
    public void AV1Torrent_IsNamedByItsV1Hash()
    {
        Assert.Equal(V1(1), Torrent(V1(1), InfoHash.EmptyV2).PrimaryHash());
    }

    [Fact]
    public void AV2OnlyTorrent_IsNamedByItsV2Hash_RatherThanByTheEmptyOneItStores()
    {
        var torrent = Torrent(InfoHash.Empty, V2(7));

        Assert.Equal(V2(7), torrent.PrimaryHash());
        Assert.False(torrent.PrimaryHash().IsEmpty);
    }

    [Fact]
    public void AHybridTorrent_IsNamedByItsV1Hash()
    {
        // Both are real, and the v1 one is what a twenty byte field elsewhere can hold.
        Assert.Equal(V1(3), Torrent(V1(3), V2(4)).PrimaryHash());
    }

    [Fact]
    public void TwoV2OnlyTorrents_AreNamedDistinctly()
    {
        // The whole point: reading .Hash gave both of them InfoHash.Empty, so anything keyed by it
        // treated them as one torrent.
        Assert.NotEqual(
            Torrent(InfoHash.Empty, V2(1)).PrimaryHash(),
            Torrent(InfoHash.Empty, V2(2)).PrimaryHash());
    }

    [Fact]
    public void ATorrentWithNeitherHash_IsEmpty_WhichIsTheHonestAnswer()
    {
        // Not a state the engine hands out - both ways of adding a torrent refuse input with no
        // usable hash - but saying "empty" beats inventing an identity for it.
        Assert.True(Torrent(InfoHash.Empty, InfoHash.EmptyV2).PrimaryHash().IsEmpty);
    }

    [Fact]
    public void NoTorrentAtAll_Throws_RatherThanAnsweringEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => ((ITorrent)null!).PrimaryHash());
    }

    private static InfoHash V1(byte seed) => new(Enumerable.Repeat(seed, InfoHash.V1Length).ToArray());

    private static InfoHash V2(byte seed) => new(Enumerable.Repeat(seed, InfoHash.V2Length).ToArray());

    private static ITorrent Torrent(InfoHash hash, InfoHash hashV2)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(hash);
        torrent.HashV2.Returns(hashV2);
        return torrent;
    }
}
