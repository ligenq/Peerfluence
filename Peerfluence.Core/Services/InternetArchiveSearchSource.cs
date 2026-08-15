using System.Globalization;
using System.Net;
using System.Text.Json;
using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

/// <summary>
/// Searches the Internet Archive, which is built in and on by default.
///
/// <para>
/// This is the one source Peerfluence can offer without the user installing anything first. The
/// archive publishes a documented JSON search API and generates a torrent for every item it holds,
/// so this queries an interface meant to be queried and hands back files the archive itself
/// publishes. Nothing here is scraped, and no index is bundled.
/// </para>
///
/// <para>
/// Worth being precise about what this is not: the archive is a general-purpose host that accepts
/// uploads, so it is moderated and takedown-compliant rather than guaranteed to be public domain
/// throughout. What it gives Peerfluence is a lawful default - search that works on first run,
/// pointed somewhere reputable, instead of an empty screen telling people to go and install
/// something else.
/// </para>
/// </summary>
public sealed class InternetArchiveSearchSource : ITorrentSearchSource
{
    private const string SearchEndpoint = "https://archive.org/advancedsearch.php";

    /// <summary>
    /// Narrows the query to rows that are worth showing, asked of the server rather than filtered
    /// afterwards so that a page of fifty results is fifty usable ones.
    ///
    /// <para>
    /// Two conditions, both found by running this against the real archive. Items without a torrent
    /// cannot be downloaded here at all. Items marked access-restricted have one - the archive
    /// generates a torrent for everything - but fetching it answers 401, so offering them would be
    /// offering rows that fail when clicked.
    /// </para>
    /// </summary>
    private const string UsableItemsClause =
        " AND format:\"Archive BitTorrent\" AND NOT access-restricted-item:true";

    /// <summary>
    /// One screenful and then some. The archive holds millions of items and this is a list someone
    /// is skimming, not a dataset.
    /// </summary>
    private const int MaxResults = 50;

    private readonly IAppSettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public InternetArchiveSearchSource(IAppSettingsService settingsService, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
    }

    public string Name => "Internet Archive";

    public bool IsEnabled => _settingsService.Current.Search.UseInternetArchive;

    public async Task<TorrentSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return TorrentSearchResponse.Succeeded([]);
        }

        var uri = BuildUri(query);

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);

            // Their bot guidance asks callers to honour this rather than press on, and being told to
            // slow down is not the same as being broken.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return TorrentSearchResponse.Failed(SearchFailure.RateLimited, Name);
            }

            if (!response.IsSuccessStatusCode)
            {
                return TorrentSearchResponse.Failed(
                    SearchFailure.Other,
                    $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim());
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return TorrentSearchResponse.Failed(SearchFailure.Unreachable, "archive.org");
        }
        catch (HttpRequestException)
        {
            // No internet, DNS trouble, or the archive is down. Ordinary, and not a crash.
            return TorrentSearchResponse.Failed(SearchFailure.Unreachable, "archive.org");
        }
        catch (Exception ex)
        {
            return TorrentSearchResponse.Failed(SearchFailure.Other, ex.Message);
        }
    }

    private static Uri BuildUri(string query)
    {
        // Only the fields that end up on screen. The archive returns whatever is asked for, and a
        // result set is smaller and faster for not carrying the rest of the metadata.
        var fields = new[] { "identifier", "title", "item_size", "publicdate" };

        var parameters = new List<string>
        {
            "q=" + Uri.EscapeDataString(query.Trim() + UsableItemsClause),
            "rows=" + MaxResults.ToString(CultureInfo.InvariantCulture),
            "page=1",
            "output=json",
            // The archive matches any of the words given and does not rank the way a search engine
            // would, so "big buck bunny" put a fitness podcast first. Downloads is the closest thing
            // it offers to a popularity signal, and it is what makes the obvious answer the obvious
            // answer. It also stands in for the seeder count the archive does not report.
            "sort%5B%5D=" + Uri.EscapeDataString("downloads desc")
        };

        parameters.AddRange(fields.Select(field => "fl%5B%5D=" + Uri.EscapeDataString(field)));

        return new Uri(SearchEndpoint + "?" + string.Join("&", parameters));
    }

    private TorrentSearchResponse Parse(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // Sometimes an error page rather than JSON, which is a fault at their end, not a search
            // that found nothing.
            return TorrentSearchResponse.Failed(SearchFailure.Other, Name);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("docs", out var docs) ||
                docs.ValueKind != JsonValueKind.Array)
            {
                return TorrentSearchResponse.Failed(SearchFailure.Other, Name);
            }

            var results = new List<TorrentSearchResult>();
            foreach (var doc in docs.EnumerateArray())
            {
                if (ParseItem(doc) is { } result)
                {
                    results.Add(result);
                }
            }

            return TorrentSearchResponse.Succeeded(results);
        }
    }

    private TorrentSearchResult? ParseItem(JsonElement doc)
    {
        var identifier = ReadString(doc, "identifier");
        if (string.IsNullOrWhiteSpace(identifier))
        {
            // Without the identifier there is no torrent to point at, so there is no row to show.
            return null;
        }

        return new TorrentSearchResult(
            Title: ReadString(doc, "title") ?? identifier,
            SizeBytes: ReadLong(doc, "item_size"),
            // The archive reports neither, and a zero here would be a claim it never made.
            Seeders: TorrentSearchResult.Unknown,
            Peers: TorrentSearchResult.Unknown,
            IndexerName: Name,
            PublishedAt: ReadDate(doc, "publicdate"),
            Link: $"https://archive.org/download/{Uri.EscapeDataString(identifier)}/{Uri.EscapeDataString(identifier)}_archive.torrent");
    }

    /// <summary>
    /// Reads a field that is usually a string. Some archive fields carry an array when an item
    /// declares the same field more than once, so the first entry is taken rather than the whole
    /// thing being discarded.
    /// </summary>
    private static string? ReadString(JsonElement doc, string name)
    {
        if (!doc.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => value.EnumerateArray()
                .FirstOrDefault(item => item.ValueKind == JsonValueKind.String)
                .GetString(),
            _ => null
        };
    }

    private static long ReadLong(JsonElement doc, string name)
    {
        if (!doc.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            // item_size arrives as a string on some items.
            JsonValueKind.String when long.TryParse(
                value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement doc, string name)
    {
        var text = ReadString(doc, name);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
