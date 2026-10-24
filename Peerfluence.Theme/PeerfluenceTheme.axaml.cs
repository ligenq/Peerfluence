using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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

    /// <summary>
    /// Roles a variant may define and the others may not.
    /// </summary>
    /// <remarks>
    /// A fill dark enough to carry white text cannot also be the accent, which has to stay light
    /// enough to read as text on a dark tint. Only Midnight needs the distinction so far, and an
    /// absent role is skipped rather than failing the accent, which is what lets a variant adopt
    /// one without every other variant having to grow a value for it.
    /// </remarks>
    private static readonly string[] OptionalRoles =
        ["AccentStrong", "AccentStrongHover", "AccentStrongPressed"];

    /// <summary>
    /// A third variant beside Light and Dark: an indigo shell with panels that sit darker than the
    /// canvas behind them, and its own typeface.
    /// </summary>
    /// <remarks>
    /// Inherits from <see cref="ThemeVariant.Dark"/> rather than standing alone, so a resource this
    /// variant does not define - including every accent, which stays swappable - resolves against
    /// the dark values instead of coming back empty. That inheritance is what keeps the variant to
    /// one dictionary of surfaces rather than a second copy of the whole palette.
    /// </remarks>
    public static ThemeVariant Midnight { get; } = new("Midnight", ThemeVariant.Dark);

    /// <summary>
    /// Midnight by day: the same indigo chrome, over a light reading surface.
    /// </summary>
    /// <remarks>
    /// Not Midnight recoloured. What makes Midnight itself is panels sitting darker than the
    /// canvas, and on a pale page that would be dark cards floating on white, which reads as
    /// broken rather than as a light theme. So this keeps the part that is recognisable - the
    /// chrome stays deep indigo at all times - and lets the content area behave the ordinary way
    /// round, with white panels above a tinted canvas. The reference interface's own light surface
    /// does exactly that, and its values are where these come from.
    /// </remarks>
    public static ThemeVariant MidnightLight { get; } = new("MidnightLight", ThemeVariant.Light);

    /// <summary>Whether <paramref name="variant"/> is one of the two Midnight variants.</summary>
    public static bool IsMidnight(ThemeVariant? variant) =>
        Equals(variant, Midnight) || Equals(variant, MidnightLight);

    /// <summary>The typeface Midnight brings with it, carried in this package.</summary>
    public static FontFamily MidnightTypeface { get; } =
        new("avares://Peerfluence.Theme/Assets/Fonts#Open Sans");

    /// <summary>The variants this theme ships, in the order a settings screen should offer them.</summary>
    public static IReadOnlyList<ThemeVariant> Variants { get; } =
        [ThemeVariant.Light, ThemeVariant.Dark, Midnight, MidnightLight];

    /// <summary>The accents Light and Dark offer, in the order a settings screen should show them.</summary>
    public static IReadOnlyList<string> Accents { get; } =
    [
        "Teal", "Indigo", "Cobalt", "Emerald", "Amber", "Rose", "Violet", "Slate",
    ];

    /// <summary>
    /// What Midnight offers instead, which is not the same set.
    /// </summary>
    /// <remarks>
    /// Its canvas is not neutral. At chroma 0.108 the background out-colours a muted accent and
    /// occupies a stretch of the wheel, so three of the eight fail on it in ways no amount of
    /// retuning fixes: Indigo sits one degree from the canvas hue and reads as the background
    /// rather than as a selection, Slate carries half the canvas's chroma and reads as disabled,
    /// and Amber is warm enough that a fill dark enough for white text turns to ochre. Sky and
    /// Cyan take their place.
    /// </remarks>
    public static IReadOnlyList<string> MidnightAccents { get; } =
    [
        "Cobalt", "Sky", "Cyan", "Teal", "Emerald", "Rose", "Violet",
    ];

    /// <summary>Every accent this theme knows, across all variants.</summary>
    public static IReadOnlyList<string> AllAccents { get; } =
        [.. Accents.Concat(MidnightAccents).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The accents <paramref name="variant"/> offers.</summary>
    public static IReadOnlyList<string> AccentsFor(ThemeVariant? variant) =>
        IsMidnight(variant) ? MidnightAccents : Accents;

    /// <summary>
    /// The variant a stored name refers to, or <see cref="ThemeVariant.Default"/> for anything
    /// this version does not recognise - including "System", and whatever a later version wrote.
    /// </summary>
    /// <remarks>
    /// Here rather than in the application, because the settings screen and the theme service both
    /// need it and a second copy of the mapping would be a second thing to forget on the next
    /// variant.
    /// </remarks>
    public static ThemeVariant VariantFor(string? name) => name switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        "Midnight" => Midnight,
        "MidnightLight" => MidnightLight,
        _ => ThemeVariant.Default,
    };

    /// <summary>The accent applied when none is chosen.</summary>
    public static string DefaultAccent => "Teal";

    /// <summary>
    /// What <paramref name="variant"/> falls back to, which differs because the sets differ.
    /// </summary>
    /// <remarks>
    /// Midnight defaults to the reference interface's own blue rather than to Teal, which it does
    /// offer but which is not what this variant is for.
    /// </remarks>
    public static string DefaultAccentFor(ThemeVariant? variant) =>
        IsMidnight(variant) ? "Cobalt" : DefaultAccent;

    private static bool Offers(ThemeVariant? variant, string accent) =>
        AccentsFor(variant).Contains(accent, StringComparer.OrdinalIgnoreCase);

    public PeerfluenceTheme(IServiceProvider? serviceProvider = null)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);

        // Initialize both variants before any controls resolve the shared accent brushes.
        UseAccent(DefaultAccent);
    }

    /// <summary>
    /// Adds or removes the styles that belong to <paramref name="variant"/>, and must be called
    /// whenever the requested variant changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A variant is mostly a palette, and a palette is resources. Two things are not: the typeface,
    /// and metrics such as a row height. Neither can be expressed as a per-variant resource. There
    /// is no resource value meaning "leave the font alone" - a setter pointing at an unresolvable
    /// DynamicResource does not skip the property, it assigns a family the font manager cannot
    /// match, and the <c>$Default</c> sentinel resolves to a last-resort face rather than to
    /// whatever the host configured, both of which render every string in the wrong font at
    /// roughly twice the width. Metrics are plain doubles shared by every variant, so overriding
    /// one in a theme dictionary would change Light and Dark with it.
    /// </para>
    /// <para>
    /// Both are therefore styles that exist only while the variant is selected. Light, Dark and
    /// Default get none, which is what leaves a host application's own font and density untouched.
    /// </para>
    /// </remarks>
    public void UseVariant(ThemeVariant? variant)
    {
        bool wanted = IsMidnight(variant);
        bool present = Contains(_midnight);
        if (wanted && !present)
        {
            Add(_midnight);
        }
        else if (!wanted && present)
        {
            Remove(_midnight);
        }
    }

    private readonly MidnightStyles _midnight = new();

    /// <summary>
    /// Switches the accent. Returns false, and changes nothing, for a name this theme does not have.
    /// </summary>
    /// <remarks>
    /// Updates variant colours while preserving the shared brushes. Fluent's static aliases hold
    /// those same brushes, so existing controls follow accent changes as well as variant changes.
    /// </remarks>
    public bool UseAccent(string? accent)
    {
        string? name = AllAccents.FirstOrDefault(
            known => string.Equals(known, accent, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return false;
        }

        var variants = new Dictionary<ThemeVariant, ResourceDictionary>();

        // Midnight is written explicitly rather than left to inherit from Dark. Inheritance would
        // hand it accents whose tints are mixed toward near-black, which go muddy on indigo, and
        // whose white-on-accent pairings do not carry at this contrast.
        foreach (var variant in Variants)
        {
            // The sets differ, so a name one variant offers may be one another has never heard of.
            // That variant takes its own default rather than failing the call: the accent is a
            // single stored preference, and switching variants must not leave the other two
            // holding colours from whichever accent happened to be applied last.
            string effective = Offers(variant, name) ? name : DefaultAccentFor(variant);

            var colours = new ResourceDictionary();
            foreach (string role in Roles)
            {
                if (!Resources.TryGetResource($"{effective}{role}Color", variant, out object? colour)
                    || colour is null)
                {
                    return false;
                }

                colours[$"Pf{role}Color"] = colour;
            }

            foreach (string role in OptionalRoles)
            {
                if (Resources.TryGetResource($"{effective}{role}Color", variant, out object? optional)
                    && optional is not null)
                {
                    colours[$"Pf{role}Color"] = optional;
                }
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
