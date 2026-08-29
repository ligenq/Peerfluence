using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Peerfluence.UiTests;

/// <summary>
/// The windows that open on top of the main one.
/// </summary>
/// <remarks>
/// The architecture tests read the markup and require that each of these sets a cancel button, so
/// that Escape closes it. Whether Escape actually closes it is a different claim, and this is the
/// only place it can be made: a modal is a separate top level window, which a headless test that
/// renders no windows cannot open.
/// </remarks>
public sealed class ModalWindowTests
{
    [Fact]
    public void TheCreateTorrentWindow_OpensOverTheApplication()
    {
        using var app = new RunningApplication();
        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => app.Exists("AddMagnetButton"), "the downloads screen");

        app.Find("CreateTorrentButton").AsButton().Invoke();

        RunningApplication.Until(() => app.ModalWindows.Length == 1, "the create torrent window");

        var modal = app.ModalWindows[0];
        Assert.NotNull(modal.FindFirstDescendant(by => by.ByAutomationId("SourcePathTextBox")));
        Assert.NotNull(modal.FindFirstDescendant(by => by.ByAutomationId("CreateButton")));
    }

    [Fact]
    public void Escape_ClosesTheCreateTorrentWindow()
    {
        using var app = new RunningApplication();
        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => app.Exists("AddMagnetButton"), "the downloads screen");

        app.Find("CreateTorrentButton").AsButton().Invoke();
        RunningApplication.Until(() => app.ModalWindows.Length == 1, "the create torrent window");

        app.ModalWindows[0].Focus();
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        RunningApplication.Until(() => app.ModalWindows.Length == 0, "the window to close on Escape");
    }

    [Fact]
    public void TheCreateTorrentWindow_PresentsItsAffirmativeActionFirst()
    {
        // The same Windows convention the prompts follow, on a window built in markup, measured
        // where a person actually sees it.
        using var app = new RunningApplication();
        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        RunningApplication.Until(() => app.Exists("AddMagnetButton"), "the downloads screen");

        app.Find("CreateTorrentButton").AsButton().Invoke();
        RunningApplication.Until(() => app.ModalWindows.Length == 1, "the create torrent window");

        var modal = app.ModalWindows[0];
        var create = modal.FindFirstDescendant(by => by.ByAutomationId("CreateButton"))!;
        var cancel = modal.FindFirstDescendant(by => by.ByAutomationId("CreateTorrentCancelButton"))!;

        Assert.True(
            create.BoundingRectangle.Left < cancel.BoundingRectangle.Left,
            $"create is at x={create.BoundingRectangle.Left}, cancel at x={cancel.BoundingRectangle.Left}");
    }
}
