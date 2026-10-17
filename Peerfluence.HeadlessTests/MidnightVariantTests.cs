using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Core.Config;
using Peerfluence.Services;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.Views;
using Peerfluence.Theme;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The third variant: an indigo shell whose panels sit darker than the canvas behind them.
/// </summary>
/// <remarks>
/// Pins the two things that are easy to break silently. A custom <see cref="ThemeVariant"/> that
/// is not reached by a lookup falls back to its inherit parent and simply renders as Dark, which
/// looks plausible rather than broken; and the surface inversion is the whole point of the
/// variant, so a palette edit that quietly restores the usual ordering should fail here.
/// </remarks>
public sealed class MidnightVariantTests
{
    /// <summary>
    /// Reads a colour out of <paramref name="theme"/>, which has to be the instance under test.
    /// </summary>
    /// <remarks>
    /// This took a theme of its own making until it was caught: constructing one re-applies the
    /// default accent, so every accent assertion measured the default rather than the accent it
    /// had just selected, and passed for the wrong reason. Taking the instance is what makes the
    /// reading belong to the test.
    /// </remarks>
    private static Color Resolve(PeerfluenceTheme theme, string key, ThemeVariant variant)
    {
        Assert.True(theme.TryGetResource(key, variant, out object? value),
            $"{key} did not resolve in {variant}.");
        return Assert.IsType<Color>(value);
    }

    /// <summary>For the palette itself, where no accent has been chosen.</summary>
    private static Color Resolve(string key, ThemeVariant variant) =>
        Resolve(new PeerfluenceTheme(), key, variant);

    [Fact]
    public void Midnight_InheritsFromDark_SoUndefinedResourcesStillResolve()
    {
        Assert.Equal(ThemeVariant.Dark, PeerfluenceTheme.Midnight.InheritVariant);
        Assert.Equal("Midnight", PeerfluenceTheme.Midnight.Key);
        Assert.Equal([ThemeVariant.Light, ThemeVariant.Dark, PeerfluenceTheme.Midnight,
            PeerfluenceTheme.MidnightLight], PeerfluenceTheme.Variants);
    }

    [AvaloniaFact]
    public void Midnight_IsItsOwnPalette_NotDarkUnderAnotherName()
    {
        // A custom variant whose dictionary is never reached resolves entirely through its parent,
        // which renders as Dark and reads as "the theme just did not change".
        Assert.NotEqual(Resolve("PfCanvasColor", ThemeVariant.Dark),
            Resolve("PfCanvasColor", PeerfluenceTheme.Midnight));
        Assert.NotEqual(Resolve("PfSurfaceColor", ThemeVariant.Dark),
            Resolve("PfSurfaceColor", PeerfluenceTheme.Midnight));
    }

    /// <summary>Panels sit darker than the canvas, which is the inversion the variant exists for.</summary>
    [AvaloniaFact]
    public void Midnight_PutsItsPanelsBelowItsCanvas_UnlikeEveryOtherVariant()
    {
        static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        double canvas = Luminance(Resolve("PfCanvasColor", PeerfluenceTheme.Midnight));
        double surface = Luminance(Resolve("PfSurfaceColor", PeerfluenceTheme.Midnight));
        Assert.True(surface < canvas,
            $"Midnight panels ({surface:F3}) must sit below the canvas ({canvas:F3}).");

        // The other two keep the ordinary arrangement, so this is a deliberate difference rather
        // than an accident that happens to be everywhere.
        foreach (var ordinary in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(Luminance(Resolve("PfSurfaceColor", ordinary))
                > Luminance(Resolve("PfCanvasColor", ordinary)),
                $"{ordinary} should lift its surfaces above its canvas.");
        }
    }

    /// <summary>
    /// Midnight brings a typeface; Light and Dark leave the host's alone.
    /// </summary>
    /// <remarks>
    /// Asserted through the style collection rather than a resource lookup because that is how it
    /// is applied: there is no resource value meaning "leave the font alone", so the theme adds
    /// and removes a style instead. The second half matters as much as the first - a theme that
    /// sets a font under every variant would silently replace the font of any application that
    /// merely wanted the Light palette.
    /// </remarks>
    [AvaloniaFact]
    public void Midnight_BringsItsOwnTypeface_AndTheOthersLeaveTheHostsAlone()
    {
        var theme = new PeerfluenceTheme();
        int baseline = theme.Count;

        theme.UseVariant(PeerfluenceTheme.Midnight);
        Assert.Equal(baseline + 1, theme.Count);
        Assert.Contains("Open Sans", PeerfluenceTheme.MidnightTypeface.ToString(), StringComparison.Ordinal);

        foreach (var ordinary in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Default })
        {
            theme.UseVariant(ordinary);
            Assert.Equal(baseline, theme.Count);
        }

        // Repeating a variant must not stack copies of the style.
        theme.UseVariant(PeerfluenceTheme.Midnight);
        theme.UseVariant(PeerfluenceTheme.Midnight);
        Assert.Equal(baseline + 1, theme.Count);
    }

    /// <summary>A real window picks up the indigo canvas and the embedded typeface.</summary>
    /// <remarks>
    /// Driven through ThemeService, which is the only path that applies both halves. The variant
    /// is requested on the application rather than the window because that is where it takes
    /// effect: setting it on a window does change that window's ActualThemeVariant, but a
    /// DynamicResource inside a style setter resolves against the application's variant, so the
    /// window reports Midnight while still painting the Light canvas.
    /// </remarks>
    [AvaloniaFact]
    public void AWindowUnderMidnight_PaintsTheIndigoCanvasAndUsesTheEmbeddedFont()
    {
        var application = Application.Current!;
        var original = application.RequestedThemeVariant;
        var theme = application.Styles.OfType<PeerfluenceTheme>().Single();
        new ThemeService().Apply(new ThemeSettings { ThemeVariant = "Midnight", ColorTheme = "Teal" });
        var window = new Window { Width = 400, Height = 200 };
        window.Show();
        try
        {
            Assert.Equal(PeerfluenceTheme.Midnight, window.ActualThemeVariant);
            Assert.Equal(Resolve("PfCanvasColor", PeerfluenceTheme.Midnight),
                Assert.IsType<SolidColorBrush>(window.Background).Color);
            Assert.Contains("Open Sans", window.FontFamily.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            new ThemeService().Apply(new ThemeSettings { ThemeVariant = "Dark", ColorTheme = "Teal" });
            theme.UseVariant(original);
            application.RequestedThemeVariant = original;
        }
    }

    /// <summary>
    /// Midnight's own metrics apply to it and to nothing else.
    /// </summary>
    /// <remarks>
    /// Row height and padding are plain doubles shared by every variant, so the only way one
    /// variant can differ is a style that comes and goes with it. The second half of this is the
    /// part worth guarding: a metric left behind after switching away would quietly re-space Light
    /// and Dark, and nothing else in the suite would notice.
    /// </remarks>
    [AvaloniaFact]
    public async Task MidnightsMetrics_ApplyToItAloneAndAreGivenBack()
    {
        var application = Application.Current!;
        var original = application.RequestedThemeVariant;
        var theme = application.Styles.OfType<PeerfluenceTheme>().Single();
        var model = TestHelpers.CreateMainWindowViewModel();
        // The sidebar only exists in advanced mode; simple mode has nowhere to navigate to.
        await model.ChooseAdvancedModeCommand.ExecuteAsync(null);
        var view = new MainWindowView(model) { Width = 1100, Height = 700 };
        var window = view;
        view.Show();
        try
        {
            static ListBoxItem FirstItem(Visual root) => root.GetVisualDescendants()
                .OfType<ListBox>().First(list => list.Classes.Contains("nav"))
                .GetVisualDescendants().OfType<ListBoxItem>().First();

            // Driven through the theme rather than through ThemeService, which migrates a stored
            // "Dark" to Midnight and so cannot produce an ordinary variant to compare against any
            // more. The package still ships the neutral pair for other applications, and it is
            // their metrics this is measuring against.
            Ordinary(application, theme);
            Layout(window);
            double ordinaryHeight = FirstItem(view).MinHeight;

            new ThemeService().Apply(new ThemeSettings { ThemeVariant = "Midnight", ColorTheme = "Cobalt" });
            Layout(window);
            var item = FirstItem(view);
            Assert.True(item.MinHeight > ordinaryHeight,
                $"Midnight rows ({item.MinHeight}) should be taller than {ordinaryHeight}.");
            Assert.Equal(FontWeight.Light, item.FontWeight);
            // Nothing may inset the row, or the selected fill cannot reach the sidebar's edges.
            Assert.Equal(default, item.Margin);
            Assert.Equal(default(CornerRadius), item.CornerRadius);

            // The sidebar stops being a flat fill.
            var sidebar = view.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("sidebar"));
            Assert.IsType<LinearGradientBrush>(sidebar.Background);

            Ordinary(application, theme);
            Layout(window);
            Assert.Equal(ordinaryHeight, FirstItem(view).MinHeight);
            Assert.IsType<SolidColorBrush>(view.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("sidebar")).Background);
        }
        finally
        {
            window.Close();
            theme.UseVariant(original);
            application.RequestedThemeVariant = original;
        }
    }

    /// <summary>Puts the application on a neutral variant, which only the package still offers.</summary>
    private static void Ordinary(Application application, PeerfluenceTheme theme)
    {
        application.RequestedThemeVariant = ThemeVariant.Dark;
        theme.UseVariant(ThemeVariant.Dark);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>
    /// Every Midnight accent is readable on the surfaces it actually sits on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PfAccent</c> is a text colour in a dozen places, usually on <c>PfAccentSubtle</c>, so
    /// WCAG AA for normal text is the bar rather than the 3:1 allowed for UI shapes. The accent
    /// colours taken from the reference interface do not clear it on indigo - its blue measures
    /// 3.28:1 on the subtle fill - so they are lifted toward white only as far as needed, which is
    /// what the generator does and what this records.
    /// </para>
    /// <para>
    /// Scoped to Midnight deliberately. Light and Dark predate this and do not all reach AA, with
    /// Dark's white-on-accent pairings as low as 2.54:1; widening this test would fail on that
    /// rather than on anything Midnight does, and fixing it is a separate decision.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void EveryMidnightAccent_MeetsAaOnTheSurfacesItSitsOn()
    {
        var theme = new PeerfluenceTheme();
        var surface = Resolve("PfSurfaceColor", PeerfluenceTheme.Midnight);
        var canvas = Resolve("PfCanvasColor", PeerfluenceTheme.Midnight);
        var failures = new List<string>();

        foreach (string accent in PeerfluenceTheme.MidnightAccents)
        {
            Assert.True(theme.UseAccent(accent), $"{accent} did not apply.");
            var value = Resolve(theme, "PfAccentColor", PeerfluenceTheme.Midnight);
            var subtle = Resolve(theme, "PfAccentSubtleColor", PeerfluenceTheme.Midnight);
            var onAccent = Resolve(theme, "PfTextOnAccentColor", PeerfluenceTheme.Midnight);

            foreach (var (label, against) in new[]
                     { ("surface", surface), ("canvas", canvas), ("its subtle fill", subtle) })
            {
                double measured = Contrast(value, against);
                if (measured < 4.5)
                {
                    failures.Add($"{accent} reads {measured:F2}:1 against {label}.");
                }
            }

            double onFill = Contrast(onAccent, value);
            if (onFill < 4.5)
            {
                failures.Add($"{accent}'s text-on-accent reads {onFill:F2}:1.");
            }
        }

        theme.UseAccent(PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.Midnight));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>WCAG 2.1 relative-luminance contrast, so the bar is the published one.</summary>
    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            double v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) =>
            (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    /// Every accent has a fill that white text can sit on, for all eight choices.
    /// </summary>
    /// <remarks>
    /// The reference interface puts white on every button and on the selected destination. The
    /// accent alone cannot deliver that: it has to stay light enough to read as text on its own
    /// tint, and a colour light enough for that cannot carry white - Amber measures 2.1:1 under
    /// it. So the fill is a second colour, and this is what stops someone later "simplifying" the
    /// two back into one and quietly making every button label unreadable on half the accents.
    /// </remarks>
    [AvaloniaFact]
    public void EveryAccent_HasAFillThatWhiteTextCanSitOn()
    {
        var theme = new PeerfluenceTheme();
        var white = Colors.White;
        var canvas = Resolve("PfCanvasColor", PeerfluenceTheme.Midnight);
        var failures = new List<string>();

        foreach (string accent in PeerfluenceTheme.MidnightAccents)
        {
            Assert.True(theme.UseAccent(accent), $"{accent} did not apply.");
            var fill = Resolve(theme, "PfAccentStrongColor", PeerfluenceTheme.Midnight);
            var hover = Resolve(theme, "PfAccentStrongHoverColor", PeerfluenceTheme.Midnight);

            double rest = Contrast(white, fill);
            double over = Contrast(white, hover);
            if (rest < 4.5)
            {
                failures.Add($"{accent}: white on its fill reads {rest:F2}:1.");
            }

            if (over < 4.5)
            {
                failures.Add($"{accent}: white on its hover fill reads {over:F2}:1.");
            }

            // The shape has to be visible, not only its label.
            if (Contrast(fill, canvas) < 3.0)
            {
                failures.Add($"{accent}: its fill reads {Contrast(fill, canvas):F2}:1 against the canvas.");
            }

            // Hover has to be seen to change, or the control stops answering the pointer.
            if (fill == hover)
            {
                failures.Add($"{accent}: its hover fill is identical to its resting fill.");
            }
        }

        theme.UseAccent(PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.Midnight));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>The fill is darker than the accent, which is the whole reason it exists.</summary>
    [AvaloniaFact]
    public void TheStrongFill_IsDarkerThanTheAccentItBelongsTo()
    {
        var theme = new PeerfluenceTheme();
        foreach (string accent in PeerfluenceTheme.MidnightAccents)
        {
            theme.UseAccent(accent);
            var value = Resolve(theme, "PfAccentColor", PeerfluenceTheme.Midnight);
            var fill = Resolve(theme, "PfAccentStrongColor", PeerfluenceTheme.Midnight);
            Assert.True(Contrast(fill, Colors.Black) < Contrast(value, Colors.Black),
                $"{accent}: its fill is not darker than its accent.");
        }

        theme.UseAccent(PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.Midnight));
    }

    /// <summary>Light and Dark are not given the extra role, and are not harmed by its absence.</summary>
    [AvaloniaFact]
    public void TheExtraRole_IsMidnightsAloneAndTheOtherAccentsStillApply()
    {
        var theme = new PeerfluenceTheme();
        foreach (string accent in PeerfluenceTheme.MidnightAccents)
        {
            Assert.True(theme.UseAccent(accent),
                $"{accent} stopped applying once an optional role existed.");
            foreach (var ordinary in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Assert.False(theme.TryGetResource("PfAccentStrongColor", ordinary, out object? value)
                    && value is Color, $"{ordinary} should not have gained a strong fill.");
            }
        }

        theme.UseAccent(PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.Midnight));
    }

    /// <summary>
    /// The variants offer different accents, and an accent one has is not forced on the others.
    /// </summary>
    /// <remarks>
    /// Three of the eight fail measurably against the indigo canvas and two replacements only make
    /// sense there, so the sets diverge. What has to hold is that a single stored preference still
    /// resolves everywhere: asking for an accent a variant has never heard of must give that
    /// variant its own default rather than failing the call or leaving it painted in whatever was
    /// applied last.
    /// </remarks>
    [AvaloniaFact]
    public void TheVariantsOfferDifferentAccents_AndEachResolvesWhateverIsStored()
    {
        Assert.DoesNotContain("Indigo", PeerfluenceTheme.MidnightAccents);
        Assert.DoesNotContain("Slate", PeerfluenceTheme.MidnightAccents);
        Assert.DoesNotContain("Amber", PeerfluenceTheme.MidnightAccents);
        Assert.Contains("Sky", PeerfluenceTheme.MidnightAccents);
        Assert.Contains("Cyan", PeerfluenceTheme.MidnightAccents);
        Assert.DoesNotContain("Sky", PeerfluenceTheme.Accents);
        Assert.Equal("Cobalt", PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.Midnight));
        Assert.Equal(PeerfluenceTheme.DefaultAccent,
            PeerfluenceTheme.DefaultAccentFor(ThemeVariant.Dark));

        var theme = new PeerfluenceTheme();

        // An accent only Midnight has: Light and Dark take their own default, and still paint.
        Assert.True(theme.UseAccent("Sky"));
        var sky = Resolve(theme, "PfAccentColor", PeerfluenceTheme.Midnight);
        theme.UseAccent("Cobalt");
        Assert.NotEqual(sky, Resolve(theme, "PfAccentColor", PeerfluenceTheme.Midnight));
        theme.UseAccent("Sky");
        foreach (var ordinary in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            var fallback = Resolve(theme, "PfAccentColor", ordinary);
            theme.UseAccent("Sky");
            Assert.Equal(fallback, Resolve(theme, "PfAccentColor", ordinary));
        }

        // And one only Light and Dark have: Midnight takes Cobalt rather than muddy inherited Amber.
        theme.UseAccent("Amber");
        var midnightUnderAmber = Resolve(theme, "PfAccentColor", PeerfluenceTheme.Midnight);
        theme.UseAccent("Cobalt");
        Assert.Equal(Resolve(theme, "PfAccentColor", PeerfluenceTheme.Midnight), midnightUnderAmber);

        // A name no variant has still fails, so a typo is not silently absorbed.
        Assert.False(theme.UseAccent("Chartreuse"));

        theme.UseAccent(PeerfluenceTheme.DefaultAccent);
    }

    /// <summary>
    /// Midnight Light is a light theme, not Midnight recoloured.
    /// </summary>
    /// <remarks>
    /// The two share an identity but not a structure. Midnight puts its panels below its canvas;
    /// on a pale page that would be dark cards floating on white, so the light variant uses the
    /// ordinary arrangement. Both halves are asserted, because getting the inversion backwards in
    /// either one is the mistake that would look deliberate.
    /// </remarks>
    [AvaloniaFact]
    public void MidnightLight_IsALightSurface_WithTheOrdinaryPanelOrdering()
    {
        static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        double canvas = Luminance(Resolve("PfCanvasColor", PeerfluenceTheme.MidnightLight));
        double surface = Luminance(Resolve("PfSurfaceColor", PeerfluenceTheme.MidnightLight));
        double text = Luminance(Resolve("PfTextColor", PeerfluenceTheme.MidnightLight));

        Assert.True(canvas > 0.85, $"the canvas should be light, measured {canvas:F3}.");
        Assert.True(surface > canvas, "panels sit above the canvas here, unlike under Midnight.");
        Assert.True(text < 0.2, $"the ink should be dark, measured {text:F3}.");

        // And its ink is indigo rather than neutral, which is what keeps it in the family.
        var ink = Resolve("PfTextColor", PeerfluenceTheme.MidnightLight);
        Assert.True(ink.B > ink.R + 40, $"the ink should carry the indigo cast, got {ink}.");

        Assert.Equal(ThemeVariant.Light, PeerfluenceTheme.MidnightLight.InheritVariant);
        Assert.True(PeerfluenceTheme.IsMidnight(PeerfluenceTheme.MidnightLight));
        Assert.True(PeerfluenceTheme.IsMidnight(PeerfluenceTheme.Midnight));
        Assert.False(PeerfluenceTheme.IsMidnight(ThemeVariant.Dark));
    }

    /// <summary>Both Midnight variants get the shared chrome and typeface; the neutrals do not.</summary>
    [AvaloniaFact]
    public void BothMidnightVariants_TakeTheSharedChrome()
    {
        var theme = new PeerfluenceTheme();
        int baseline = theme.Count;

        foreach (var variant in new[] { PeerfluenceTheme.Midnight, PeerfluenceTheme.MidnightLight })
        {
            theme.UseVariant(variant);
            Assert.Equal(baseline + 1, theme.Count);
        }

        foreach (var ordinary in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Default })
        {
            theme.UseVariant(ordinary);
            Assert.Equal(baseline, theme.Count);
        }
    }

    /// <summary>Its accents are darkened for a white page, not the dark variant's lifted ones.</summary>
    [AvaloniaFact]
    public void MidnightLightAccents_AreReadableOnWhiteAndDifferFromMidnights()
    {
        var theme = new PeerfluenceTheme();
        var white = Resolve("PfSurfaceColor", PeerfluenceTheme.MidnightLight);
        var failures = new List<string>();

        foreach (string accent in PeerfluenceTheme.MidnightAccents)
        {
            Assert.True(theme.UseAccent(accent), $"{accent} did not apply.");
            var light = Resolve(theme, "PfAccentColor", PeerfluenceTheme.MidnightLight);
            var dark = Resolve(theme, "PfAccentColor", PeerfluenceTheme.Midnight);
            var subtle = Resolve(theme, "PfAccentSubtleColor", PeerfluenceTheme.MidnightLight);

            Assert.NotEqual(dark, light);
            foreach (var (label, against) in new[] { ("white", white), ("its subtle fill", subtle) })
            {
                double measured = Contrast(light, against);
                if (measured < 4.5)
                {
                    failures.Add($"{accent} reads {measured:F2}:1 against {label}.");
                }
            }

            double onFill = Contrast(Colors.White,
                Resolve(theme, "PfAccentStrongColor", PeerfluenceTheme.MidnightLight));
            if (onFill < 4.5)
            {
                failures.Add($"{accent}: white on its fill reads {onFill:F2}:1.");
            }
        }

        theme.UseAccent(PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.Midnight));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>Midnight's accents are its own, not Dark's showing through the inherit chain.</summary>
    [AvaloniaFact]
    public void MidnightAccents_AreTunedForIndigoRatherThanInheritedFromDark()
    {
        var theme = new PeerfluenceTheme();
        foreach (string accent in PeerfluenceTheme.MidnightAccents)
        {
            theme.UseAccent(accent);
            // The tints are the tell: mixed toward indigo here, toward near-black in Dark.
            Assert.NotEqual(Resolve(theme, "PfAccentSubtleColor", ThemeVariant.Dark),
                Resolve(theme, "PfAccentSubtleColor", PeerfluenceTheme.Midnight));
            Assert.NotEqual(Resolve(theme, "PfAccentMutedColor", ThemeVariant.Dark),
                Resolve(theme, "PfAccentMutedColor", PeerfluenceTheme.Midnight));
        }

        theme.UseAccent(PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.Midnight));
    }

    [AvaloniaFact]
    public void EveryAccent_StillWorksUnderMidnight()
    {
        var theme = new PeerfluenceTheme();
        foreach (string accent in PeerfluenceTheme.MidnightAccents)
        {
            Assert.True(theme.UseAccent(accent), $"{accent} did not apply.");
            Assert.True(theme.TryGetResource("PfAccentColor", PeerfluenceTheme.Midnight, out object? value)
                && value is Color, $"{accent} left Midnight without an accent colour.");
        }
    }
}
