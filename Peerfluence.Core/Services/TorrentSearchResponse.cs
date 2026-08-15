namespace Peerfluence.Core.Services;

/// <summary>
/// The outcome of one search.
///
/// <para>
/// Carries a failure alongside results rather than instead of them. An aggregator fans a query out
/// to every index the user configured, and some of them being slow, rate-limited or broken is the
/// ordinary case, not the exceptional one - so a search that half worked returns what it got and
/// says what it did not.
/// </para>
/// </summary>
/// <param name="Results">What came back, in the order the endpoint returned it.</param>
/// <param name="Failure">
/// Why the search produced nothing at all, when that is what happened. <see cref="SearchFailure.None"/>
/// on success, including a successful search that found nothing.
/// </param>
/// <param name="FailureDetail">
/// The specific that makes the failure actionable, when there is one: the address nothing answered
/// at, or the words the indexer used to refuse. Not a sentence, and never shown on its own - the
/// interface supplies the translated sentence this fills a gap in.
/// </param>
/// <param name="IndexersQueried">How many indexes the endpoint says it asked. Zero when unreported.</param>
/// <param name="IndexersFailed">How many of those did not answer. Zero when unreported.</param>
public sealed record TorrentSearchResponse(
    IReadOnlyList<TorrentSearchResult> Results,
    SearchFailure Failure = SearchFailure.None,
    string? FailureDetail = null,
    int IndexersQueried = 0,
    int IndexersFailed = 0)
{
    public static TorrentSearchResponse Failed(SearchFailure failure, string? detail = null)
        => new([], failure, detail);

    public static TorrentSearchResponse Succeeded(
        IReadOnlyList<TorrentSearchResult> results,
        int indexersQueried = 0,
        int indexersFailed = 0)
        => new(results, SearchFailure.None, null, indexersQueried, indexersFailed);

    public bool HasFailure => Failure != SearchFailure.None;

    /// <summary>
    /// Whether going to the search settings could plausibly fix this. Everything except a transport
    /// problem that is nobody's configuration - so the interface knows when offering that route is
    /// help rather than a wild goose chase.
    /// </summary>
    public bool IsSettingsFixable => Failure
        is SearchFailure.NotConfigured
        or SearchFailure.Unreachable
        or SearchFailure.Rejected
        or SearchFailure.NotTorznab;

    /// <summary>
    /// True when the endpoint reported that some of its indexes did not answer. Worth telling the
    /// user: "nothing matched" and "half your indexes are down" call for different reactions.
    /// </summary>
    public bool IsPartial => IndexersFailed > 0 && IndexersQueried > 0;
}
