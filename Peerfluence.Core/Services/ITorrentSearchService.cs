namespace Peerfluence.Core.Services;

/// <summary>
/// Searches for torrents through whatever Torznab endpoint the user has configured.
/// </summary>
public interface ITorrentSearchService
{
    /// <summary>
    /// Whether an endpoint has been configured at all. False means the screen has nothing to do.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Runs a query. Never throws for an unreachable or misbehaving endpoint - that is an ordinary
    /// outcome of talking to someone else's server, and it comes back as a failure message the
    /// screen can show.
    /// </summary>
    Task<TorrentSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the configured endpoint answers, without running a search. Fails in exactly the ways
    /// a real search would, so what it reports is what the user would have hit anyway.
    /// </summary>
    Task<TorrentSearchResponse> TestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks for an indexer manager already running on this machine, so the common case needs no
    /// typing. Returns the endpoint URL found, or null.
    /// </summary>
    Task<string?> DetectLocalEndpointAsync(CancellationToken cancellationToken = default);
}
