namespace Peerfluence.Core.Services;

/// <summary>
/// One row of a search. What an indexer claims about a torrent, not something verified.
/// </summary>
/// <param name="Title">The name the indexer gave it.</param>
/// <param name="SizeBytes">Total size, or zero when the indexer did not say.</param>
/// <param name="Seeders">
/// The one useful quality signal a Torznab feed carries. Negative one when absent, which sorts
/// below a genuine zero: "not reported" and "nobody has it" are different, and only one of them is
/// the indexer's fault.
/// </param>
/// <param name="Peers">Leechers, on the same terms.</param>
/// <param name="IndexerName">Which index it came from, for the column of the same name.</param>
/// <param name="PublishedAt">When the indexer says it was posted, if it says.</param>
/// <param name="Link">
/// What to hand the engine: a magnet link, or an http link to a .torrent file. Torznab feeds carry
/// either, so both are accepted and told apart when adding.
/// </param>
public sealed record TorrentSearchResult(
    string Title,
    long SizeBytes,
    int Seeders,
    int Peers,
    string IndexerName,
    DateTimeOffset? PublishedAt,
    string Link)
{
    public const int Unknown = -1;

    public bool IsMagnet => Link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);
}
