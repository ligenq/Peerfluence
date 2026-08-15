using System.IO.Abstractions;
using System.Net;
using System.Text;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class InternetArchiveSearchSourceTests
{
    private const string TwoResults = """
        {
          "responseHeader": { "status": 0 },
          "response": {
            "numFound": 2,
            "docs": [
              {
                "identifier": "BigBuckBunny_124",
                "title": "Big Buck Bunny",
                "item_size": 1234567,
                "publicdate": "2010-03-05T00:00:00Z"
              },
              {
                "identifier": "nasa-apollo-11",
                "title": ["Apollo 11", "Duplicate title field"],
                "item_size": "987654321"
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task ASearch_TurnsArchiveItemsIntoRowsWithATorrentToDownload()
    {
        var sut = Create(TwoResults, out _);

        var response = await sut.SearchAsync("bunny", TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        Assert.Equal(2, response.Results.Count);

        var first = response.Results[0];
        Assert.Equal("Big Buck Bunny", first.Title);
        Assert.Equal(1234567, first.SizeBytes);
        Assert.Equal("Internet Archive", first.IndexerName);

        // The archive generates this for every item it holds; it is the whole reason this source
        // can exist without an index of our own.
        Assert.Equal(
            "https://archive.org/download/BigBuckBunny_124/BigBuckBunny_124_archive.torrent",
            first.Link);
        Assert.False(first.IsMagnet);
    }

    /// <summary>
    /// The archive reports neither seeders nor leechers, and a zero would be a claim it never made.
    /// </summary>
    [Fact]
    public async Task CountsTheArchiveDoesNotReport_ComeBackAsUnknown()
    {
        var sut = Create(TwoResults, out _);

        var response = await sut.SearchAsync("bunny", TestContext.Current.CancellationToken);

        Assert.All(response.Results, result =>
        {
            Assert.Equal(TorrentSearchResult.Unknown, result.Seeders);
            Assert.Equal(TorrentSearchResult.Unknown, result.Peers);
        });
    }

    /// <summary>
    /// Some archive fields carry an array when an item declares the same field twice. Taking the
    /// first beats dropping the row.
    /// </summary>
    [Fact]
    public async Task AFieldThatArrivesAsAnArray_IsStillRead()
    {
        var sut = Create(TwoResults, out _);

        var response = await sut.SearchAsync("apollo", TestContext.Current.CancellationToken);

        var second = response.Results[1];
        Assert.Equal("Apollo 11", second.Title);
        // item_size arrives as a string on some items.
        Assert.Equal(987654321, second.SizeBytes);
    }

    /// <summary>
    /// Both clauses were put here because a live search against the archive produced rows that did
    /// not work: without the format clause there is no torrent to fetch, and an access-restricted
    /// item has one but answers 401 when it is fetched. Asked of the server rather than filtered
    /// here, so a page of results is a page of things that can actually be downloaded.
    /// </summary>
    [Fact]
    public async Task OnlyItemsThatCanActuallyBeDownloaded_AreAskedFor()
    {
        var sut = Create(TwoResults, out var handler);

        await sut.SearchAsync("bunny", TestContext.Current.CancellationToken);

        var requested = Uri.UnescapeDataString(Assert.Single(handler.Requests).AbsoluteUri);
        Assert.Contains("archive.org/advancedsearch.php", requested);
        Assert.Contains("format:\"Archive BitTorrent\"", requested);
        Assert.Contains("NOT access-restricted-item:true", requested);
        Assert.Contains("output=json", requested);
    }

    /// <summary>
    /// The archive matches any of the words given and does not rank the way a search engine would:
    /// unsorted, "big buck bunny" returned a fitness podcast first and the film not at all in the
    /// first five. Downloads is the nearest thing to a popularity signal it offers.
    /// </summary>
    [Fact]
    public async Task ResultsAreAskedForInPopularityOrder()
    {
        var sut = Create(TwoResults, out var handler);

        await sut.SearchAsync("bunny", TestContext.Current.CancellationToken);

        var requested = Uri.UnescapeDataString(Assert.Single(handler.Requests).AbsoluteUri);
        Assert.Contains("sort[]=downloads desc", requested);
    }

    /// <summary>
    /// Their guidance for automated access asks callers to honour this rather than press on, and
    /// being asked to wait is not the same as being broken.
    /// </summary>
    [Fact]
    public async Task BeingAskedToSlowDown_IsReportedAsSuchAndNotAsAFault()
    {
        var sut = Create(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests), out _);

        var response = await sut.SearchAsync("bunny", TestContext.Current.CancellationToken);

        Assert.Equal(SearchFailure.RateLimited, response.Failure);
        // Nothing in the settings would fix this, so the screen must not offer them.
        Assert.False(response.IsSettingsFixable);
    }

    [Fact]
    public async Task TheArchiveBeingUnreachable_IsAnOrdinaryFailure()
    {
        var sut = Create(_ => throw new HttpRequestException("No such host is known"), out _);

        var response = await sut.SearchAsync("bunny", TestContext.Current.CancellationToken);

        Assert.Equal(SearchFailure.Unreachable, response.Failure);
        Assert.Equal("archive.org", response.FailureDetail);
    }

    [Fact]
    public async Task AnErrorPageInsteadOfJson_IsAFailureNotAnEmptyResult()
    {
        var sut = Create("<html><body>Service unavailable</body></html>", out _);

        var response = await sut.SearchAsync("bunny", TestContext.Current.CancellationToken);

        Assert.True(response.HasFailure);
    }

    [Fact]
    public async Task AnItemWithoutAnIdentifier_IsSkippedRatherThanShownWithNowhereToGo()
    {
        var sut = Create("""{ "response": { "docs": [ { "title": "Nameless" } ] } }""", out _);

        var response = await sut.SearchAsync("bunny", TestContext.Current.CancellationToken);

        Assert.False(response.HasFailure);
        Assert.Empty(response.Results);
    }

    [Fact]
    public void TheSource_IsOnByDefault_AndCanBeTurnedOff()
    {
        var settings = new AppSettings();
        var sut = Create(TwoResults, out _, settings);

        Assert.True(sut.IsEnabled);

        settings.Search.UseInternetArchive = false;

        Assert.False(sut.IsEnabled);
    }

    private static InternetArchiveSearchSource Create(string body, out StubHandler handler, AppSettings? settings = null)
    {
        return Create(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            out handler,
            settings);
    }

    private static InternetArchiveSearchSource Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        out StubHandler handler,
        AppSettings? settings = null)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings ?? new AppSettings());

        handler = new StubHandler(respond);
        return new InternetArchiveSearchSource(settingsService, new HttpClient(handler));
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
