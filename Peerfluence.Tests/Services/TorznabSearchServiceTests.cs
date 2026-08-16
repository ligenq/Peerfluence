using System.Net;
using System.Text;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class TorznabSearchServiceTests
{
    private const string JackettFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
          <channel>
            <Response total="4" failed="1" />
            <item>
              <title>ubuntu-24.04.2-desktop-amd64.iso</title>
              <link>http://127.0.0.1:9117/dl/ubuntu/abc</link>
              <pubDate>Mon, 10 Aug 2026 09:12:00 +0000</pubDate>
              <size>6116732928</size>
              <jackettindexer id="ubuntu">Ubuntu</jackettindexer>
              <torznab:attr name="seeders" value="1204" />
              <torznab:attr name="peers" value="88" />
              <torznab:attr name="magneturl" value="magnet:?xt=urn:btih:1111111111111111111111111111111111111111" />
            </item>
            <item>
              <title>kubuntu-24.04.2-desktop-amd64.iso</title>
              <link>http://127.0.0.1:9117/dl/linuxtracker/def</link>
              <size>4938427392</size>
              <jackettindexer id="linuxtracker">Linux Tracker</jackettindexer>
              <torznab:attr name="seeders" value="311" />
              <torznab:attr name="leechers" value="12" />
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public async Task ASearch_ReadsTheFieldsTheColumnsShow()
    {
        var sut = Create(JackettFeed, out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        var first = response.Results[0];
        Assert.Equal("ubuntu-24.04.2-desktop-amd64.iso", first.Title);
        Assert.Equal(6116732928, first.SizeBytes);
        Assert.Equal(1204, first.Seeders);
        Assert.Equal(88, first.Peers);
        Assert.Equal("Ubuntu", first.IndexerName);
        Assert.Equal(2026, first.PublishedAt!.Value.Year);
    }

    [Fact]
    public async Task AMagnetInTheAttributes_IsPreferredOverTheIndexersOwnLink()
    {
        // The link element usually redirects through the indexer; the magnet can go straight to the
        // engine without a round trip or a second set of credentials.
        var sut = Create(JackettFeed, out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.StartsWith("magnet:?xt=urn:btih:1111", response.Results[0].Link);
        Assert.True(response.Results[0].IsMagnet);
    }

    [Fact]
    public async Task WithoutAMagnet_TheDownloadLinkIsUsed()
    {
        var sut = Create(JackettFeed, out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.Equal("http://127.0.0.1:9117/dl/linuxtracker/def", response.Results[1].Link);
        Assert.False(response.Results[1].IsMagnet);
    }

    [Fact]
    public async Task LeechersCountAsPeers_BecauseIndexersUseBothNames()
    {
        var sut = Create(JackettFeed, out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.Equal(12, response.Results[1].Peers);
    }

    [Fact]
    public async Task IndexersThatDidNotAnswer_AreCountedRatherThanHidden()
    {
        var sut = Create(JackettFeed, out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.True(response.IsPartial);
        Assert.Equal(4, response.IndexersQueried);
        Assert.Equal(1, response.IndexersFailed);
    }

    [Fact]
    public async Task AFeedWithoutCounts_IsNotReportedAsPartial()
    {
        // Prowlarr does not send the Response element that carries them.
        var sut = Create("""
            <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
              <channel><item><title>anything</title><link>magnet:?xt=urn:btih:2222</link></item></channel>
            </rss>
            """, out _);

        var response = await sut.SearchAsync("anything", TestContext.Current.CancellationToken);

        Assert.False(response.IsPartial);
        Assert.Single(response.Results);
    }

    [Fact]
    public async Task MissingSeederCounts_AreUnknownRatherThanZero()
    {
        // Sorting puts these below a genuine zero: "not reported" is the indexer's silence, not a
        // claim that nobody is seeding.
        var sut = Create("""
            <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
              <channel><item><title>quiet</title><link>magnet:?xt=urn:btih:3333</link></item></channel>
            </rss>
            """, out _);

        var response = await sut.SearchAsync("quiet", TestContext.Current.CancellationToken);

        Assert.Equal(TorrentSearchResult.Unknown, response.Results[0].Seeders);
    }

    [Fact]
    public async Task AnIndexerRejectingTheRequest_ComesBackAsAMessageNotAnException()
    {
        // Torznab reports its own errors inside a 200 response.
        var sut = Create("""<error code="100" description="Incorrect user credentials" />""", out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        // Torznab code 100 is a credentials problem, and the user needs to hear about the key
        // rather than about the transport.
        Assert.Equal(SearchFailure.Rejected, response.Failure);
        Assert.Equal("Incorrect user credentials", response.FailureDetail);
        Assert.Empty(response.Results);
    }

    /// <summary>
    /// The reported case: pressing a preset writes an address for software that may not be
    /// installed, and Windows answers with "the target machine actively refused it". That is a
    /// sentence about sockets, in one language, and it reaches ten-language users untranslated.
    /// What survives the classification is the address, which is the part they can act on.
    /// </summary>
    [Fact]
    public async Task AnEndpointWithNothingListening_IsReportedAsUnreachable_NotAsASocketMessage()
    {
        var sut = Create(
            _ => throw new HttpRequestException("No connection could be made because the target machine actively refused it"),
            out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.Equal(SearchFailure.Unreachable, response.Failure);
        Assert.Equal("127.0.0.1:9117", response.FailureDetail);
        Assert.True(response.IsSettingsFixable);
        Assert.DoesNotContain("socket", response.FailureDetail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEndpointThatNeverAnswers_IsUnreachableToo()
    {
        var sut = Create(_ => throw new TaskCanceledException("The request timed out"), out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.Equal(SearchFailure.Unreachable, response.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SearchFailure.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, SearchFailure.Rejected)]
    [InlineData(HttpStatusCode.NotFound, SearchFailure.NotTorznab)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SearchFailure.Unreachable)]
    [InlineData(HttpStatusCode.InternalServerError, SearchFailure.Other)]
    public async Task AStatusCode_IsTranslatedIntoSomethingTheUserCanActOn(HttpStatusCode status, SearchFailure expected)
    {
        var sut = Create(_ => new HttpResponseMessage(status), out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.Failure);
    }

    [Fact]
    public async Task GarbageInsteadOfXml_IsAnOrdinaryFailure()
    {
        var sut = Create("<html><body>404 not found</body></html>", out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.True(response.HasFailure);
    }

    [Fact]
    public async Task WithNoEndpointConfigured_NothingIsSent()
    {
        var settings = new AppSettings();
        var sut = Create(JackettFeed, out var handler, settings);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.True(response.HasFailure);
        Assert.Empty(handler.Requests);
        Assert.False(sut.IsConfigured);
    }

    [Fact]
    public async Task TheQuery_CarriesTheKeyAndTheSearchTerm()
    {
        var sut = Create(JackettFeed, out var handler);

        await sut.SearchAsync("ubuntu 24.04", TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.Requests).AbsoluteUri;
        Assert.Contains("t=search", sent);
        Assert.Contains("q=ubuntu%2024.04", sent);
        Assert.Contains("apikey=secret", sent);
    }

    [Fact]
    public async Task AnEndpointThatAlreadyCarriesAQuery_KeepsIt()
    {
        // Endpoints get pasted by hand, key and all.
        var settings = new AppSettings
        {
            Search =
            {
                TorznabUrl = "http://127.0.0.1:9117/api?apikey=pasted",
                ApiKey = string.Empty
            }
        };
        var sut = Create(JackettFeed, out var handler, settings);

        await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.Requests).AbsoluteUri;
        Assert.Contains("apikey=pasted", sent);
        Assert.Contains("t=search", sent);
    }

    /// <summary>
    /// The document a real server answers t=caps with, trimmed but otherwise as Prowlarr sent it.
    ///
    /// <para>
    /// This test used to hand the caps call a result feed, because that was the fixture already to
    /// hand. It passed, and it was worthless: no server answers caps with an rss document, and the
    /// real one - a caps root, with no channel and no items - was being classified as "not a Torznab
    /// feed". The Test button reported a healthy Prowlarr as broken, and nothing noticed until it was
    /// pointed at one.
    /// </para>
    /// </summary>
    private const string CapsDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <caps>
          <server title="Prowlarr" />
          <limits default="100" max="100" />
          <searching>
            <search available="yes" supportedParams="q" />
            <tv-search available="no" supportedParams="q" />
            <movie-search available="yes" supportedParams="q,imdbid" />
          </searching>
        </caps>
        """;


    /// <summary>
    /// A Prowlarr feed, shortened but otherwise as Prowlarr sent it. The element naming the indexer
    /// is <c>prowlarrindexer</c>; Jackett calls the same thing <c>jackettindexer</c>, and neither
    /// appears in any specification. Reading only Jackett's spelling left the Indexer column empty
    /// for every Prowlarr result, which is how it looked the first time one was pointed at.
    /// </summary>
    private const string ProwlarrFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="1.0" xmlns:atom="http://www.w3.org/2005/Atom" xmlns:torznab="http://torznab.com/schemas/2015/feed">
          <channel>
            <atom:link rel="self" type="application/rss+xml" />
            <title>Prowlarr</title>
            <item>
              <title>Example Release (2015) 720p</title>
              <guid>https://example.invalid/torrent/DC3A651718821A8A9CD00B67C00B30C654B23893</guid>
              <prowlarrindexer id="1" type="public">YTS</prowlarrindexer>
              <comments>https://example.invalid/movies/example</comments>
              <pubDate>Thu, 12 Nov 2015 18:05:58 +0100</pubDate>
              <size>728886149</size>
              <link>http://localhost:9696/1/download?apikey=redacted&amp;link=abc&amp;file=Example</link>
              <torznab:attr name="seeders" value="12" />
              <torznab:attr name="peers" value="15" />
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public async Task AProwlarrFeed_NamesTheIndexerItCameFrom()
    {
        var sut = Create(ProwlarrFeed, out _);

        var response = await sut.SearchAsync("example", TestContext.Current.CancellationToken);

        var result = Assert.Single(response.Results);
        Assert.Equal("YTS", result.IndexerName);
    }

    [Fact]
    public async Task AProwlarrFeed_ParsesTheRestOfTheRowToo()
    {
        var sut = Create(ProwlarrFeed, out _);

        var response = await sut.SearchAsync("example", TestContext.Current.CancellationToken);

        var result = Assert.Single(response.Results);
        Assert.Equal("Example Release (2015) 720p", result.Title);
        Assert.Equal(728886149, result.SizeBytes);
        Assert.Equal(12, result.Seeders);
        Assert.Equal(15, result.Peers);
        // Prowlarr proxies the download through itself, so this is an http link rather than a magnet.
        Assert.False(result.IsMagnet);
        Assert.StartsWith("http://localhost:9696/1/download", result.Link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_AsksTheEndpointToDescribeItself()
    {
        var sut = Create(CapsDocument, out var handler);

        var response = await sut.TestAsync(TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        Assert.Contains("t=caps", Assert.Single(handler.Requests).AbsoluteUri);
    }

    /// <summary>
    /// A caps document is a Torznab answer, not a malformed feed. Saying otherwise is what made a
    /// working endpoint look broken.
    /// </summary>
    [Fact]
    public async Task ACapsDocument_IsNotMistakenForSomethingThatIsNotTorznab()
    {
        var sut = Create(CapsDocument, out _);

        var response = await sut.TestAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(SearchFailure.NotTorznab, response.Failure);
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task Test_ReportsWhyItCouldNotConnect()
    {
        var sut = Create(_ => throw new HttpRequestException("Connection refused"), out _);

        var response = await sut.TestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SearchFailure.Unreachable, response.Failure);
    }

    [Fact]
    public async Task Detect_FindsAnIndexerManagerAlreadyRunning()
    {
        var sut = Create(request => request.RequestUri!.Port == 9117
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : throw new HttpRequestException("nothing here"), out _);

        var found = await sut.DetectLocalEndpointAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SearchSettings.JackettTemplate, found);
    }

    [Fact]
    public async Task Detect_FallsThroughToTheNextPortWhenTheFirstIsSilent()
    {
        var sut = Create(request => request.RequestUri!.Port == 9696
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : throw new HttpRequestException("nothing here"), out _);

        // Unauthorized still means something answered; whether the key is right is Test's job.
        var found = await sut.DetectLocalEndpointAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SearchSettings.ProwlarrTemplate, found);
    }

    [Fact]
    public async Task Detect_ReturnsNothingWhenNothingIsListening()
    {
        var sut = Create(_ => throw new HttpRequestException("nothing here"), out _);

        Assert.Null(await sut.DetectLocalEndpointAsync(TestContext.Current.CancellationToken));
    }

    private static TorznabSearchService Create(string body, out StubHandler handler, AppSettings? settings = null)
    {
        return Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml")
        }, out handler, settings);
    }

    private static TorznabSearchService Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        out StubHandler handler,
        AppSettings? settings = null)
    {
        settings ??= new AppSettings
        {
            Search =
            {
                TorznabUrl = SearchSettings.JackettTemplate,
                ApiKey = "secret"
            }
        };

        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);

        handler = new StubHandler(respond);
        return new TorznabSearchService(settingsService, new HttpClient(handler));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
