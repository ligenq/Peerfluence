namespace Peerfluence.Core.Services;

/// <summary>
/// One place a search can go.
///
/// <para>
/// Peerfluence has no index of its own. What it has is a set of sources: one built in, pointed at an
/// archive that publishes its own torrents through a documented API, and one the user brings by
/// running Prowlarr or Jackett. They answer the same question and their results sit in the same
/// list, so they are the same shape here.
/// </para>
/// </summary>
public interface ITorrentSearchSource
{
    /// <summary>What to show in the Indexer column for results from here.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this source is set up and switched on. A source that is off is not queried and does
    /// not count towards how many answered.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Runs a query. Never throws for a source that is down, slow or misbehaving - that is an
    /// ordinary outcome of talking to someone else's server, and it comes back as a classified
    /// failure the aggregate can report alongside whatever the other sources found.
    /// </summary>
    Task<TorrentSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// The Torznab endpoint the user configures, which unlike the built-in source has an address, a key,
/// and therefore something to get wrong and something to test.
/// </summary>
public interface ITorznabIndexer : ITorrentSearchSource
{
    /// <summary>Whether an endpoint has been configured at all.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Checks the configured endpoint answers, without running a search. Fails in exactly the ways a
    /// real search would, so what it reports is what the user would have hit anyway.
    /// </summary>
    Task<TorrentSearchResponse> TestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks for an indexer manager already running on this machine, so the common case needs no
    /// typing. Returns the endpoint URL found, or null.
    /// </summary>
    Task<string?> DetectLocalEndpointAsync(CancellationToken cancellationToken = default);
}
