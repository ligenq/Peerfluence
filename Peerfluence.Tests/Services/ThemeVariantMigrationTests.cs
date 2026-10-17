using Avalonia.Styling;
using Peerfluence.Services;
using Peerfluence.Theme;

namespace Peerfluence.Tests.Services;

/// <summary>
/// How a stored variant name becomes a variant, now that the application offers a different set
/// from the one it used to.
/// </summary>
/// <remarks>
/// Two things changed at once and both are easy to get wrong silently. Settings already on disk
/// name "Light" or "Dark", which this application no longer offers, and "System" can no longer be
/// left to <see cref="ThemeVariant.Default"/>, because Avalonia resolves that to its own neutral
/// pair rather than to the Midnight pair. Either mistake shows up as the application quietly
/// ignoring a preference, which is the kind of thing nobody reports as a bug.
/// </remarks>
public sealed class ThemeVariantMigrationTests
{
    [Theory]
    [InlineData("Midnight")]
    [InlineData("MidnightLight")]
    public void AStoredMidnightName_SurvivesUntouched(string stored)
    {
        Assert.Equal(PeerfluenceTheme.VariantFor(stored), ThemeService.Resolve(stored));
    }

    [Theory]
    // The neutral variants still ship in the package; this application maps them to the Midnight
    // of the same brightness rather than dropping the reader back on a default.
    [InlineData("Light", "MidnightLight")]
    [InlineData("Dark", "Midnight")]
    [InlineData("light", "MidnightLight")]
    [InlineData("DARK", "Midnight")]
    public void ARetiredVariantName_MigratesToTheMidnightOfTheSameBrightness(string stored, string expected)
    {
        Assert.Equal(PeerfluenceTheme.VariantFor(expected), ThemeService.Resolve(stored));
    }

    [Theory]
    [InlineData("System")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Taupe")]
    public void AnythingElse_ResolvesToOneOfTheMidnightPairRatherThanToDefault(string? stored)
    {
        var resolved = ThemeService.Resolve(stored);

        // Which of the two depends on the machine, so the assertion is that it is one of them and
        // never ThemeVariant.Default, which would paint the neutral theme this application dropped.
        Assert.True(PeerfluenceTheme.IsMidnight(resolved),
            $"'{stored}' resolved to {resolved}, which is not a Midnight variant.");
        Assert.NotEqual(ThemeVariant.Default, resolved);
    }

    [Fact]
    public void TheTwoVariants_AreDistinctAndBothKnownToTheTheme()
    {
        Assert.NotEqual(PeerfluenceTheme.Midnight, PeerfluenceTheme.MidnightLight);
        Assert.Contains(PeerfluenceTheme.Midnight, PeerfluenceTheme.Variants);
        Assert.Contains(PeerfluenceTheme.MidnightLight, PeerfluenceTheme.Variants);

        // The package keeps the neutrals for other applications even though this one hides them.
        Assert.Contains(ThemeVariant.Light, PeerfluenceTheme.Variants);
        Assert.Contains(ThemeVariant.Dark, PeerfluenceTheme.Variants);
    }

    [Fact]
    public void BothVariants_OfferTheSameAccentsAndTheSameDefault()
    {
        Assert.Equal(PeerfluenceTheme.AccentsFor(PeerfluenceTheme.Midnight),
            PeerfluenceTheme.AccentsFor(PeerfluenceTheme.MidnightLight));
        Assert.Equal(PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.Midnight),
            PeerfluenceTheme.DefaultAccentFor(PeerfluenceTheme.MidnightLight));
    }
}
