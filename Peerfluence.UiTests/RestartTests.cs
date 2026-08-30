using FlaUI.Core.AutomationElements;

namespace Peerfluence.UiTests;

/// <summary>
/// What the application remembers when it is closed and opened again.
/// </summary>
/// <remarks>
/// Nothing else here can ask this. A unit test can check that a setting is written and a headless
/// test can check that a view model holds it, but whether the application reads back what it wrote
/// needs a second process reading the first one's file.
/// </remarks>
public sealed class RestartTests
{
    [Fact]
    public void TheChosenInterface_IsNotAskedAboutTwice()
    {
        using var first = new RunningApplication();
        first.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => first.Exists("AddMagnetButton"), "the downloads screen");

        using var second = first.Restart();

        Assert.False(
            second.Exists("ChooseAdvancedModeButton"),
            "the welcome screen came back after a choice had already been made");
        Assert.True(second.Exists("AddMagnetButton"), second.Describe());
    }

    [Fact]
    public void ASettingChangedInTheInterface_SurvivesARestart()
    {
        using var first = new RunningApplication();
        first.Find("ChooseSimpleModeButton").AsButton().Invoke();
        first.Find("ShowSimpleSettingsButton").AsButton().Invoke();
        RunningApplication.Activate(first.Find("StorageAndSessionTab"));

        var before = first.Find("ShowAddTorrentOptionsSwitch").AsToggleButton();
        var flipped = before.IsToggled != true;
        before.IsToggled = flipped;

        // Settings reach the disk a moment after the switch moves, so the application has to be let
        // finish before it is stopped. Restarting the instant the switch is flipped loses the
        // change, which is worth knowing and is why this waits on the file rather than on a clock.
        var settings = Path.Combine(first.ProfileDirectory, "settings.json");
        RunningApplication.Until(
            () => File.Exists(settings)
                && File.ReadAllText(settings)
                    .Contains($"\"ShowAddTorrentOptions\":{(flipped ? "true" : "false")}", StringComparison.Ordinal),
            "the change to be written to settings.json");

        using var second = first.Restart();
        second.Find("ShowSimpleSettingsButton").AsButton().Invoke();
        RunningApplication.Activate(second.Find("StorageAndSessionTab"));

        Assert.Equal(
            flipped,
            second.Find("ShowAddTorrentOptionsSwitch").AsToggleButton().IsToggled);
    }
}
