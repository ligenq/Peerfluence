using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;

namespace Peerfluence.Tests.ViewModels;

/// <summary>
/// The engine's account of itself, as the screen reads it.
/// </summary>
[Collection("Messenger")]
public sealed class StatisticsViewModelTests
{
    public StatisticsViewModelTests()
    {
        WeakReferenceMessenger.Default.Reset();
    }

    private static StatisticsViewModel Shown(ITorrentService service) => new(service) { IsOnScreen = true };

    private static ITorrentService Service(EngineStats snapshot)
    {
        var service = Substitute.For<ITorrentService>();
        service.GetStats().Returns(snapshot);
        return service;
    }

    private static EngineStats Snapshot(
        long downloaded = 0,
        long uploaded = 0,
        long downSpeed = 0,
        long upSpeed = 0,
        int torrents = 0,
        int active = 0,
        int peers = 0) =>
        new(downSpeed, upSpeed, TorrentCount: torrents, ActiveTorrents: active, TotalPeers: peers)
        {
            LifetimeDownloaded = downloaded,
            LifetimeUploaded = uploaded
        };

    [Fact]
    public void WhileHidden_ItLeavesTheEngineAlone()
    {
        // Every feature view model is built at startup, and this page is rarely open.
        var service = Service(Snapshot(downloaded: 1024));
        using var sut = new StatisticsViewModel(service);

        WeakReferenceMessenger.Default.Send(new EngineStatsReadMessage(Snapshot(downloaded: 2048)));

        service.DidNotReceive().GetStats();
        Assert.Equal(0, sut.DownloadedBytes);
    }

    [Fact]
    public void ItReadsTheEngineAsSoonAsItIsShown()
    {
        // Rather than showing what it last saw, or zeroes, until the next reading a second later.
        var service = Service(Snapshot(downloaded: 1024, uploaded: 512));
        using var sut = new StatisticsViewModel(service);

        sut.IsOnScreen = true;

        service.Received(1).GetStats();
        Assert.Equal(1024, sut.DownloadedBytes);
        Assert.Equal(512, sut.UploadedBytes);
    }

    [Fact]
    public void WhileShown_ItTakesTheReadingTheDownloadsScreenAlreadyMade()
    {
        var service = Service(Snapshot());
        using var sut = Shown(service);

        WeakReferenceMessenger.Default.Send(new EngineStatsReadMessage(Snapshot(
            downloaded: 4000, uploaded: 1000, downSpeed: 300, upSpeed: 200,
            torrents: 7, active: 3, peers: 42)));

        // The one read on being shown, and no second one for the same numbers.
        service.Received(1).GetStats();
        Assert.Equal(4000, sut.DownloadedBytes);
        Assert.Equal(1000, sut.UploadedBytes);
        Assert.Equal(300, sut.DownloadSpeedBytesPerSecond);
        Assert.Equal(200, sut.UploadSpeedBytesPerSecond);
        Assert.Equal(7, sut.Torrents);
        Assert.Equal(3, sut.ActiveTorrents);
        Assert.Equal(42, sut.ConnectedPeers);
    }

    [Fact]
    public void OnceHiddenAgain_ItStopsTakingReadings()
    {
        using var sut = Shown(Service(Snapshot(downloaded: 1000)));

        sut.IsOnScreen = false;
        WeakReferenceMessenger.Default.Send(new EngineStatsReadMessage(Snapshot(downloaded: 9000)));

        Assert.Equal(1000, sut.DownloadedBytes);
    }

    [Fact]
    public void ShownAgain_ItReadsAgain()
    {
        var service = Service(Snapshot(downloaded: 1000));
        using var sut = Shown(service);
        sut.IsOnScreen = false;

        service.GetStats().Returns(Snapshot(downloaded: 5000));
        sut.IsOnScreen = true;

        Assert.Equal(5000, sut.DownloadedBytes);
    }

    [Fact]
    public void Refresh_TakesTheNumbersItIsGiven()
    {
        var service = Service(Snapshot());
        using var sut = Shown(service);

        service.GetStats().Returns(Snapshot(
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
    public void Refresh_UsesLifetimeTotalsEvenAfterTorrentsAreRemoved()
    {
        var service = Service(new EngineStats(TotalDownloaded: 100, TotalUploaded: 50)
        {
            LifetimeDownloaded = 4000,
            LifetimeUploaded = 6000
        });
        using var sut = Shown(service);

        Assert.Equal(4000, sut.DownloadedBytes);
        Assert.Equal(6000, sut.UploadedBytes);
        Assert.Equal(1.5d, sut.Ratio);

        service.GetStats().Returns(new EngineStats());
        sut.Refresh();

        Assert.Equal(0, sut.DownloadedBytes);
        Assert.Equal(0, sut.UploadedBytes);
        Assert.Equal(0, sut.Ratio);
    }

    [Fact]
    public void Ratio_IsWhatWasGivenBackOverWhatWasTaken()
    {
        using var sut = Shown(Service(Snapshot(downloaded: 1000, uploaded: 2500)));

        Assert.Equal(2.5d, sut.Ratio);
    }

    [Fact]
    public void Ratio_IsZeroBeforeAnythingHasBeenDownloaded()
    {
        // A session that has only uploaded - reseeding what is already on disk - has no meaningful
        // ratio rather than an infinite one, and dividing by zero would say otherwise.
        using var sut = Shown(Service(Snapshot(downloaded: 0, uploaded: 9000)));

        Assert.Equal(0d, sut.Ratio);
    }

    [Fact]
    public void ItAnnouncesItselfInTheNavigation()
    {
        using var sut = new StatisticsViewModel(Service(Snapshot()));

        Assert.Equal(Peerfluence.Properties.Resources.Nav_Statistics, sut.Title);
        Assert.Equal("ChartLine", sut.IconKind);

        // Between finding torrents and the settings.
        Assert.InRange(sut.Order, 51, 99);
    }

    [Fact]
    public void Dispose_StopsItTakingReadings()
    {
        var service = Service(Snapshot(downloaded: 1000));
        var sut = Shown(service);

        sut.Dispose();
        WeakReferenceMessenger.Default.Send(new EngineStatsReadMessage(Snapshot(downloaded: 9000)));
        sut.IsOnScreen = true;

        Assert.Equal(1000, sut.DownloadedBytes);
        service.Received(1).GetStats();
    }

    [Fact]
    public void Dispose_CanBeCalledMoreThanOnce()
    {
        var sut = new StatisticsViewModel(Service(Snapshot()));

        sut.Dispose();
        var second = Record.Exception(sut.Dispose);

        Assert.Null(second);
    }
}
