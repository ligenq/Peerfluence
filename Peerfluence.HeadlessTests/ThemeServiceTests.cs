using Peerfluence.Core.Config;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// Headless rather than a plain unit test, and that is the whole point: <c>ApplyPalette</c> returns
/// early when there is no Application, so without one running these never reach the lookup that
/// threw. A version of this in the unit project passed whether or not the fix was present.
/// </summary>
public sealed class ThemeServiceTests
{
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Aubergine")]
    public void AThemeItCannotName_IsIgnoredRatherThanThrown(string? colorTheme)
    {
        // The name comes from settings, so it can be absent, blank, or written by a version that
        // knew more themes than this one.
        new ThemeService().Apply(new ThemeSettings
        {
            ThemeVariant = "System",
            ColorTheme = colorTheme!,
            BackgroundStyle = "GradientSoft"
        });
    }

    [AvaloniaFact]
    public void AVariantAndBackgroundItCannotName_AreIgnoredToo()
    {
        new ThemeService().Apply(new ThemeSettings
        {
            ThemeVariant = null!,
            ColorTheme = null!,
            BackgroundStyle = null!
        });
    }
}
