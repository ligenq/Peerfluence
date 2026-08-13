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

        Assert.Equal("Incorrect user credentials", response.FailureMessage);
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task AnUnreachableEndpoint_IsAnOrdinaryFailure()
    {
        var sut = Create(_ => throw new HttpRequestException("No connection could be made"), out _);

        var response = await sut.SearchAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.True(response.HasFailure);
        Assert.Contains("No connection", response.FailureMessage);
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

    [Fact]
    public async Task Test_AsksTheEndpointToDescribeItself()
    {
        var sut = Create(JackettFeed, out var handler);

        var failure = await sut.TestAsync(TestContext.Current.CancellationToken);

        Assert.Null(failure);
        Assert.Contains("t=caps", Assert.Single(handler.Requests).AbsoluteUri);
    }

    [Fact]
    public async Task Test_ReportsWhyItCouldNotConnect()
    {
        var sut = Create(_ => throw new HttpRequestException("Connection refused"), out _);

        var failure = await sut.TestAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Connection refused", failure);
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
