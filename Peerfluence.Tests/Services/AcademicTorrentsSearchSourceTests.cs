using System.Net;
using System.Text;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class AcademicTorrentsSearchSourceTests
{
    private const string Catalogue = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
        <channel>
          <title>Academic Torrents</title>
          <item>
            <title>MNIST Database</title>
            <category>Dataset</category>
            <infohash>aaaa1111bbbb2222cccc3333dddd4444eeee5555</infohash>
            <description>Handwritten digits, the standard benchmark.</description>
            <size>11594722</size>
          </item>
          <item>
            <title>CIFAR-10</title>
            <category>Dataset</category>
            <infohash>ffff6666aaaa7777bbbb8888cccc9999dddd0000</infohash>
            <description>Small images, often compared against MNIST in papers.</description>
            <size>170498071</size>
          </item>
          <item>
            <title>Lecture notes on optimisation</title>
            <category>Course</category>
            <infohash>1234567890abcdef1234567890abcdef12345678</infohash>
            <description>Gradient descent and friends.</description>
            <size>4096</size>
          </item>
        </channel>
        </rss>
        """;

    [Fact]
    public async Task ASearch_FindsCatalogueEntriesAndPointsAtTheirTorrents()
    {
        var sut = Create(out _);

        var response = await sut.SearchAsync("mnist", TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        var first = response.Results[0];
        Assert.Equal("MNIST Database", first.Title);
        Assert.Equal(11594722, first.SizeBytes);
        Assert.Equal("Academic Torrents", first.IndexerName);
        Assert.Equal(
            "https://academictorrents.com/download/aaaa1111bbbb2222cccc3333dddd4444eeee5555",
            first.Link);
    }

    /// <summary>
    /// A title match is what the user meant; a description match is a maybe. Ordering them the other
    /// way round is how the Internet Archive ended up answering "big buck bunny" with a podcast.
    /// </summary>
    [Fact]
    public async Task TitleMatches_ComeBeforeDescriptionMatches()
    {
        var sut = Create(out _);

        var response = await sut.SearchAsync("mnist", TestContext.Current.CancellationToken);

        Assert.Equal(["MNIST Database", "CIFAR-10"], response.Results.Select(r => r.Title));
    }

    /// <summary>
    /// Every word has to appear. Matching any of them turns a specific query into a list of
    /// everything that shares one common word with it.
    /// </summary>
    [Fact]
    public async Task EveryWordInTheQuery_HasToMatch()
    {
        var sut = Create(out _);

        var response = await sut.SearchAsync("mnist database", TestContext.Current.CancellationToken);

        Assert.Equal(["MNIST Database"], response.Results.Select(r => r.Title));
    }

    /// <summary>
    /// The case that separates "all the words" from "any of them": one term matches a title
    /// outright and the other matches nothing at all. Matching any would return the entry on the
    /// strength of the word that hit, which is how a specific query turns into a shrug.
    /// </summary>
    [Fact]
    public async Task AnEntryMatchingOnlySomeOfTheWords_IsNotAMatch()
    {
        var sut = Create(out _);

        var response = await sut.SearchAsync("mnist protein", TestContext.Current.CancellationToken);

        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task AQueryMatchingNothing_IsAnEmptyResultNotAFailure()
    {
        var sut = Create(out _);

        var response = await sut.SearchAsync("protein folding", TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        Assert.Empty(response.Results);
    }

    /// <summary>
    /// The catalogue is several megabytes. Fetching it once per keystroke would be indefensible.
    /// </summary>
    [Fact]
    public async Task TheCatalogue_IsFetchedOnceAndReused()
    {
        var sut = Create(out var handler);

        await sut.SearchAsync("mnist", TestContext.Current.CancellationToken);
        await sut.SearchAsync("cifar", TestContext.Current.CancellationToken);
        await sut.SearchAsync("lecture", TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task TheCatalogue_IsFetchedAgainOnceItHasGoneStale()
    {
        var time = new AdvanceableTime(DateTimeOffset.UnixEpoch);
        var sut = Create(out var handler, timeProvider: time);

        await sut.SearchAsync("mnist", TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromHours(7));
        await sut.SearchAsync("mnist", TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests);
    }

    /// <summary>
    /// A list that worked a minute ago should not be emptied by a moment of the site being down.
    /// </summary>
    [Fact]
    public async Task AStaleCatalogue_IsUsedWhenTheSiteStopsAnswering()
    {
        var time = new AdvanceableTime(DateTimeOffset.UnixEpoch);
        var failing = false;
        var sut = Create(
            out _,
            respond: _ => failing
                ? throw new HttpRequestException("Service unavailable")
                : Ok(Catalogue),
            timeProvider: time);

        await sut.SearchAsync("mnist", TestContext.Current.CancellationToken);

        failing = true;
        time.Advance(TimeSpan.FromHours(7));
        var response = await sut.SearchAsync("mnist", TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        // The same rows the fresh catalogue returned, served from the copy that was already held.
        Assert.Equal(["MNIST Database", "CIFAR-10"], response.Results.Select(r => r.Title));
    }

    [Fact]
    public async Task TheSiteBeingUnreachableOnTheFirstSearch_IsAnOrdinaryFailure()
    {
        var sut = Create(out _, respond: _ => throw new HttpRequestException("No such host is known"));

        var response = await sut.SearchAsync("mnist", TestContext.Current.CancellationToken);

        Assert.Equal(SearchFailure.Unreachable, response.Failure);
        Assert.Equal("academictorrents.com", response.FailureDetail);
    }

    [Fact]
    public async Task AnEmptyQuery_FetchesNothingAtAll()
    {
        var sut = Create(out var handler);

        var response = await sut.SearchAsync("   ", TestContext.Current.CancellationToken);

        Assert.Empty(response.Results);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public void TheSource_IsOnByDefault_AndCanBeTurnedOff()
    {
        var settings = new AppSettings();
        var sut = Create(out _, settings: settings);

        Assert.True(sut.IsEnabled);

        settings.Search.UseAcademicTorrents = false;

        Assert.False(sut.IsEnabled);
    }

    private static HttpResponseMessage Ok(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml")
        };
    }

    private static AcademicTorrentsSearchSource Create(
        out StubHandler handler,
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null,
        AppSettings? settings = null,
        TimeProvider? timeProvider = null)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings ?? new AppSettings());

        handler = new StubHandler(respond ?? (_ => Ok(Catalogue)));
        return new AcademicTorrentsSearchSource(settingsService, new HttpClient(handler), timeProvider);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class AdvanceableTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
