using System.Globalization;
using System.Xml.Linq;

namespace Peerfluence.Core.Services;

/// <summary>
/// Searches Academic Torrents: research datasets, papers and course material, shared over BitTorrent
/// by the people who produced them.
///
/// <para>
/// Searched locally rather than remotely, because the site offers no search API. What it does offer
/// is its whole catalogue as one document - a few megabytes covering everything it holds - which it
/// publishes for exactly this purpose. Fetched once, kept for a while, and queried here.
/// </para>
///
/// <para>
/// The alternative was the site's own browse page, which sits behind a browser check. That is a
/// clear statement that automated access is not wanted there, and working around it would be both
/// rude and fragile. The published export is the door that is open.
/// </para>
/// </summary>
public sealed class AcademicTorrentsSearchSource : ITorrentSearchSource
{
    /// <summary>The whole catalogue, as the site publishes it.</summary>
    private const string CatalogueUrl = "https://academictorrents.com/database.xml";

    /// <summary>
    /// How long a fetched catalogue is reused. Research datasets are not a fast-moving feed, and
    /// re-downloading several megabytes for every keystroke would be indefensible.
    /// </summary>
    private static readonly TimeSpan CatalogueLifetime = TimeSpan.FromHours(6);

    private const int MaxResults = 50;

    private readonly IAppSettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _catalogueLock = new(1, 1);

    private IReadOnlyList<CatalogueEntry> _catalogue = [];
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public AcademicTorrentsSearchSource(
        IAppSettingsService settingsService,
        HttpClient httpClient,
        TimeProvider? timeProvider = null)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => "Academic Torrents";

    public bool IsEnabled => _settingsService.Current.Search.UseAcademicTorrents;

    public async Task<TorrentSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return TorrentSearchResponse.Succeeded([]);
        }

        IReadOnlyList<CatalogueEntry> catalogue;
        try
        {
            catalogue = await GetCatalogueAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return TorrentSearchResponse.Failed(SearchFailure.Unreachable, "academictorrents.com");
        }
        catch (HttpRequestException)
        {
            return TorrentSearchResponse.Failed(SearchFailure.Unreachable, "academictorrents.com");
        }
        catch (Exception ex)
        {
            return TorrentSearchResponse.Failed(SearchFailure.Other, ex.Message);
        }

        return TorrentSearchResponse.Succeeded(Search(catalogue, query));
    }

    /// <summary>
    /// Every word has to appear somewhere, and a title match outranks a description match.
    ///
    /// <para>
    /// Requiring all the words rather than any of them, deliberately: matching any is what made the
    /// Internet Archive return a fitness podcast for "big buck bunny", and there is no reason to
    /// repeat that here where the matching is ours to decide.
    /// </para>
    /// </summary>
    private static List<TorrentSearchResult> Search(IReadOnlyList<CatalogueEntry> catalogue, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return [];
        }

        var matches = new List<(CatalogueEntry Entry, int Rank)>();
        foreach (var entry in catalogue)
        {
            var inTitle = terms.All(term => entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
            var matchesAll = inTitle || terms.All(term =>
                entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.Description.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (matchesAll)
            {
                matches.Add((entry, inTitle ? 0 : 1));
            }
        }

        return matches
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(match => match.Entry.ToResult())
            .ToList();
    }

    private async Task<IReadOnlyList<CatalogueEntry>> GetCatalogueAsync(CancellationToken cancellationToken)
    {
        if (IsFresh())
        {
            return _catalogue;
        }

        await _catalogueLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Checked again inside the lock: several searches can arrive together, and only the
            // first of them should be the one that fetches.
            if (IsFresh())
            {
                return _catalogue;
            }

            try
            {
                var body = await _httpClient.GetStringAsync(CatalogueUrl, cancellationToken).ConfigureAwait(false);
                _catalogue = ParseCatalogue(body);
                _fetchedAt = _timeProvider.GetUtcNow();
            }
            catch when (_catalogue.Count > 0)
            {
                // A stale catalogue beats no results. The site being briefly unreachable should not
                // empty a list that was working a minute ago.
            }

            return _catalogue;
        }
        finally
        {
            _catalogueLock.Release();
        }
    }

    private bool IsFresh()
    {
        return _catalogue.Count > 0 && _timeProvider.GetUtcNow() - _fetchedAt < CatalogueLifetime;
    }

    private static List<CatalogueEntry> ParseCatalogue(string body)
    {
        var document = XDocument.Parse(body);
        var entries = new List<CatalogueEntry>();

        foreach (var item in document.Descendants("item"))
        {
            var infoHash = item.Element("infohash")?.Value?.Trim();
            var title = item.Element("title")?.Value?.Trim();

            if (string.IsNullOrEmpty(infoHash) || string.IsNullOrEmpty(title))
            {
                continue;
            }

            entries.Add(new CatalogueEntry(
                infoHash,
                title,
                item.Element("description")?.Value?.Trim() ?? string.Empty,
                long.TryParse(
                    item.Element("size")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var size) ? size : 0));
        }

        return entries;
    }

    private sealed record CatalogueEntry(string InfoHash, string Title, string Description, long SizeBytes)
    {
        public TorrentSearchResult ToResult()
        {
            return new TorrentSearchResult(
                Title,
                SizeBytes,
                // Not reported by the catalogue, and a zero would be a claim it never made.
                Seeders: TorrentSearchResult.Unknown,
                Peers: TorrentSearchResult.Unknown,
                IndexerName: "Academic Torrents",
                // The catalogue carries no date.
                PublishedAt: null,
                // The torrent itself rather than a magnet built from the hash: these are large
                // datasets with few seeders, where the trackers inside the file are what finds them.
                Link: $"https://academictorrents.com/download/{InfoHash}");
        }
    }
}
