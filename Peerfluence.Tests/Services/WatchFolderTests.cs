using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class WatchFolderTests
{
    [Theory]
    [InlineData("a.torrent", true)]
    [InlineData("a.TORRENT", true)]
    [InlineData("a.txt", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void ShouldAdd_TakesTorrentFilesAndNothingElse(string name, bool expected)
    {
        Assert.Equal(expected, WatchFolder.ShouldAdd(name));
    }

    [Fact]
    public void ShouldAdd_LeavesAFileItHasAlreadyTaken()
    {
        // The suffix is what stops the next sweep adding the same torrent again.
        Assert.False(WatchFolder.ShouldAdd("a.torrent" + WatchFolder.AddedSuffix));
    }

    [Fact]
    public void MarkedPath_KeepsTheOriginalName()
    {
        // Renamed rather than deleted: the directory is somewhere a person drops things.
        Assert.Equal(@"C:\drop\film.torrent.added", WatchFolder.MarkedPath(@"C:\drop\film.torrent"));
    }
}
