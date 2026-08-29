using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using Peerfluence.Services;

namespace Peerfluence.UiTests;

/// <summary>
/// The magnet prompt, driven the way a person drives it.
/// </summary>
/// <remarks>
/// <para>
/// This is the one thing here that no other suite can reach. The prompt is built in C# rather than
/// markup, so the tests that read .axaml never see it; the headless tests see the controls but not a
/// rendered window; and the MCP agent tools add torrents by calling the service, so they never press
/// the button at all. An enabled button that quietly did nothing survived all three.
/// </para>
/// </remarks>
public sealed class MagnetPromptTests
{
    [Fact]
    public void TheMagnetPrompt_WillNotAcceptAnEmptyMagnet()
    {
        ClipboardMustNotHoldAMagnet();

        using var app = new RunningApplication();
        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();

        app.Find("AddMagnetButton").AsButton().Invoke();

        var box = app.Find(DialogService.PromptTextBoxId);
        var confirm = app.Find(DialogService.PromptConfirmButtonId);
        var cancel = app.Find(DialogService.PromptCancelButtonId);

        Assert.False(confirm.IsEnabled, "the prompt opened empty with an enabled accepting button");
        Assert.True(cancel.IsEnabled, "the dismissing button must always be available");

        box.AsTextBox().Text = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

        RunningApplication.Until(() => confirm.IsEnabled, "the accepting button to become available");
    }

    [Fact]
    public void TheMagnetPrompt_PutsTheAffirmativeActionOnTheLeft()
    {
        // Read off the rendered window rather than the markup, because there is no markup: this is
        // the only place the Windows button order can be observed as a person sees it.
        ClipboardMustNotHoldAMagnet();

        using var app = new RunningApplication();
        app.Find("ChooseAdvancedModeButton").AsButton().Invoke();
        app.Find("AddMagnetButton").AsButton().Invoke();

        var confirm = app.Find(DialogService.PromptConfirmButtonId);
        var cancel = app.Find(DialogService.PromptCancelButtonId);

        Assert.True(
            confirm.BoundingRectangle.Left < cancel.BoundingRectangle.Left,
            $"the accepting button is at x={confirm.BoundingRectangle.Left} and the dismissing one at "
                + $"x={cancel.BoundingRectangle.Left}; Windows puts the affirmative action first");
    }

    /// <summary>
    /// Makes sure the prompt will actually open.
    /// </summary>
    /// <remarks>
    /// Adding a magnet reads the clipboard first and skips the prompt entirely when what it finds is
    /// already a valid magnet link, which is a real convenience and an unhelpful precondition here.
    /// The clipboard is overwritten rather than read, because these tests take over the desktop for
    /// their duration anyway, and a test that passes or fails depending on what was last copied is
    /// worse than one that is honest about clearing it.
    /// </remarks>
    private static void ClipboardMustNotHoldAMagnet()
    {
        using var powershell = Process.Start(new ProcessStartInfo("powershell")
        {
            ArgumentList = { "-NoProfile", "-Command", "Set-Clipboard -Value 'peerfluence-ui-tests'" },
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        powershell?.WaitForExit(10_000);
    }
}
