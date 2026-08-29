using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// Holds the English interface text to Microsoft's writing style, which is the house style here by
/// virtue of this being a Windows application.
/// </summary>
/// <remarks>
/// <para>
/// English only. Capitalisation is a property of a language rather than of a product - German
/// capitalises every noun - so the translations are left to their own conventions and only the
/// source strings are judged.
/// </para>
/// <para>
/// The Microsoft Style Guide: "Microsoft style uses sentence-style capitalization. That means
/// everything is lowercase except the first word and proper nouns, which include the names of
/// brands, products, and services", and, for anyone reaching for the older convention, "use
/// sentence-style capitalization in most titles and headings".
/// </para>
/// </remarks>
public sealed class UiTextStyleTests
{
    /// <summary>
    /// The words that stay capitalised: brand and product names, acronyms, and units.
    /// </summary>
    /// <remarks>
    /// A list rather than a cleverness, because no rule distinguishes a product name from a common
    /// noun. Adding to it is the correct response to a false positive; lowercasing a brand is not.
    /// </remarks>
    private static readonly HashSet<string> ProperNouns =
    [
        "Peerfluence", "Prowlarr", "Jackett", "VLC", "BitTorrent", "Torznab", "Velopack",
        "Windows", "GitHub", "Transmission", "Avalonia",
        "DHT", "UPnP", "NAT-PMP", "UDP", "TCP", "IP", "IPv4", "IPv6", "URL", "URLs", "BEP",
        "GeoIP", "MCP", "RPC", "API", "UI", "OS", "SOCKS5", "HTTP", "HTTPS", "V1", "V2",
        "B/s", "KiB/s", "MB", "GB", "KB", "TB",
    ];

    [Fact]
    public void EveryEnglishLabel_UsesSentenceCase()
    {
        var offences = EnglishStrings()
            .Where(entry => IsStandalonePhrase(entry.Value))
            .SelectMany(entry => TitleCasedWords(entry.Value)
                .Select(word => $"  {entry.Key} = \"{entry.Value}\" capitalises \"{word}\""))
            .ToList();

        Assert.True(
            offences.Count == 0,
            $"{offences.Count} labels are not in sentence case. Lowercase the word, or add it to "
                + $"ProperNouns if it names a brand, product or protocol:{Environment.NewLine}"
                + string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void NoControlLabel_EndsWithAPeriod()
    {
        // "Don't place [periods] at the end of control labels", while supplemental text that forms
        // a complete sentence should have one. The difference is not visible in the resx, so it is
        // taken from the markup: a string bound to Content or Header is a label, and everything
        // else - a status line, a hint, a message - is left alone.
        var labelKeys = KeysUsedAsControlLabels();
        Assert.NotEmpty(labelKeys);

        var offences = EnglishStrings()
            .Where(entry => labelKeys.Contains(entry.Key))
            // An ellipsis is not a full stop. "Browse..." is a command saying it will ask for more,
            // which is a different rule in the same guidance.
            .Where(entry => entry.Value.TrimEnd().EndsWith('.')
                && !entry.Value.TrimEnd().EndsWith("...", StringComparison.Ordinal))
            .Select(entry => $"  {entry.Key} = \"{entry.Value}\" is a control label and ends with a period")
            .ToList();

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    // ------------------------------------------------------------------------------- reading --

    /// <summary>
    /// A short standalone phrase - a button, a label, a heading - as opposed to a sentence or a
    /// formatted message, where a capital may begin a new sentence and mean something.
    /// </summary>
    private static bool IsStandalonePhrase(string value)
    {
        return value.Split(' ').Length is >= 2 and <= 6
            && !value.Contains('{', StringComparison.Ordinal)
            && !value.TrimEnd().EndsWith('.')
            && !value.Contains(". ", StringComparison.Ordinal);
    }

    private static IEnumerable<string> TitleCasedWords(string value)
    {
        return value
            .Split(' ')
            .Skip(1)
            .Select(word => word.Trim('.', ',', ':', '(', ')', '?', '!'))
            .Where(word => Regex.IsMatch(word, "^[A-Z][a-z]"))
            .Where(word => !ProperNouns.Contains(word));
    }

    /// <summary>
    /// The resource keys the markup binds to a control's own label.
    /// </summary>
    private static HashSet<string> KeysUsedAsControlLabels()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var reference = new Regex(@"\{\s*m:L\s+([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);

        foreach (var file in Directory.EnumerateFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories))
        {
            foreach (var attribute in XDocument.Load(file).Root!.DescendantsAndSelf().Attributes())
            {
                if (attribute.Name.LocalName is not ("Content" or "Header"))
                {
                    continue;
                }

                var match = reference.Match(attribute.Value);
                if (match.Success)
                {
                    keys.Add(match.Groups[1].Value);
                }
            }
        }

        return keys;
    }

    private static IEnumerable<KeyValuePair<string, string>> EnglishStrings()
    {
        var path = Path.Combine(ProjectDirectory(), "Properties", "Resources.resx");
        Assert.True(File.Exists(path), $"The English resources were not found at {path}.");

        var entries = XDocument.Load(path)
            .Root!
            .Elements("data")
            .Where(data => data.Attribute("name") is not null && data.Element("value") is not null)
            .Select(data => new KeyValuePair<string, string>(
                data.Attribute("name")!.Value,
                data.Element("value")!.Value))
            .ToList();

        Assert.NotEmpty(entries);
        return entries;
    }

    private static string ViewsDirectory() => Path.Combine(ProjectDirectory(), "Views");

    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Peerfluence", "Peerfluence.csproj");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the Peerfluence project directory above {AppContext.BaseDirectory}.");
    }
}
