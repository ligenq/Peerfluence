using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

/// <summary>
/// Talks Torznab: a query string in, an RSS document out, with the numbers that matter carried in
/// <c>torznab:attr</c> elements alongside each item.
/// </summary>
public sealed class TorznabSearchService : ITorrentSearchService
{
    private static readonly XNamespace Torznab = "http://torznab.com/schemas/2015/feed";

    /// <summary>
    /// Where an indexer manager usually is when it is on the same machine. Jackett first because
    /// its aggregate feed is a documented path; Prowlarr's varies more by version.
    /// </summary>
    private static readonly (string Url, string Probe)[] LocalCandidates =
    [
        (SearchSettings.JackettTemplate, "http://127.0.0.1:9117/"),
        (SearchSettings.ProwlarrTemplate, "http://127.0.0.1:9696/")
    ];

    private readonly IAppSettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public TorznabSearchService(IAppSettingsService settingsService, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
    }

    public bool IsConfigured => _settingsService.Current.Search.IsConfigured;

    public Task<TorrentSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        return QueryAsync("search", query, cancellationToken);
    }

    public Task<TorrentSearchResponse> TestAsync(CancellationToken cancellationToken = default)
    {
        // "caps" is Torznab's describe-yourself call: cheap, and it fails the same way a real
        // search would if the URL or the key is wrong.
        return QueryAsync("caps", query: null, cancellationToken);
    }

    public async Task<string?> DetectLocalEndpointAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (url, probe) in LocalCandidates)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, probe);
                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                // Any answer at all means something is listening. Whether it is the right thing,
                // and whether the key is right, is what Test is for.
                return url;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Nothing there; try the next.
            }
        }

        return null;
    }

    private async Task<TorrentSearchResponse> QueryAsync(string type, string? query, CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current.Search;
        if (!settings.IsConfigured)
        {
            return TorrentSearchResponse.Failed(SearchFailure.NotConfigured);
        }

        Uri uri;
        try
        {
            uri = BuildUri(settings, type, query);
        }
        catch (UriFormatException ex)
        {
            return TorrentSearchResponse.Failed(SearchFailure.NotTorznab, ex.Message);
        }

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return FromStatusCode(response.StatusCode, response.ReasonPhrase, uri);
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
            // Not our cancellation, so it is the client's timeout: something is at that address but
            // it never finished answering. Unreachable from where the user is standing.
            return TorrentSearchResponse.Failed(SearchFailure.Unreachable, Authority(uri));
        }
        catch (HttpRequestException)
        {
            // Someone else's server on the other end of this: not running, wrong port, no route, bad
            // TLS. All ordinary, none of them a crash, and all of them the same thing to the user -
            // nothing is answering where they pointed this.
            return TorrentSearchResponse.Failed(SearchFailure.Unreachable, Authority(uri));
        }
        catch (Exception ex)
        {
            return TorrentSearchResponse.Failed(SearchFailure.Other, ex.Message);
        }
    }

    /// <summary>
    /// The part of the address worth repeating back: "127.0.0.1:9117". The full endpoint is a long
    /// path nobody needs to read to understand that nothing is listening on that port.
    /// </summary>
    private static string Authority(Uri uri)
    {
        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    private static TorrentSearchResponse FromStatusCode(HttpStatusCode statusCode, string? reasonPhrase, Uri uri)
    {
        var detail = $"{(int)statusCode} {reasonPhrase}".Trim();

        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => TorrentSearchResponse.Failed(SearchFailure.Rejected, detail),
            HttpStatusCode.NotFound
                => TorrentSearchResponse.Failed(SearchFailure.NotTorznab, detail),
            HttpStatusCode.TooManyRequests
                => TorrentSearchResponse.Failed(SearchFailure.RateLimited, Authority(uri)),
            // A gateway that cannot reach what it fronts is the same problem one hop further out.
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                => TorrentSearchResponse.Failed(SearchFailure.Unreachable, Authority(uri)),
            _ => TorrentSearchResponse.Failed(SearchFailure.Other, detail)
        };
    }

    private static Uri BuildUri(SearchSettings settings, string type, string? query)
    {
        var builder = new UriBuilder(settings.TorznabUrl.Trim());
        var parameters = new List<string> { "t=" + Uri.EscapeDataString(type) };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            parameters.Add("apikey=" + Uri.EscapeDataString(settings.ApiKey.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters.Add("q=" + Uri.EscapeDataString(query.Trim()));
        }

        // Endpoints are pasted by hand and often already carry a key or a path query, so add to
        // what is there rather than replacing it.
        var existing = builder.Query.TrimStart('?');
        builder.Query = existing.Length > 0
            ? existing + "&" + string.Join("&", parameters)
            : string.Join("&", parameters);

        return builder.Uri;
    }

    private static TorrentSearchResponse Parse(string body)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(body);
        }
        catch (System.Xml.XmlException)
        {
            // Answered with something that is not XML at all. Whatever is on that port, it is not
            // the Torznab endpoint the address claims.
            return TorrentSearchResponse.Failed(SearchFailure.NotTorznab);
        }

        // Torznab reports its own failures inside a 200 response, which is why this is checked
        // before the status code is trusted to mean anything.
        var error = document.Root?.Name.LocalName == "error" ? document.Root : null;
        if (error != null)
        {
            // Torznab's own codes: 100-103 are all "your key is missing, wrong, or not allowed to
            // do that". Anything else is the indexer objecting to the query itself.
            var description = error.Attribute("description")?.Value;
            var code = error.Attribute("code")?.Value;
            var rejected = code is "100" or "101" or "102" or "103";

            return TorrentSearchResponse.Failed(
                rejected ? SearchFailure.Rejected : SearchFailure.Other,
                description);
        }

        // "caps" is a Torznab server describing itself, and it is what Test asks for. It is a
        // different document from a result feed - no channel, no items - so it lands here and has to
        // be recognised, or the one call whose job is to say "this endpoint is fine" reports that a
        // working Prowlarr is not a Torznab endpoint. Which is what it did.
        if (document.Root?.Name.LocalName == "caps")
        {
            return TorrentSearchResponse.Succeeded([]);
        }

        // A wrong URL usually answers with an HTML error page, and HTML parses as XML perfectly
        // well - so without this check a mistyped endpoint reads as "no results" rather than as the
        // mistake it is.
        if (document.Root?.Name.LocalName is not "rss" and not "feed")
        {
            return TorrentSearchResponse.Failed(SearchFailure.NotTorznab);
        }

        var channel = document.Root.Element("channel");
        if (channel == null)
        {
            return TorrentSearchResponse.Succeeded([]);
        }

        var results = channel.Elements("item").Select(ParseItem).ToList();

        return TorrentSearchResponse.Succeeded(
            results,
            indexersQueried: ReadResponseCount(channel, "total"),
            indexersFailed: ReadResponseCount(channel, "failed"));
    }

    /// <summary>
    /// Jackett reports how many of its indexers answered in a <c>Response</c> element. Prowlarr
    /// does not, so both counts are optional and the screen only mentions them when they are there.
    /// </summary>
    private static int ReadResponseCount(XElement channel, string attribute)
    {
        var element = channel.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("Response", StringComparison.OrdinalIgnoreCase));

        var value = element?.Attributes()
            .FirstOrDefault(a => a.Name.LocalName.Equals(attribute, StringComparison.OrdinalIgnoreCase))?.Value;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static TorrentSearchResult ParseItem(XElement item)
    {
        var title = item.Element("title")?.Value?.Trim() ?? string.Empty;

        // A magnet in the torznab attributes beats the link element: the link is often a redirect
        // through the indexer, and the magnet can be handed straight to the engine.
        var link = Attribute(item, "magneturl")
            ?? item.Element("link")?.Value
            ?? item.Element("enclosure")?.Attribute("url")?.Value
            ?? string.Empty;

        return new TorrentSearchResult(
            title,
            SizeBytes: ParseLong(item.Element("size")?.Value ?? Attribute(item, "size")),
            Seeders: ParseInt(Attribute(item, "seeders")),
            Peers: ParseInt(Attribute(item, "peers") ?? Attribute(item, "leechers")),
            // Each aggregator names the indexer in its own element - Jackett's jackettindexer,
            // Prowlarr's prowlarrindexer - and neither is in any specification. Reading only one of
            // them left the Indexer column empty for every result from the other, which was how
            // Prowlarr looked when first pointed at.
            IndexerName: item.Element("jackettindexer")?.Value
                ?? item.Element("prowlarrindexer")?.Value
                ?? Attribute(item, "indexer")
                ?? string.Empty,
            PublishedAt: ParseDate(item.Element("pubDate")?.Value),
            Link: link.Trim());
    }

    private static string? Attribute(XElement item, string name)
    {
        return item.Elements(Torznab + "attr")
            .FirstOrDefault(a => string.Equals(a.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("value")?.Value;
    }

    private static long ParseLong(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static int ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : TorrentSearchResult.Unknown;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
