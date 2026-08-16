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
    /// Prowlarr, which unlike Jackett has no aggregate feed at all: its routes are
    /// <c>{id}/api</c> and <c>/api/v1/indexer/{id}/newznab</c>, and the id is a required part of the
    /// path rather than something that can be left out or set to "all".
    ///
    /// <para>
    /// So this template is incomplete by nature, and the number in it has to be replaced with the
    /// id of the indexer being pointed at. An earlier version of this constant invented an
    /// "all indexers" path by analogy with Jackett. That path does not exist, and whatever Prowlarr
    /// answers a request for it with, it is not a feed - which is what the user saw.
    /// </para>
    /// </summary>
    public const string ProwlarrTemplate = "http://127.0.0.1:9696/1/api";

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
