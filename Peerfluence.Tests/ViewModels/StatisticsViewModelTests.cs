using Peerfluence.Core.Services;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

/// <summary>
/// The engine's account of itself, as the screen reads it.
/// </summary>
public sealed class StatisticsViewModelTests
{
    private static IEngineMetricsReader Reader(EngineMetricsSnapshot snapshot)
    {
        var reader = Substitute.For<IEngineMetricsReader>();
        reader.Read().Returns(snapshot);
        return reader;
    }

    private static EngineMetricsSnapshot Snapshot(
        long downloaded = 0,
        long uploaded = 0,
        long downSpeed = 0,
        long upSpeed = 0,
        long torrents = 0,
        long active = 0,
        long peers = 0) =>
        new(downloaded, uploaded, downSpeed, upSpeed, torrents, active, peers);

    [Fact]
    public void ItReadsTheMeterAsSoonAsItExists()
    {
        // Rather than showing zeroes until the first tick a second later.
        using var sut = new StatisticsViewModel(Reader(Snapshot(downloaded: 1024, uploaded: 512)));

        Assert.Equal(1024, sut.DownloadedBytes);
        Assert.Equal(512, sut.UploadedBytes);
    }

    [Fact]
    public void Refresh_TakesTheNumbersItIsGiven()
    {
        var reader = Reader(Snapshot());
        using var sut = new StatisticsViewModel(reader);

        reader.Read().Returns(Snapshot(
            downloaded: 4000, uploaded: 1000, downSpeed: 300, upSpeed: 200,
            torrents: 7, active: 3, peers: 42));
        sut.Refresh();

        Assert.Equal(4000, sut.DownloadedBytes);
        Assert.Equal(1000, sut.UploadedBytes);
        Assert.Equal(300, sut.DownloadSpeedBytesPerSecond);
        Assert.Equal(200, sut.UploadSpeedBytesPerSecond);
        Assert.Equal(7, sut.Torrents);
        Assert.Equal(3, sut.ActiveTorrents);
        Assert.Equal(42, sut.ConnectedPeers);
    }

    [Fact]
    public void Ratio_IsWhatWasGivenBackOverWhatWasTaken()
    {
        using var sut = new StatisticsViewModel(Reader(Snapshot(downloaded: 1000, uploaded: 2500)));

        Assert.Equal(2.5d, sut.Ratio);
    }

    [Fact]
    public void Ratio_IsZeroBeforeAnythingHasBeenDownloaded()
    {
        // A session that has only uploaded - reseeding what is already on disk - has no meaningful
        // ratio rather than an infinite one, and dividing by zero would say otherwise.
        using var sut = new StatisticsViewModel(Reader(Snapshot(downloaded: 0, uploaded: 9000)));

        Assert.Equal(0d, sut.Ratio);
    }

    [Fact]
    public void ItAnnouncesItselfInTheNavigation()
    {
        using var sut = new StatisticsViewModel(Reader(Snapshot()));

        Assert.Equal(Peerfluence.Properties.Resources.Nav_Statistics, sut.Title);
        Assert.Equal("ChartLine", sut.IconKind);

        // Between finding torrents and the settings.
        Assert.InRange(sut.Order, 51, 99);
    }

    [Fact]
    public void Dispose_StopsItReadingTheMeter()
    {
        var reader = Reader(Snapshot());
        var sut = new StatisticsViewModel(reader);
        var readsBefore = reader.ReceivedCalls().Count();

        sut.Dispose();
        Thread.Sleep(1300);

        Assert.Equal(readsBefore, reader.ReceivedCalls().Count());
    }

    [Fact]
    public void Dispose_CanBeCalledMoreThanOnce()
    {
        var sut = new StatisticsViewModel(Reader(Snapshot()));

        sut.Dispose();
        var second = Record.Exception(sut.Dispose);

        Assert.Null(second);
    }
}
