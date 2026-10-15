using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Peerfluence.Theme;

/// <summary>
/// The Peerfluence look for Avalonia: light and dark variants, a selectable accent, and control
/// styles over Avalonia's Fluent templates.
/// </summary>
/// <remarks>
/// Fluent supplies the templates; this supplies the colours, metrics and the parts of the styling
/// that differ. Restyling rather than retemplating keeps the surface small enough to maintain, and
/// inherits the accessibility and input behaviour a hand-written template would have to redo.
/// </remarks>
public sealed class PeerfluenceTheme : Styles
{
    private static readonly string[] Roles =
        ["Accent", "AccentHover", "AccentPressed", "AccentSubtle", "AccentMuted", "TextOnAccent"];

    /// <summary>The accents this theme ships, in the order a settings screen should offer them.</summary>
    public static IReadOnlyList<string> Accents { get; } =
    [
        "Teal", "Indigo", "Cobalt", "Emerald", "Amber", "Rose", "Violet", "Slate",
    ];

    /// <summary>The accent applied when none is chosen.</summary>
    public static string DefaultAccent => "Teal";

    public PeerfluenceTheme(IServiceProvider? serviceProvider = null)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);

        // Initialize both variants before any controls resolve the shared accent brushes.
        UseAccent(DefaultAccent);
    }

    /// <summary>
    /// Switches the accent. Returns false, and changes nothing, for a name this theme does not have.
    /// </summary>
    /// <remarks>
    /// Updates variant colours while preserving the shared brushes. Fluent's static aliases hold
    /// those same brushes, so existing controls follow accent changes as well as variant changes.
    /// </remarks>
    public bool UseAccent(string? accent)
    {
        string? name = Accents.FirstOrDefault(
            known => string.Equals(known, accent, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return false;
        }

        var variants = new Dictionary<ThemeVariant, ResourceDictionary>();
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var colours = new ResourceDictionary();
            foreach (string role in Roles)
            {
                if (!Resources.TryGetResource($"{name}{role}Color", variant, out object? colour)
                    || colour is null)
                {
                    return false;
                }

                colours[$"Pf{role}Color"] = colour;
            }

            colours["SystemAccentColor"] = colours["PfAccentColor"];
            variants[variant] = colours;
        }

        foreach (var (variant, colours) in variants)
        {
            Resources.ThemeDictionaries[variant] = colours;
        }

        return true;
    }
}
