using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Pins the shape of the addresses the preset buttons fill in.
///
/// <para>
/// Written after shipping a Prowlarr template that pointed at a path Prowlarr does not have. It was
/// invented by analogy with Jackett's aggregate feed, which Prowlarr has no equivalent of: its
/// routes are <c>{id}/api</c> and <c>/api/v1/indexer/{id}/newznab</c>, both of which require the id.
/// The cost of getting it wrong was a user configuring everything correctly and being told the
/// address "was not a Torznab feed", which was true and useless: the address was one this
/// application had supplied.
/// </para>
/// </summary>
public sealed class IndexerTemplateTests
{
    [Fact]
    public void TheProwlarrTemplate_NamesAnIndexer_BecauseProwlarrHasNoCombinedFeed()
    {
        var template = new Uri(SearchSettings.ProwlarrTemplate);

        // Prowlarr's own route is "{id:int}/api": a number, then api, and nothing else.
        var segments = template.AbsolutePath.Trim('/').Split('/');
        Assert.Equal(2, segments.Length);
        Assert.True(int.TryParse(segments[0], out _), $"expected an indexer id, found '{segments[0]}'");
        Assert.Equal("api", segments[1]);
    }

    /// <summary>
    /// The specific mistake that shipped. Prowlarr has no "all indexers" path, so anything claiming
    /// one is a path it will not route.
    /// </summary>
    [Fact]
    public void TheProwlarrTemplate_DoesNotPretendThereIsAnAggregateFeed()
    {
        Assert.DoesNotContain("all", SearchSettings.ProwlarrTemplate, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Jackett is the opposite case and genuinely does aggregate, which is why its template can be
    /// complete and Prowlarr's cannot.
    /// </summary>
    [Fact]
    public void TheJackettTemplate_UsesItsAggregateFeed()
    {
        Assert.Contains("/indexers/all/results/torznab", SearchSettings.JackettTemplate, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(9696, false)]
    [InlineData(9117, true)]
    public void EachTemplate_PointsAtItsOwnProductsPort(int port, bool jackett)
    {
        var template = new Uri(jackett ? SearchSettings.JackettTemplate : SearchSettings.ProwlarrTemplate);

        Assert.Equal(port, template.Port);
        // Loopback, because a preset should never reach off this machine by default.
        Assert.Equal("127.0.0.1", template.Host);
    }
}
