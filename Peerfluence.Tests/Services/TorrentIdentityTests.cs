using Peerfluence.Core;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

public sealed class TorrentIdentityTests
{
    [Fact]
    public void SameTorrent_IsFalseForTwoV1Torrents_WhoseAbsentV2HashesAreBothEmpty()
    {
        // The defect this type exists for: an empty hash equals an empty hash, so comparing the
        // pairs directly made every V1 torrent match every other one.
        var left = Torrent(V1(1), InfoHash.EmptyV2);
        var right = Torrent(V1(2), InfoHash.EmptyV2);

        Assert.False(TorrentIdentity.SameTorrent(left, right));
    }

    [Fact]
    public void SameTorrent_IsFalseForTwoV2Torrents_WhoseAbsentV1HashesAreBothEmpty()
    {
        var left = Torrent(InfoHash.Empty, V2(1));
        var right = Torrent(InfoHash.Empty, V2(2));

        Assert.False(TorrentIdentity.SameTorrent(left, right));
    }

    [Fact]
    public void SameTorrent_IsTrueForATorrentComparedWithItself()
    {
        var torrent = Torrent(V1(1), InfoHash.EmptyV2);

        Assert.True(TorrentIdentity.SameTorrent(torrent, torrent));
        Assert.True(TorrentIdentity.SameTorrent(torrent, Torrent(V1(1), InfoHash.EmptyV2)));
    }

    [Fact]
    public void SameTorrent_RecognisesAHybridByEitherOfItsHashes()
    {
        var hybrid = Torrent(V1(1), V2(1));

        Assert.True(TorrentIdentity.SameTorrent(hybrid, Torrent(V1(1), InfoHash.EmptyV2)));
        Assert.True(TorrentIdentity.SameTorrent(hybrid, Torrent(InfoHash.Empty, V2(1))));
        Assert.False(TorrentIdentity.SameTorrent(hybrid, Torrent(V1(2), V2(2))));
    }

    [Fact]
    public void SameTorrent_IsTrueForTheSameInstanceEvenWithoutAnyHash()
    {
        var unnamed = Torrent(InfoHash.Empty, InfoHash.EmptyV2);

        Assert.True(TorrentIdentity.SameTorrent(unnamed, unnamed));
        Assert.False(TorrentIdentity.SameTorrent(unnamed, Torrent(InfoHash.Empty, InfoHash.EmptyV2)));
    }

    [Fact]
    public void HasHash_MatchesEitherVersion_ButNeverAnEmptyOne()
    {
        var torrent = Torrent(V1(1), InfoHash.EmptyV2);

        Assert.True(TorrentIdentity.HasHash(torrent, V1(1)));
        Assert.False(TorrentIdentity.HasHash(torrent, V1(2)));
        Assert.False(TorrentIdentity.HasHash(torrent, InfoHash.EmptyV2));
        Assert.False(TorrentIdentity.HasHash(torrent, InfoHash.Empty));
    }

    [Fact]
    public void SameHash_TreatsAnEmptyHashAsNamingNothing()
    {
        Assert.False(TorrentIdentity.SameHash(InfoHash.Empty, InfoHash.Empty));
        Assert.False(TorrentIdentity.SameHash(InfoHash.EmptyV2, InfoHash.EmptyV2));
        Assert.True(TorrentIdentity.SameHash(V1(1), V1(1)));
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
