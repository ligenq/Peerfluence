using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Whether search has anywhere to send a query.
/// </summary>
public sealed class SearchSettingsTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("http://127.0.0.1:9696/1/api", true)]
    public void ASearchEndpoint_CountsAsConfiguredOnlyWhenThereIsAUrl(string url, bool expected)
    {
        // The key is not part of this on purpose: a Torznab endpoint may need no key, and refusing
        // to search until one was typed would lock out the people running it that way.
        var settings = new SearchSettings { TorznabUrl = url };

        Assert.Equal(expected, settings.IsConfigured);
    }
}

/// <summary>
/// A category, and whether it sends its torrents anywhere in particular.
/// </summary>
public sealed class TorrentCategoryTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(@"D:\films", true)]
    public void ACategory_HasASavePathOnlyWhenOneWasGiven(string savePath, bool expected)
    {
        Assert.Equal(expected, new TorrentCategory("Films", savePath).HasSavePath);
    }

    [Fact]
    public void ACategoryWithNoSavePath_IsTheDefault()
    {
        Assert.False(new TorrentCategory("Films").HasSavePath);
    }
}

/// <summary>
/// What a search result says about itself.
/// </summary>
public sealed class TorrentSearchResultTests
{
    [Theory]
    [InlineData("magnet:?xt=urn:btih:abc", true)]
    [InlineData("MAGNET:?xt=urn:btih:abc", true)]
    [InlineData("https://example.invalid/a.torrent", false)]
    [InlineData("", false)]
    public void AResult_KnowsWhetherItIsAMagnetOrALink(string link, bool expected)
    {
        // Which one it is decides how the result is added: a magnet goes to the engine, a link has
        // to be fetched first, because the engine's loader only reads local paths.
        var result = new TorrentSearchResult("Name", 1, 2, 3, "Indexer", null, link);

        Assert.Equal(expected, result.IsMagnet);
    }
}

/// <summary>
/// What a search answered, and what the interface should do about it.
/// </summary>
public sealed class TorrentSearchResponseTests
{
    [Fact]
    public void ASucceededSearch_ReportsNoFailure()
    {
        var response = TorrentSearchResponse.Succeeded([], indexersQueried: 3);

        Assert.False(response.HasFailure);
        Assert.False(response.IsSettingsFixable);
        Assert.Equal(SearchFailure.None, response.Failure);
    }

    [Fact]
    public void AFailedSearch_CarriesTheReasonAndTheDetail()
    {
        var response = TorrentSearchResponse.Failed(SearchFailure.Rejected, "401");

        Assert.True(response.HasFailure);
        Assert.Equal(SearchFailure.Rejected, response.Failure);
        Assert.Equal("401", response.FailureDetail);
        Assert.Empty(response.Results);
    }

    [Theory]
    [InlineData(SearchFailure.NotConfigured, true)]
    [InlineData(SearchFailure.Unreachable, true)]
    [InlineData(SearchFailure.Rejected, true)]
    [InlineData(SearchFailure.NotTorznab, true)]
    [InlineData(SearchFailure.None, false)]
    public void OnlyAFailureSomeoneCouldFix_SendsThemToTheSettings(SearchFailure failure, bool expected)
    {
        // Offering the settings for a problem the settings cannot fix is a wild goose chase, which
        // is why this is a property of the failure rather than of every failure.
        Assert.Equal(expected, TorrentSearchResponse.Failed(failure).IsSettingsFixable);
    }

    [Theory]
    [InlineData(4, 0, false)]
    [InlineData(4, 2, true)]
    [InlineData(0, 0, false)]
    public void ASearch_IsPartialOnlyWhenSomethingWasAskedAndSomethingFailed(
        int queried,
        int failed,
        bool expected)
    {
        // "Nothing matched" and "half your indexes are down" call for different reactions, and a
        // search that asked nothing is neither.
        var response = TorrentSearchResponse.Succeeded([], queried, failed);

        Assert.Equal(expected, response.IsPartial);
    }
}

/// <summary>
/// The clock startup timings are measured against.
/// </summary>
public sealed class StartupTrackerTests
{
    [Fact]
    public void TheStartupClock_RunsForwardFromWhenItWasMade()
    {
        var tracker = new StartupTracker();

        var first = tracker.ElapsedMilliseconds;
        Thread.Sleep(2);
        var second = tracker.ElapsedMilliseconds;

        Assert.True(first >= 0, "the clock started before zero");
        Assert.True(second >= first, $"the clock went backwards: {second} after {first}");
    }
}
