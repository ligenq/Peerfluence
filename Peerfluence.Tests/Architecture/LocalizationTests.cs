using System.Globalization;
using System.Resources;
using Peerfluence.Properties;

namespace Peerfluence.Tests.Architecture;

public class LocalizationTests
{
    private static readonly string[] Languages =
        ["de-DE", "es-ES", "fr-FR", "it-IT", "pl-PL", "pt-PT", "ru-RU", "sv-SE", "uk-UA"];

    [Fact]
    public void EveryShippedLanguage_TranslatesEveryString()
    {
        var invariant = ReadAll(CultureInfo.InvariantCulture);
        Assert.NotEmpty(invariant);

        var untranslated = new List<string>();
        foreach (var language in Languages)
        {
            var culture = new CultureInfo(language);
            var translated = ReadAll(culture);

            foreach (var key in invariant.Keys)
            {
                // A satellite assembly falls back to the invariant string for a key it does not
                // define, so a missing translation shows up as English rather than as an error.
                if (!translated.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    untranslated.Add($"{language}: {key} is missing");
                }
            }
        }

        Assert.True(untranslated.Count == 0, string.Join(Environment.NewLine, untranslated));
    }

    [Fact]
    public void NoTranslation_AltersTheFormatPlaceholdersItWasGiven()
    {
        // Two ways a translation breaks string.Format: dropping or renumbering a placeholder, and
        // escaping it — es-ES shipped "Error: \{0}", which .NET renders with the backslash intact.
        var invariant = ReadAll(CultureInfo.InvariantCulture);
        var problems = new List<string>();

        foreach (var language in Languages)
        {
            var translated = ReadAll(new CultureInfo(language));
            foreach (var (key, english) in invariant)
            {
                if (!translated.TryGetValue(key, out var value))
                {
                    continue;
                }

                if (value.Contains("\\{", StringComparison.Ordinal))
                {
                    problems.Add($"{language}: {key} escapes a placeholder: {value}");
                }

                var expected = Placeholders(english);
                var actual = Placeholders(value);
                if (!expected.SequenceEqual(actual))
                {
                    problems.Add($"{language}: {key} has placeholders [{string.Join(",", actual)}], expected [{string.Join(",", expected)}]");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static List<string> Placeholders(string value)
    {
        return System.Text.RegularExpressions.Regex
            .Matches(value, @"\{(\d+)[^}]*\}")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    [Theory]
    [InlineData("TrackerStatus_Disabled")]
    [InlineData("Settings_AnswerInfoHashSampling")]
    [InlineData("Settings_AnswerInfoHashSamplingHint")]
    [InlineData("Settings_AllowMultipleConnectionsPerIp")]
    [InlineData("Settings_AllowMultipleConnectionsPerIpHint")]
    [InlineData("Details_Peers_AddWatermark")]
    [InlineData("Details_Peers_AddTitle")]
    [InlineData("Details_Peers_AddAccepted")]
    [InlineData("Details_Peers_AddRejected")]
    [InlineData("Downloads_ToggleDetails")]
    [InlineData("Downloads_ClipboardUnavailable")]
    [InlineData("Downloads_CopyFailed")]
    [InlineData("Simple_EmptyBody")]
    [InlineData("Simple_PauseResume")]
    [InlineData("Simple_SwitchToAdvanced")]
    [InlineData("Welcome_Title")]
    [InlineData("Welcome_Subtitle")]
    // Welcome_Simple_Title and Settings_InterfaceMode are deliberately absent: "Simple" and
    // "Interface" are the same word in French, so this guard cannot tell a real translation from a
    // missing one. Their presence is still covered by EveryShippedLanguage_TranslatesEveryString.
    [InlineData("Welcome_Simple_Body")]
    [InlineData("Welcome_Advanced_Title")]
    [InlineData("Welcome_Advanced_Body")]
    [InlineData("Downloads_Search")]
    [InlineData("Downloads_Filter_All")]
    [InlineData("Downloads_Filter_Downloading")]
    [InlineData("Downloads_Filter_Seeding")]
    [InlineData("Downloads_Filter_Completed")]
    [InlineData("Downloads_NoMatches")]
    [InlineData("Downloads_Grid_Size")]
    [InlineData("Downloads_Remove_Confirm_Many")]
    [InlineData("Settings_InterfaceModeHint")]
    [InlineData("Settings_SavedAutomatically")]
    public void RecentlyAddedStrings_DifferFromEnglishInEveryLanguage(string key)
    {
        var english = Resources.ResourceManager.GetString(key, CultureInfo.InvariantCulture);
        Assert.False(string.IsNullOrWhiteSpace(english));

        foreach (var language in Languages)
        {
            var value = Resources.ResourceManager.GetString(key, new CultureInfo(language));
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: {key} resolved to nothing");
            Assert.True(value != english, $"{language}: {key} fell back to the English string");
        }
    }

    private static Dictionary<string, string> ReadAll(CultureInfo culture)
    {
        // tryParents: false so a language is judged on what it actually defines, not on what it
        // inherits from the invariant resources. Deliberately not disposed: this is the manager's
        // own cached set, and disposing it breaks every later lookup in the process.
        var set = Resources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (set == null)
        {
            return values;
        }

        foreach (System.Collections.DictionaryEntry entry in set)
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                values[key] = value;
            }
        }

        return values;
    }
}
