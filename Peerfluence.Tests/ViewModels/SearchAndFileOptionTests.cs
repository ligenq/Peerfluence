using PeerSharp.Core;
using PeerSharp.Interfaces;
using Peerfluence.Core.Services;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

/// <summary>
/// One file in the add-torrent dialog, where selecting and prioritising are two spellings of one
/// decision.
/// </summary>
/// <remarks>
/// The two properties move each other, which is the only real logic here and the sort that goes
/// wrong quietly: a file left selected at <c>DoNotDownload</c>, or deselected at <c>High</c>, is a
/// contradiction the engine has to resolve on its own.
/// </remarks>
public sealed class AddTorrentFileOptionViewModelTests
{
    private static AddTorrentFileOptionViewModel File(int index = 0) =>
        new(index, $"folder/file{index}.bin", 1024);

    [Fact]
    public void AFile_StartsSelectedAtNormalPriority()
    {
        var file = File();

        Assert.True(file.IsSelected);
        Assert.Equal(Priority.Normal, file.Priority);
    }

    [Fact]
    public void DeselectingAFile_DropsItsPriorityToDoNotDownload()
    {
        var file = File();

        file.IsSelected = false;

        Assert.Equal(Priority.DoNotDownload, file.Priority);
    }

    [Fact]
    public void ReselectingAFile_GivesItAPriorityAgain()
    {
        var file = File();
        file.IsSelected = false;

        file.IsSelected = true;

        Assert.Equal(Priority.Normal, file.Priority);
    }

    [Fact]
    public void GivingADeselectedFileAPriority_SelectsIt()
    {
        // Setting a priority on a file nobody is downloading is only meaningful as a request to
        // download it.
        var file = File();
        file.IsSelected = false;

        file.Priority = Priority.High;

        Assert.True(file.IsSelected);
        Assert.Equal(Priority.High, file.Priority);
    }

    [Fact]
    public void ReselectingAFileThatKeptItsPriority_LeavesThatPriorityAlone()
    {
        var file = File();
        file.Priority = Priority.High;

        file.IsSelected = true;

        Assert.Equal(Priority.High, file.Priority);
    }

    [Fact]
    public void AFile_KeepsWhatItWasBuiltWith()
    {
        var file = new AddTorrentFileOptionViewModel(3, "a/b.bin", 4096);

        Assert.Equal(3, file.Index);
        Assert.Equal("a/b.bin", file.Path);
        Assert.Equal(4096, file.SizeBytes);
    }

    [Fact]
    public void ThePriorityChoices_AreTheLocalizedOnes()
    {
        Assert.Equal(PriorityOptions.Localized, AddTorrentFileOptionViewModel.PriorityOptions);
    }
}

/// <summary>
/// One search result, as the grid shows it.
/// </summary>
public sealed class TorrentSearchResultViewModelTests
{
    private static TorrentSearchResultViewModel Result(int seeders, int peers) =>
        new(new TorrentSearchResult("Title", 2048, seeders, peers, "Indexer", null, "magnet:?xt=urn:btih:abc"));

    [Fact]
    public void TheCountsShown_AreTheOnesTheIndexerGave()
    {
        var result = Result(seeders: 12, peers: 34);

        Assert.Equal(12, result.Seeders);
        Assert.Equal(34, result.Peers);
    }

    [Fact]
    public void ACountTheIndexerDidNotGive_IsCarriedThroughRatherThanTurnedIntoZero()
    {
        // Unknown is negative, and it has to survive as far as the text so the grid can say so.
        // Zero would read as "nobody is seeding this", which is a different thing entirely.
        var result = Result(seeders: TorrentSearchResult.Unknown, peers: TorrentSearchResult.Unknown);

        Assert.Equal(TorrentSearchResult.Unknown, result.Seeders);
        Assert.Equal(TorrentSearchResult.Unknown, result.Peers);
        Assert.NotEqual("0", result.SeedersText);
        Assert.NotEqual("0", result.PeersText);
    }
}
