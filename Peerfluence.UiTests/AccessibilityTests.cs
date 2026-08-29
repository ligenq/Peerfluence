using FlaUI.Core.AutomationElements;

namespace Peerfluence.UiTests;

/// <summary>
/// What a screen reader would say about the interface.
/// </summary>
/// <remarks>
/// <para>
/// The architecture tests already require an accessible name in the markup, but they read .axaml
/// and can only check that an attribute is present. Whether the platform ends up announcing it is a
/// different question, answerable only by asking the platform. It was worth asking: every button
/// whose content is an icon and a label rather than a plain string was announcing itself as
/// "Avalonia.Controls.StackPanel", because Avalonia falls back to the content's type name and the
/// markup looked perfectly correct.
/// </para>
/// </remarks>
public sealed class AccessibilityTests
{
    /// <summary>
    /// Window chrome supplied by Avalonia and SukiUI rather than by this application. Not ours to
    /// name, and not what these tests are about.
    /// </summary>
    private static readonly HashSet<string> NotOurs =
    [
        "TitleBar", "SystemMenuBar", "AvaloniaWindowChrome",
        "Minimize-Restore", "Maximize-Restore", "Close", "toggle",
    ];

    [Fact]
    public void EveryControl_AnnouncesSomethingAPersonWouldUnderstand()
    {
        using var app = new RunningApplication();
        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => app.Exists("AddMagnetButton"), "the downloads screen");

        var mute = new List<string>();

        foreach (var element in app.Window.FindAllDescendants())
        {
            var id = element.Properties.AutomationId.ValueOrDefault;
            if (string.IsNullOrEmpty(id) || NotOurs.Contains(id) || id.StartsWith("PART_", StringComparison.Ordinal))
            {
                continue;
            }

            var name = element.Properties.Name.ValueOrDefault;

            if (string.IsNullOrWhiteSpace(name))
            {
                mute.Add($"  {id} is announced as nothing at all");
            }
            else if (LooksLikeATypeName(name))
            {
                mute.Add($"  {id} is announced as '{name}', which is the name of a class");
            }
        }

        Assert.True(
            mute.Count == 0,
            $"{mute.Count} controls would be read out meaninglessly by a screen reader. A button whose "
                + $"content is markup rather than a string needs AutomationProperties.Name of its own:"
                + $"{Environment.NewLine}{string.Join(Environment.NewLine, mute)}");
    }

    private static bool LooksLikeATypeName(string name) =>
        name.StartsWith("Avalonia.", StringComparison.Ordinal)
            || name.StartsWith("Material.Icons.", StringComparison.Ordinal)
            || name.StartsWith("SukiUI.", StringComparison.Ordinal)
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Peerfluence.", StringComparison.Ordinal);

    [Fact]
    public void TheNavigation_CanBeReadAloud()
    {
        // The side menu items are generated from a template, so they carry no automation id and are
        // reached by position. Their names are all a screen reader has to go on.
        using var app = new RunningApplication();
        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => app.Exists("AddMagnetButton"), "the downloads screen");

        var destinations = app.NavigationItems();

        Assert.NotEmpty(destinations);
        Assert.All(destinations, item => Assert.False(
            string.IsNullOrWhiteSpace(item.Properties.Name.ValueOrDefault),
            "a navigation destination has no name, so there is no way to hear where it goes"));
    }
}
