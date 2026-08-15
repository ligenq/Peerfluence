namespace Peerfluence.Core.Services;

/// <summary>
/// Asks every enabled source at once and puts the answers in one list.
///
/// <para>
/// A source being down is not a failed search. The built-in archive and a Prowlarr that is not
/// running fail independently, and the user still wants whatever did come back - so a source that
/// answers contributes its results, a source that does not contributes to the count of what did not,
/// and only a search where nothing at all answered is reported as a failure.
/// </para>
/// </summary>
public sealed class AggregateTorrentSearchService : ITorrentSearchService
{
    private readonly IReadOnlyList<ITorrentSearchSource> _sources;
    private readonly ITorznabIndexer _torznab;

    public AggregateTorrentSearchService(IEnumerable<ITorrentSearchSource> sources, ITorznabIndexer torznab)
    {
        _sources = sources.ToList();
        _torznab = torznab;
    }

    /// <summary>
    /// Whether searching can do anything at all. True whenever any source is switched on, which
    /// with the built-in archive enabled is the ordinary case - so the screen is usable on first run
    /// rather than being a notice about software the user has not installed.
    /// </summary>
    public bool IsConfigured => _sources.Any(source => source.IsEnabled);

    // Testing and detection belong to the endpoint that has an address to get wrong. The built-in
    // source has nothing to configure and so nothing to test.
    public Task<TorrentSearchResponse> TestAsync(CancellationToken cancellationToken = default)
        => _torznab.TestAsync(cancellationToken);

    public Task<string?> DetectLocalEndpointAsync(CancellationToken cancellationToken = default)
        => _torznab.DetectLocalEndpointAsync(cancellationToken);

    public async Task<TorrentSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var enabled = _sources.Where(source => source.IsEnabled).ToList();
        if (enabled.Count == 0)
        {
            return TorrentSearchResponse.Failed(SearchFailure.NotConfigured);
        }

        // In parallel: they are independent servers, and waiting for a dead one in turn would make
        // every search as slow as the slowest thing configured.
        var responses = await Task.WhenAll(
            enabled.Select(source => source.SearchAsync(query, cancellationToken))).ConfigureAwait(false);

        var results = new List<TorrentSearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queried = 0;
        var failed = 0;

        foreach (var response in responses)
        {
            // An aggregator like Jackett reports how many indexes sit behind it; a single source
            // that reports nothing counts as the one thing it is.
            queried += response.IndexersQueried > 0 ? response.IndexersQueried : 1;
            failed += response.IndexersFailed > 0
                ? response.IndexersFailed
                : response.HasFailure ? 1 : 0;

            foreach (var result in response.Results)
            {
                // Two sources can carry the same torrent. One row is enough.
                if (string.IsNullOrEmpty(result.Link) || seen.Add(result.Link))
                {
                    results.Add(result);
                }
            }
        }

        // Only when nothing answered is this a failure rather than a partial result. Reported as
        // whatever the first source said, so a lone misconfigured endpoint still explains itself.
        if (responses.All(response => response.HasFailure))
        {
            var first = responses[0];
            return TorrentSearchResponse.Failed(first.Failure, first.FailureDetail);
        }

        return TorrentSearchResponse.Succeeded(results, queried, failed);
    }
}
