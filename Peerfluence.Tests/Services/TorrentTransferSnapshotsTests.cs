using Peerfluence.Core.Services.Rpc;
using PeerSharp.Core;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Written because mutation testing found this class had no tests at all: every mutant in it was
/// unreached, which is a quieter way of saying nothing here was checked.
/// </summary>
public sealed class TorrentTransferSnapshotsTests
{
    private static readonly InfoHash Hash = InfoHash.FromHex("AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555");
    private static readonly InfoHash Other = InfoHash.FromHex("FFFF6666AAAA7777BBBB8888CCCC9999DDDD0000");

    [Fact]
    public void WhatWasRecorded_IsWhatComesBack()
    {
        var sut = new TorrentTransferSnapshots();

        sut.Record(Hash, new TorrentTransferSnapshot(1024, 512, 4096, 2048, 7));

        Assert.Equal(new TorrentTransferSnapshot(1024, 512, 4096, 2048, 7), sut.Get(Hash));
    }

    [Fact]
    public void ATorrentNothingHasBeenRecordedFor_ReadsAsZeroRatherThanThrowing()
    {
        var sut = new TorrentTransferSnapshots();
        sut.Record(Hash, new TorrentTransferSnapshot(1024, 512, 4096, 2048, 7));

        // Every field zero: a torrent that has not reported yet is going nowhere, which is true.
        Assert.Equal(default, sut.Get(Other));
    }

    [Fact]
    public void ALaterRecording_ReplacesTheEarlierOne()
    {
        var sut = new TorrentTransferSnapshots();

        sut.Record(Hash, new TorrentTransferSnapshot(1, 1, 1, 1, 1));
        sut.Record(Hash, new TorrentTransferSnapshot(2, 2, 2, 2, 2));

        Assert.Equal(2, sut.Get(Hash).DownloadSpeed);
    }

    /// <summary>
    /// A magnet has no hash until its metadata arrives. Recording against that would file every such
    /// torrent under one key and have them read each other's figures.
    /// </summary>
    [Fact]
    public void ATorrentWithNoHashYet_IsNeitherStoredNorFound()
    {
        var sut = new TorrentTransferSnapshots();

        sut.Record(default, new TorrentTransferSnapshot(1024, 512, 4096, 2048, 7));

        Assert.Equal(default, sut.Get(default));
    }

    /// <summary>
    /// Alerts arrive on the engine's thread while a remote client reads on another, so the store is
    /// concurrent. This does not prove it is correct under contention - it does show that concurrent
    /// use does not throw, which a plain dictionary would.
    /// </summary>
    [Fact]
    public void RecordingAndReadingAtOnce_DoesNotThrow()
    {
        var sut = new TorrentTransferSnapshots();

        Parallel.For(0, 200, i =>
        {
            sut.Record(Hash, new TorrentTransferSnapshot(i, i, i, i, i));
            _ = sut.Get(Hash);
        });

        Assert.True(sut.Get(Hash).DownloadSpeed >= 0);
    }
}
