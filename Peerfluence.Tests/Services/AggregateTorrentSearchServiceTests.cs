using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class AggregateTorrentSearchServiceTests
{
    private readonly ITorznabIndexer _torznab = Substitute.For<ITorznabIndexer>();

    [Fact]
    public async Task Results_FromEverySourceEndUpInOneList()
    {
        var archive = Source("Internet Archive", TorrentSearchResponse.Succeeded([Result("bunny", "a")]));
        var indexer = Source("Torznab", TorrentSearchResponse.Succeeded([Result("ubuntu", "b")]));
        var sut = Create(archive, indexer);

        var response = await sut.SearchAsync("x", TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        Assert.Equal(["bunny", "ubuntu"], response.Results.Select(r => r.Title));
    }

    /// <summary>
    /// The whole point of aggregating: one source being down must not throw away what the others
    /// found. It becomes a partial result, which the screen already knows how to describe.
    /// </summary>
    [Fact]
    public async Task OneSourceBeingDown_DoesNotDiscardWhatTheOthersFound()
    {
        var archive = Source("Internet Archive", TorrentSearchResponse.Succeeded([Result("bunny", "a")]));
        var indexer = Source("Torznab", TorrentSearchResponse.Failed(SearchFailure.Unreachable, "127.0.0.1:9117"));
        var sut = Create(archive, indexer);

        var response = await sut.SearchAsync("x", TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        Assert.Single(response.Results);
        Assert.True(response.IsPartial);
        Assert.Equal(2, response.IndexersQueried);
        Assert.Equal(1, response.IndexersFailed);
    }

    [Fact]
    public async Task OnlyWhenNothingAnswered_IsTheWholeSearchAFailure()
    {
        var archive = Source("Internet Archive", TorrentSearchResponse.Failed(SearchFailure.Unreachable, "archive.org"));
        var indexer = Source("Torznab", TorrentSearchResponse.Failed(SearchFailure.Rejected, "401"));
        var sut = Create(archive, indexer);

        var response = await sut.SearchAsync("x", TestContext.Current.CancellationToken);

        Assert.True(response.HasFailure);
        Assert.Equal(SearchFailure.Unreachable, response.Failure);
        Assert.Empty(response.Results);
    }

    /// <summary>
    /// An aggregator like Jackett already counts the indexes behind it; a plain source counts as the
    /// one thing it is. Otherwise "1 of 2 answered" would be nonsense next to a Jackett with twenty.
    /// </summary>
    [Fact]
    public async Task ASourceThatCountsItsOwnIndexes_HasThoseCountsCarriedThrough()
    {
        var archive = Source("Internet Archive", TorrentSearchResponse.Succeeded([Result("bunny", "a")]));
        var indexer = Source("Torznab", TorrentSearchResponse.Succeeded([Result("ubuntu", "b")], 20, 3));
        var sut = Create(archive, indexer);

        var response = await sut.SearchAsync("x", TestContext.Current.CancellationToken);

        Assert.Equal(21, response.IndexersQueried);
        Assert.Equal(3, response.IndexersFailed);
    }

    [Fact]
    public async Task TheSameTorrentFromTwoSources_IsOneRow()
    {
        var archive = Source("Internet Archive", TorrentSearchResponse.Succeeded([Result("bunny", "same")]));
        var indexer = Source("Torznab", TorrentSearchResponse.Succeeded([Result("bunny copy", "same")]));
        var sut = Create(archive, indexer);

        var response = await sut.SearchAsync("x", TestContext.Current.CancellationToken);

        Assert.Single(response.Results);
    }

    [Fact]
    public async Task ASourceThatIsSwitchedOff_IsNotAskedAndDoesNotCount()
    {
        var archive = Source("Internet Archive", TorrentSearchResponse.Succeeded([Result("bunny", "a")]));
        var indexer = Source("Torznab", TorrentSearchResponse.Succeeded([Result("ubuntu", "b")]), enabled: false);
        var sut = Create(archive, indexer);

        var response = await sut.SearchAsync("x", TestContext.Current.CancellationToken);

        Assert.Single(response.Results);
        Assert.Equal(1, response.IndexersQueried);
        await indexer.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// With the built-in archive on, this is true before anything has been installed - which is what
    /// makes the search screen usable on first run.
    /// </summary>
    [Fact]
    public void SearchIsAvailable_WheneverAnySourceIsOn()
    {
        var archive = Source("Internet Archive", TorrentSearchResponse.Succeeded([]));
        var indexer = Source("Torznab", TorrentSearchResponse.Succeeded([]), enabled: false);

        Assert.True(Create(archive, indexer).IsConfigured);
    }

    [Fact]
    public async Task EverySourceOff_IsReportedAsNothingBeingConfigured()
    {
        var archive = Source("Internet Archive", TorrentSearchResponse.Succeeded([]), enabled: false);
        var sut = Create(archive);

        Assert.False(sut.IsConfigured);

        var response = await sut.SearchAsync("x", TestContext.Current.CancellationToken);

        Assert.Equal(SearchFailure.NotConfigured, response.Failure);
    }

    /// <summary>
    /// Testing and detection belong to the endpoint that has an address to get wrong. The built-in
    /// source has nothing to configure and so nothing to test.
    /// </summary>
    [Fact]
    public async Task TestingAndDetection_GoToTheConfiguredEndpoint()
    {
        var sut = Create(Source("Internet Archive", TorrentSearchResponse.Succeeded([])));
        _torznab.TestAsync(Arg.Any<CancellationToken>()).Returns(TorrentSearchResponse.Succeeded([]));

        await sut.TestAsync(TestContext.Current.CancellationToken);
        await sut.DetectLocalEndpointAsync(TestContext.Current.CancellationToken);

        await _torznab.Received(1).TestAsync(Arg.Any<CancellationToken>());
        await _torznab.Received(1).DetectLocalEndpointAsync(Arg.Any<CancellationToken>());
    }

    private AggregateTorrentSearchService Create(params ITorrentSearchSource[] sources)
    {
        return new AggregateTorrentSearchService(sources, _torznab);
    }

    private static ITorrentSearchSource Source(string name, TorrentSearchResponse response, bool enabled = true)
    {
        var source = Substitute.For<ITorrentSearchSource>();
        source.Name.Returns(name);
        source.IsEnabled.Returns(enabled);
        source.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(response);
        return source;
    }

    private static TorrentSearchResult Result(string title, string link)
    {
        return new TorrentSearchResult(
            title,
            SizeBytes: 1,
            Seeders: 1,
            Peers: 1,
            IndexerName: "test",
            PublishedAt: null,
            Link: "magnet:?xt=urn:btih:" + link);
    }
}
