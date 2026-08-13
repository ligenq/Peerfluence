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
/// <param name="FailureMessage">
/// Why the search produced nothing at all, when that is what happened. Null on success, including a
/// successful search that found nothing.
/// </param>
/// <param name="IndexersQueried">How many indexes the endpoint says it asked. Zero when unreported.</param>
/// <param name="IndexersFailed">How many of those did not answer. Zero when unreported.</param>
public sealed record TorrentSearchResponse(
    IReadOnlyList<TorrentSearchResult> Results,
    string? FailureMessage = null,
    int IndexersQueried = 0,
    int IndexersFailed = 0)
{
    public static TorrentSearchResponse Failed(string message) => new([], message);

    public static TorrentSearchResponse Succeeded(
        IReadOnlyList<TorrentSearchResult> results,
        int indexersQueried = 0,
        int indexersFailed = 0)
        => new(results, null, indexersQueried, indexersFailed);

    public bool HasFailure => FailureMessage != null;

    /// <summary>
    /// True when the endpoint reported that some of its indexes did not answer. Worth telling the
    /// user: "nothing matched" and "half your indexes are down" call for different reactions.
    /// </summary>
    public bool IsPartial => IndexersFailed > 0 && IndexersQueried > 0;
}
