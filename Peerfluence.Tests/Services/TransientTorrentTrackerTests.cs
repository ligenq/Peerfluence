using Peerfluence.Services;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

public sealed class TransientTorrentTrackerTests
{
    [Fact]
    public void ShouldSuppress_IsFalseForUntrackedHashes()
    {
        var sut = new TransientTorrentTracker();

        Assert.False(sut.ShouldSuppress(TorrentAlert(Hash(1), AlertId.TorrentAdded)));
    }

    [Fact]
    public void ShouldSuppress_CoversTheWholeLifecycleOfATrackedFetch()
    {
        var hash = Hash(1);
        var sut = new TransientTorrentTracker();

        using var scope = sut.Track(hash);

        Assert.True(sut.ShouldSuppress(TorrentAlert(hash, AlertId.TorrentAdded)));
        Assert.True(sut.ShouldSuppress(MetadataAlert(hash, AlertId.MetadataInitialized)));
        Assert.True(sut.ShouldSuppress(TorrentAlert(hash, AlertId.TorrentRemoved)));
        Assert.False(sut.ShouldSuppress(TorrentAlert(Hash(2), AlertId.TorrentAdded)));
    }

    [Fact]
    public void ShouldSuppress_StillCoversAlertsDrainedAfterTheScopeClosed()
    {
        // The alert queue is polled, so the scope closing says nothing about whether the fetch's
        // own alerts have been read yet.
        var hash = Hash(1);
        var sut = new TransientTorrentTracker();

        sut.Track(hash).Dispose();

        Assert.True(sut.ShouldSuppress(TorrentAlert(hash, AlertId.TorrentAdded)));
        Assert.True(sut.ShouldSuppress(MetadataAlert(hash, AlertId.MetadataInitialized)));
    }

    [Fact]
    public void ShouldSuppress_ReleasesTheHashOnceTheFetchsRemovalIsSeen()
    {
        var hash = Hash(1);
        var sut = new TransientTorrentTracker();

        sut.Track(hash).Dispose();
        Assert.True(sut.ShouldSuppress(TorrentAlert(hash, AlertId.TorrentRemoved)));

        // The user is free to add this hash for real immediately afterwards.
        Assert.False(sut.ShouldSuppress(TorrentAlert(hash, AlertId.TorrentAdded)));
    }

    [Fact]
    public void ShouldSuppress_KeepsSuppressingWhileAnotherFetchOfTheSameHashIsStillRunning()
    {
        var hash = Hash(1);
        var sut = new TransientTorrentTracker();

        var first = sut.Track(hash);
        using var second = sut.Track(hash);
        first.Dispose();

        Assert.True(sut.ShouldSuppress(TorrentAlert(hash, AlertId.TorrentRemoved)));
        Assert.True(sut.ShouldSuppress(TorrentAlert(hash, AlertId.TorrentAdded)));
    }

    [Fact]
    public void ShouldSuppress_GivesUpOnAFetchThatNeverProducedARemoval()
    {
        // An add that throws before registering - the user already holds this hash - produces no
        // alerts at all, so there is no removal to release on.
        var hash = Hash(1);
        var timeProvider = new AdvanceableTimeProvider();
        var sut = new TransientTorrentTracker(timeProvider);

        sut.Track(hash).Dispose();
        timeProvider.Advance(TransientTorrentTracker.RetentionAfterCompletion + TimeSpan.FromSeconds(1));

        Assert.False(sut.ShouldSuppress(TorrentAlert(hash, AlertId.TorrentAdded)));
    }

    [Fact]
    public void ShouldSuppress_MatchesOnTheV2HashOfAV2OnlyTorrent()
    {
        var hashV2 = Hash(2);
        var sut = new TransientTorrentTracker();

        using var scope = sut.Track(hashV2);
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(InfoHash.Empty);
        torrent.HashV2.Returns(hashV2);

        Assert.True(sut.ShouldSuppress(new SimpleTorrentAlert { Id = AlertId.TorrentAdded, Torrent = torrent }));
    }

    [Fact]
    public void ShouldSuppress_IgnoresAlertsThatCarryNoTorrent()
    {
        var sut = new TransientTorrentTracker();

        Assert.False(sut.ShouldSuppress(new ConfigAlert { Id = AlertId.ConfigChanged, ConfigType = "Files" }));
    }

    [Fact]
    public void Track_IgnoresAnEmptyHashRatherThanMatchingEveryTorrent()
    {
        var sut = new TransientTorrentTracker();

        using var scope = sut.Track(InfoHash.Empty);

        Assert.False(sut.ShouldSuppress(TorrentAlert(Hash(1), AlertId.TorrentAdded)));
    }

    private static InfoHash Hash(byte seed)
    {
        var bytes = new byte[20];
        Array.Fill(bytes, seed);
        return new InfoHash(bytes);
    }

    private static Alert TorrentAlert(InfoHash hash, AlertId id)
    {
        return new SimpleTorrentAlert { Id = id, Torrent = TorrentWith(hash) };
    }

    private static Alert MetadataAlert(InfoHash hash, AlertId id)
    {
        return new SimpleMetadataAlert { Id = id, Torrent = TorrentWith(hash) };
    }

    private static ITorrent TorrentWith(InfoHash hash)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(hash);
        torrent.HashV2.Returns(InfoHash.Empty);
        return torrent;
    }

    private sealed class AdvanceableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
