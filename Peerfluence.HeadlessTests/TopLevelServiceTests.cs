using Avalonia.Controls;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The window everything else reaches the desktop through.
/// </summary>
/// <remarks>
/// One field, but it is the field the clipboard, the folder pickers, the modal dialogs and the MCP
/// screenshot tool all go through. What matters is that it is honest about being empty: the window
/// is set when it opens and cleared when it closes, and anything asking in between has to be told
/// there is nothing there rather than handed a window that has gone.
/// </remarks>
public class TopLevelServiceTests
{
    [AvaloniaFact]
    public void BeforeAWindowExists_NothingIsAvailable()
    {
        var sut = new TopLevelService();

        Assert.False(sut.IsWindowAvailable);
    }

    [AvaloniaFact]
    public void OnceTheWindowIsSet_ItIsAvailable()
    {
        var sut = new TopLevelService();
        var window = new Window();

        sut.SetTopLevel(window);

        Assert.True(sut.IsWindowAvailable);
    }

    [AvaloniaFact]
    public void ClearingTheWindow_MakesItUnavailableAgain()
    {
        // What happens on shutdown. The screenshot tool checks this before capturing, so a stale
        // window here is the difference between a refusal and an exception across the MCP pipe.
        var sut = new TopLevelService();
        sut.SetTopLevel(new Window());

        sut.SetTopLevel(null);

        Assert.False(sut.IsWindowAvailable);
    }

    [AvaloniaFact]
    public void AskingForTheClipboardWithNoWindow_FailsLoudlyRatherThanReturningNothing()
    {
        // The copy commands would otherwise appear to work and silently do nothing.
        var sut = new TopLevelService();

        Assert.Throws<InvalidOperationException>(() => sut.GetClipboard());
    }

    [AvaloniaFact]
    public void AskingForStorageWithNoWindow_FailsLoudlyRatherThanReturningNothing()
    {
        var sut = new TopLevelService();

        Assert.Throws<InvalidOperationException>(() => sut.GetStorageProvider());
    }

    [AvaloniaFact]
    public async Task CapturingTheWindowWithNoWindow_AnswersNothing()
    {
        // Answers null rather than throwing, because the MCP tool turns this into a refusal the
        // agent can read.
        var sut = new TopLevelService();

        Assert.Null(await sut.CaptureWindowPngAsync(TestContext.Current.CancellationToken));
    }
}
