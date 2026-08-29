using FlaUI.Core.AutomationElements;

namespace Peerfluence.UiTests;

/// <summary>
/// What a person sees the very first time they open Peerfluence.
/// </summary>
/// <remarks>
/// Only observable on a genuinely new profile, which is why nothing else covers it: the headless
/// tests construct a view model directly, and the developer machine has chosen a mode years ago.
/// </remarks>
public sealed class FirstRunTests
{
    [Fact]
    public void TheFirstRun_AsksWhichInterfaceToUse()
    {
        using var app = new RunningApplication();

        Assert.True(app.Exists("ChooseSimpleModeButton"), app.Describe());
        Assert.True(app.Exists("ChooseAdvancedModeButton"), app.Describe());
    }

    [Fact]
    public void ChoosingAnInterface_LeavesTheWelcomeBehindForGood()
    {
        using var app = new RunningApplication();

        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();

        RunningApplication.Until(
            () => !app.Exists("ChooseAdvancedModeButton"), "the welcome overlay to be dismissed");

        // The advanced interface, rather than merely the absence of the overlay.
        Assert.True(app.Exists("AddMagnetButton"), app.Describe());
    }

    [Fact]
    public void TheProfileItWasGiven_IsWhereItWrites()
    {
        // The reason these tests are safe to run on a machine that also uses the application. If
        // this ever fails, the tests are writing into somebody's real settings and should not run.
        using var app = new RunningApplication();

        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();

        RunningApplication.Until(
            () => File.Exists(Path.Combine(app.ProfileDirectory, "settings.json")),
            $"settings.json to appear in {app.ProfileDirectory}");
    }
}
