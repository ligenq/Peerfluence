using System.Text.Json.Serialization;

namespace Peerfluence.Core.Config;

/// <summary>
/// Where torrent search sends its queries.
///
/// <para>
/// Empty by default and nothing is shipped in it. Peerfluence searches through a Torznab endpoint
/// the user runs themselves - Prowlarr or Jackett - so which indexes exist is their decision and
/// their configuration, not a list carried around inside this application.
/// </para>
/// </summary>
public sealed class SearchSettings
{
    /// <summary>
    /// Jackett aggregates every configured indexer behind one well-known path, so a URL can be
    /// offered complete and the user only has to supply the key.
    /// </summary>
    public const string JackettTemplate = "http://127.0.0.1:9117/api/v2.0/indexers/all/results/torznab/api";

    /// <summary>
    /// Prowlarr's equivalent. Its per-indexer feeds live at <c>/{id}/api</c> instead, so anyone
    /// pointing at a single indexer will need to edit this - which is what Test is for.
    /// </summary>
    public const string ProwlarrTemplate = "http://127.0.0.1:9696/api/v1/indexers/all/results/torznab";

    /// <summary>
    /// Whether to search the Internet Archive, which is built in and needs nothing installed. On by
    /// default: it is what makes the search screen work the first time it is opened.
    /// </summary>
    public bool UseInternetArchive { get; set; } = true;

    /// <summary>
    /// Whether to search Academic Torrents: research datasets, papers and courses. On by default and
    /// needs nothing installed, like the archive above it.
    /// </summary>
    public bool UseAcademicTorrents { get; set; } = true;

    public string TorznabUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether search has somewhere to go. The screen is inert until it does, and says so.
    ///
    /// <para>
    /// Not written to the settings file: it is derived from the address, and a stored copy of it
    /// would be a second answer to a question that already has one - read back into nothing,
    /// because there is no setter for it to land in.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TorznabUrl);
}
