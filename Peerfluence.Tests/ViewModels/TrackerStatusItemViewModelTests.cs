using Peerfluence.ViewModels;
using PeerSharp.Core;

namespace Peerfluence.Tests.ViewModels;

public class TrackerStatusItemViewModelTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var status = new TrackerStatus(
            Url: "http://tracker.test/announce",
            Status: TrackerStatusType.Working,
            LastAnnounce: now.AddMinutes(-5),
            NextAnnounce: now.AddMinutes(25),
            SeedCount: 42,
            LeechCount: 7,
            LastError: null);

        var sut = new TrackerStatusItemViewModel(status);

        Assert.Equal("http://tracker.test/announce", sut.Url);
        Assert.Equal("Working", sut.State);
        Assert.Equal(now.AddMinutes(-5), sut.LastAnnounce);
        Assert.Equal(now.AddMinutes(25), sut.NextAnnounce);
        Assert.Equal(42u, sut.SeedCount);
        Assert.Equal(7u, sut.LeechCount);
        Assert.Equal(string.Empty, sut.LastError);
    }

    [Fact]
    public void Constructor_SetsLastError_WhenPresent()
    {
        var status = new TrackerStatus(
            Url: "http://tracker.test/announce",
            Status: TrackerStatusType.NotWorking,
            LastError: "Connection timed out");

        var sut = new TrackerStatusItemViewModel(status);

        Assert.Equal("Connection timed out", sut.LastError);
        Assert.Equal("Not working", sut.State);
    }
}
