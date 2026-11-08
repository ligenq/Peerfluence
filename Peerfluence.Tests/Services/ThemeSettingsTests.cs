using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Services;

public sealed class ThemeSettingsTests
{
    [Fact]
    public void ANewProfile_FollowsTheSystem_InCobalt()
    {
        var settings = new ThemeSettings();

        Assert.Equal("System", settings.ThemeVariant);
        Assert.Equal("Cobalt", settings.ColorTheme);
    }

    /// <summary>Teal was the default, so a profile from before the move saved it whether or not it was chosen.</summary>
    [Theory]
    [InlineData("Teal")]
    [InlineData("teal")]
    public void TealFromBeforeTheMove_BecomesCobalt(string saved)
    {
        var settings = new ThemeSettings { ColorTheme = saved };

        Assert.True(settings.ApplyNewerDefaults());

        Assert.Equal("Cobalt", settings.ColorTheme);
        Assert.Equal(ThemeSettings.CurrentDefaultsRevision, settings.DefaultsRevision);
    }

    /// <summary>Only Teal was ever the default, so any other accent was chosen and stays.</summary>
    [Theory]
    [InlineData("Sky")]
    [InlineData("Violet")]
    [InlineData("Cobalt")]
    public void AnyOtherAccentFromBeforeTheMove_Stays(string saved)
    {
        var settings = new ThemeSettings { ColorTheme = saved };

        Assert.True(settings.ApplyNewerDefaults());

        Assert.Equal(saved, settings.ColorTheme);
        Assert.Equal(ThemeSettings.CurrentDefaultsRevision, settings.DefaultsRevision);
    }

    /// <summary>
    /// Once moved, a Teal is a choice. A profile from a later version, which may know defaults this one
    /// does not, is left alone as well.
    /// </summary>
    [Theory]
    [InlineData(ThemeSettings.CurrentDefaultsRevision)]
    [InlineData(ThemeSettings.CurrentDefaultsRevision + 1)]
    public void TealChosenSinceTheMove_Stays(int revision)
    {
        var settings = new ThemeSettings { ColorTheme = "Teal", DefaultsRevision = revision };

        Assert.False(settings.ApplyNewerDefaults());

        Assert.Equal("Teal", settings.ColorTheme);
        Assert.Equal(revision, settings.DefaultsRevision);
    }
}
