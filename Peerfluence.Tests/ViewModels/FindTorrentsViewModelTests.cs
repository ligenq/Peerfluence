using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using PeerSharp.Config;

namespace Peerfluence.Tests.ViewModels;

/// <summary>
/// In the messenger collection: this class both sends on the shared default messenger and registers
/// on it, and a class doing that in parallel with another sees the other's traffic.
/// </summary>
[Collection("Messenger")]
public class FindTorrentsViewModelTests
{
    private readonly ITorrentSearchService _searchService = Substitute.For<ITorrentSearchService>();
    private readonly ITorrentService _torrentService = Substitute.For<ITorrentService>();
    private readonly IAddTorrentDialogService _addTorrentDialogService = Substitute.For<IAddTorrentDialogService>();

    private FindTorrentsViewModel Create()
    {
        return new FindTorrentsViewModel(_searchService, _torrentService, _addTorrentDialogService);
    }

    private static TorrentSearchResult Result(string title, int seeders = 0, bool magnet = true)
    {
        return new TorrentSearchResult(
            title,
            SizeBytes: 1024,
            Seeders: seeders,
            Peers: 0,
            IndexerName: "Example",
            PublishedAt: null,
            Link: magnet
                ? "magnet:?xt=urn:btih:" + title
                : "http://example.invalid/" + title + ".torrent");
    }

    [Fact]
    public void SearchCommand_IsUnavailable_UntilSomethingIsTyped()
    {
        var sut = Create();

        Assert.False(sut.SearchCommand.CanExecute(null));

        sut.Query = "ubuntu";

        Assert.True(sut.SearchCommand.CanExecute(null));
    }

    [Fact]
    public void Refresh_ReadsWhetherAnEndpointIsConfigured()
    {
        _searchService.IsConfigured.Returns(false);
        var sut = Create();
        sut.Refresh();
        Assert.True(sut.IsNotConfigured);

        _searchService.IsConfigured.Returns(true);
        sut.Refresh();

        Assert.True(sut.IsConfigured);
        Assert.False(sut.IsNotConfigured);
    }

    [Fact]
    public async Task Search_DoesNothing_WhenNoEndpointIsConfigured()
    {
        _searchService.IsConfigured.Returns(false);
        var sut = Create();
        sut.Query = "ubuntu";

        await sut.SearchCommand.ExecuteAsync(null);

        await _searchService.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Seeds descending: on an aggregated search it is the only quality signal the feed carries.
    /// </summary>
    [Fact]
    public async Task Search_PutsTheBestSeededResultsFirst()
    {
        _searchService.IsConfigured.Returns(true);
        _searchService.SearchAsync("ubuntu", Arg.Any<CancellationToken>()).Returns(
            TorrentSearchResponse.Succeeded([Result("few", 3), Result("many", 90), Result("some", 20)], 1, 0));
        var sut = Create();
        sut.Query = "ubuntu";

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["many", "some", "few"], sut.Results.Select(r => r.Title));
        Assert.False(sut.HasStatusMessage);
    }

    [Fact]
    public async Task Search_SaysSoWhenNothingMatched()
    {
        _searchService.IsConfigured.Returns(true);
        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Succeeded([], 1, 0));
        var sut = Create();
        sut.Query = "nothing at all";

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Equal(Peerfluence.Properties.Resources.Find_NoResults, sut.StatusMessage);
    }

    /// <summary>
    /// An empty list because two indexers timed out is a different problem from an empty list
    /// because nothing matched, and only the user can tell which they care about.
    /// </summary>
    [Fact]
    public async Task Search_SaysHowManyIndexersAnswered_WhenSomeDidNot()
    {
        _searchService.IsConfigured.Returns(true);
        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Succeeded([Result("one", 5)], 5, 2));
        var sut = Create();
        sut.Query = "ubuntu";

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Contains("3", sut.StatusMessage);
        Assert.Contains("5", sut.StatusMessage);
        Assert.Single(sut.Results);
    }

    /// <summary>
    /// The reported case. What the user must not see is the socket message; what they must see is
    /// the address, and a way to get to the settings.
    /// </summary>
    [Fact]
    public async Task Search_ExplainsAnUnreachableIndexer_AndOffersTheSettings()
    {
        _searchService.IsConfigured.Returns(true);
        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Failed(SearchFailure.Unreachable, "127.0.0.1:9117"));
        var sut = Create();
        sut.Query = "ubuntu";

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Contains("127.0.0.1:9117", sut.StatusMessage);
        Assert.Contains("Jackett", sut.StatusMessage);
        Assert.True(sut.CanFixInSettings);
        Assert.Empty(sut.Results);
    }

    [Fact]
    public async Task Search_SaysToCheckTheKey_WhenTheIndexerRefuses()
    {
        _searchService.IsConfigured.Returns(true);
        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Failed(SearchFailure.Rejected, "401 Unauthorized"));
        var sut = Create();
        sut.Query = "ubuntu";

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Equal(Peerfluence.Properties.Resources.Find_Failure_Rejected, sut.StatusMessage);
        Assert.True(sut.CanFixInSettings);
    }

    /// <summary>
    /// Offering the settings for something the settings cannot fix sends the user on a wasted trip.
    /// </summary>
    [Fact]
    public async Task Search_DoesNotOfferTheSettings_ForAProblemTheyCannotFix()
    {
        _searchService.IsConfigured.Returns(true);
        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Failed(SearchFailure.Other, "The feed was truncated"));
        var sut = Create();
        sut.Query = "ubuntu";

        await sut.SearchCommand.ExecuteAsync(null);

        Assert.False(sut.CanFixInSettings);
    }

    [Fact]
    public async Task TheOfferOfSettings_IsWithdrawn_OnceASearchWorks()
    {
        _searchService.IsConfigured.Returns(true);
        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Failed(SearchFailure.Unreachable, "127.0.0.1:9117"));
        var sut = Create();
        sut.Query = "ubuntu";
        await sut.SearchCommand.ExecuteAsync(null);

        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Succeeded([Result("ubuntu", 5)], 1, 0));
        await sut.SearchCommand.ExecuteAsync(null);

        Assert.False(sut.CanFixInSettings);
        Assert.False(sut.HasStatusMessage);
    }

    [Fact]
    public void OpenSearchSettings_AsksTheShellToGoThere()
    {
        var sut = Create();
        var received = 0;
        var recipient = new object();
        WeakReferenceMessenger.Default.Register<ShowSearchSettingsMessage>(recipient, (_, _) => received++);

        try
        {
            sut.OpenSearchSettingsCommand.Execute(null);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task Search_ClearsTheResultsOfThePreviousSearch()
    {
        _searchService.IsConfigured.Returns(true);
        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Succeeded([Result("first", 1)], 1, 0));
        var sut = Create();
        sut.Query = "first";
        await sut.SearchCommand.ExecuteAsync(null);

        _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentSearchResponse.Succeeded([Result("second", 1)], 1, 0));
        sut.Query = "second";
        await sut.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["second"], sut.Results.Select(r => r.Title));
    }

    /// <summary>
    /// Through the same dialog as every other add, so the download path and file selection are
    /// asked for in the one place that asks for them.
    /// </summary>
    [Fact]
    public async Task Add_SendsAMagnetThroughTheAddDialog()
    {
        var sut = Create();
        var result = new TorrentSearchResultViewModel(Result("ubuntu", magnet: true));

        await sut.AddCommand.ExecuteAsync(result);

        await _addTorrentDialogService.Received(1).ShowMagnetAsync(result.Link);
        await _torrentService.DidNotReceive().AddTorrentFileAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions>());
    }

    [Fact]
    public async Task Add_SendsATorrentFileLinkToTheEngine()
    {
        var sut = Create();
        var result = new TorrentSearchResultViewModel(Result("ubuntu", magnet: false));

        await sut.AddCommand.ExecuteAsync(result);

        await _torrentService.Received(1).AddTorrentFileAsync(result.Link, Arg.Any<AddTorrentOptions>());
        await _addTorrentDialogService.DidNotReceive().ShowMagnetAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Add_SaysSoWhenTheAddFails()
    {
        _addTorrentDialogService.ShowMagnetAsync(Arg.Any<string>())
            .Returns<Task>(_ => throw new InvalidOperationException("Engine is not running"));
        var sut = Create();

        await sut.AddCommand.ExecuteAsync(new TorrentSearchResultViewModel(Result("ubuntu")));

        Assert.Contains("Engine is not running", sut.StatusMessage);
    }

    [Fact]
    public async Task Add_IgnoresNothingBeingSelected()
    {
        var sut = Create();

        await sut.AddCommand.ExecuteAsync(null);

        await _addTorrentDialogService.DidNotReceive().ShowMagnetAsync(Arg.Any<string>());
    }

    [Fact]
    public void AMissingCount_ShowsADash_RatherThanAZeroTheIndexerNeverSaid()
    {
        var absent = new TorrentSearchResultViewModel(new TorrentSearchResult(
            "ubuntu",
            SizeBytes: 0,
            Seeders: TorrentSearchResult.Unknown,
            Peers: TorrentSearchResult.Unknown,
            IndexerName: "Example",
            PublishedAt: null,
            Link: "magnet:?xt=urn:btih:ubuntu"));

        Assert.Equal("—", absent.SeedersText);
        Assert.Equal("—", absent.PeersText);
        Assert.Equal("0", new TorrentSearchResultViewModel(Result("ubuntu")).SeedersText);
    }
}
