using FlaUI.Core.AutomationElements;

namespace Peerfluence.UiTests;

/// <summary>
/// Getting from one screen to another in the real application.
/// </summary>
/// <remarks>
/// A view that throws while loading - a binding to a property that no longer exists, a service the
/// container cannot supply - fails at the moment it is navigated to and nowhere earlier. Visiting
/// every destination is the cheapest way to find that out.
/// </remarks>
public sealed class NavigationTests
{
    /// <summary>
    /// One control that only exists on each destination.
    /// </summary>
    /// <remarks>
    /// Finding torrents is landmarked by its way into the settings rather than by its search box,
    /// because on a profile with no indexer configured - which every one of these is - the screen
    /// offers somewhere to configure one instead of somewhere to type a query.
    /// </remarks>
    private static readonly string[] Landmarks =
    [
        "AddMagnetButton",                      // downloads
        "FindTorrentsOpenSearchSettingsButton", // find torrents, with nothing set up yet
        "AppearanceTab",                        // settings
    ];

    [Fact]
    public void EveryNavigationDestination_Opens()
    {
        using var app = new RunningApplication();
        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => app.Exists("AddMagnetButton"), "the downloads screen");

        var reached = new HashSet<string>(StringComparer.Ordinal);

        // By position rather than by name: the destinations are titled in whichever of the ten
        // languages the machine running this happens to use.
        for (int index = 0; index < app.NavigationItems().Length; index++)
        {
            app.Activate(app.NavigationItems()[index]);
            RunningApplication.Until(
                () => Landmarks.Any(app.Exists), "a destination to finish opening");

            foreach (var landmark in Landmarks.Where(app.Exists))
            {
                reached.Add(landmark);
            }
        }

        Assert.True(
            reached.SetEquals(Landmarks),
            $"reached {string.Join(", ", reached)}, expected {string.Join(", ", Landmarks)}. "
                + $"What was on screen at the end:{Environment.NewLine}{app.Describe()}");
    }

    [Fact]
    public void TheTwoInterfaces_CanBeSwitchedBetweenBothWays()
    {
        using var app = new RunningApplication();

        app.Find("ChooseSimpleModeButton").AsButton().Invoke();
        RunningApplication.Until(() => app.Exists("SimpleDownloadsAddMagnetButton"), "the simple screen");

        app.Find("SwitchToAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => app.Exists("AddMagnetButton"), "the advanced screen");

        // And back, through the settings, which is the only way back.
        app.GoToSettings();
        var simple = app.Find("InterfaceModeSimpleRadioButton");

        // A real click, not SelectionItem.Select(). These radio buttons bind IsChecked one way and
        // do their work in a Command that runs on click, so selecting one through the accessibility
        // API moves the dot and changes nothing else. Assistive technology selects exactly that way.
        simple.Click();

        // The chrome simple mode brings with it, rather than the download list: the settings screen
        // was open when the switch happened, and simple mode keeps showing it.
        RunningApplication.Until(
            () => app.Exists("SwitchToAdvancedModeButton"), "the simple interface");
        Assert.False(app.Exists("AddMagnetButton"), "the advanced download list is still on screen");
    }
}
