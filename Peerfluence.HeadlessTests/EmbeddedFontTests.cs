using Avalonia.Media;
using Peerfluence.HeadlessTests.XUnit;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The theme carries its own typeface, so the weights it asks for have to be real ones.
/// </summary>
/// <remarks>
/// Open Sans ships from Google Fonts only as a variable font. Avalonia will happily return a
/// glyph typeface for a weight a font does not contain by smearing the regular one, reporting it
/// as <see cref="FontSimulations"/>. Synthesized weights look wrong at the sizes this interface
/// uses - heavier but blurred, with the wrong sidebearings - so these pin that every weight the
/// theme and the views ask for comes from the font's own weight axis.
/// </remarks>
public sealed class EmbeddedFontTests
{
    private const string OpenSans = "avares://Peerfluence.Theme/Assets/Fonts#Open Sans";

    [AvaloniaTheory]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(600)]
    [InlineData(700)]
    public void EveryWeightTheThemeUses_ComesFromTheFontRatherThanBeingSynthesized(int weight)
    {
        var typeface = new Typeface(new FontFamily(OpenSans), FontStyle.Normal, (FontWeight)weight);

        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface),
            $"The embedded font did not resolve at all for weight {weight}.");
        Assert.Equal(FontSimulations.None, glyphTypeface.FontSimulations);
        Assert.Equal((FontWeight)weight, glyphTypeface.Weight);
    }

    /// <summary>
    /// Open Sans has no 500, so a request for Medium is answered by a real face, not a smear.
    /// </summary>
    /// <remarks>
    /// The family ships 300, 400, 600, 700 and 800 - the same set the reference interface loads -
    /// and the theme asks for Medium in a handful of places. What matters is not which face wins
    /// but that the winner is a drawn one: a synthesized 500 would be the blurred Regular that
    /// this whole exercise exists to avoid. Recorded here because the resolved weight is the
    /// visible consequence of a gap in the font, and a future reader should not have to rediscover
    /// why Medium looks like Regular only under this variant.
    /// </remarks>
    [AvaloniaFact]
    public void MediumFallsBackToADrawnFace_BecauseTheFamilyHasNo500()
    {
        var typeface = new Typeface(new FontFamily(OpenSans), FontStyle.Normal, FontWeight.Medium);

        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface));
        Assert.Equal(FontSimulations.None, glyphTypeface.FontSimulations);
        Assert.NotEqual(FontWeight.Medium, glyphTypeface.Weight);
    }

    [AvaloniaFact]
    public void TheEmbeddedFamily_IsTheOneItClaimsToBe()
    {
        var typeface = new Typeface(new FontFamily(OpenSans));

        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface));
        Assert.Equal("Open Sans", glyphTypeface.FamilyName);
    }

    /// <summary>Cyrillic is covered, because the application ships Russian and Ukrainian.</summary>
    [AvaloniaTheory]
    [InlineData('Я', "Cyrillic capital Ya")]
    [InlineData('ї', "Ukrainian yi")]
    [InlineData('å', "a-ring, Swedish")]
    [InlineData('ł', "l-stroke, Polish")]
    public void TheEmbeddedFont_CoversTheAlphabetsTheApplicationShips(char character, string description)
    {
        var typeface = new Typeface(new FontFamily(OpenSans));
        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface));

        Assert.True(glyphTypeface.CharacterToGlyphMap.TryGetGlyph(character, out ushort glyph) && glyph != 0,
            $"The font has no glyph for {description} (U+{(int)character:X4}), so that language would fall back.");
    }
}
