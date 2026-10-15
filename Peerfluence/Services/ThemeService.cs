using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using Peerfluence.Theme;

namespace Peerfluence.Services;

public sealed class ThemeService : IThemeService
{
    /// <summary>
    /// Accent names that existed before the theme was replaced, and what they became.
    /// </summary>
    /// <remarks>
    /// Settings already on disk name these. Mapping them is three lines; the alternative is every
    /// existing installation silently reverting to the default accent on upgrade, which reads as
    /// the update having lost a preference.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Renamed =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mint"] = "Emerald",
            ["Vibrant"] = "Violet",
            ["Solar"] = "Amber",
        };

    public void Apply(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ApplyVariant(settings.ThemeVariant);
        ApplyAccent(settings.ColorTheme);
    }

    private static void ApplyVariant(string variant)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = variant switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,

            // Anything else, including "System" and a value this version does not know, follows
            // the operating system.
            _ => ThemeVariant.Default,
        };
    }

    private static void ApplyAccent(string? accent)
    {
        if (Application.Current?.Styles.OfType<PeerfluenceTheme>().FirstOrDefault() is not { } theme)
        {
            return;
        }

        if (accent is not null && Renamed.TryGetValue(accent, out string? current))
        {
            accent = current;
        }

        // Falls back rather than throwing: the name arrives from a settings file, so it can be
        // absent, or something a later version wrote.
        if (!theme.UseAccent(accent))
        {
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
        }
    }
}
