using Avalonia.Controls;
using Avalonia.LogicalTree;
using Peerfluence.Core.Services;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using SukiUI.Controls;
using SukiUI.Dialogs;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The prompts the application builds in code, shown for real.
/// </summary>
/// <remarks>
/// <para>
/// The two dialogs written in markup have their button order checked by reading the markup, which
/// cannot see these: a magnet prompt, a rename, a removal and the update offer are all assembled
/// through <see cref="DialogService"/> and never appear in a .axaml file. This shows one and looks
/// at what came out.
/// </para>
/// <para>
/// It also settles a thing worth not assuming - that SukiUI lays its action buttons out in the
/// order they were added, so putting the affirmative one first is what puts it on the left.
/// </para>
/// </remarks>
public class DialogServiceTests
{
    [AvaloniaFact]
    public void ThePromptsBuiltInCode_PutTheAffirmativeActionFirst()
    {
        var buttons = ShowConfirmAndReadButtons(
            new ConfirmPrompt("Title", "Message", "Remove", "Cancel"));

        var confirm = buttons.FindIndex(text => text == "Remove");
        var cancel = buttons.FindIndex(text => text == "Cancel");

        Assert.True(confirm >= 0, $"no confirming button was rendered; got: {string.Join(", ", buttons)}");
        Assert.True(cancel >= 0, $"no cancelling button was rendered; got: {string.Join(", ", buttons)}");
        Assert.True(
            confirm < cancel,
            $"the affirmative action must come before Cancel; got: {string.Join(", ", buttons)}");
    }

    [AvaloniaFact]
    public void ATextPrompt_ShowsAnEditableBoxCarryingWhatItWasGiven()
    {
        var dialog = Show(service => service.PromptForTextAsync(
            new TextPrompt("Rename", "Rename", InitialText: "folder/file.bin")));

        var box = (dialog.Content as Control)?.GetSelfAndLogicalDescendants().OfType<TextBox>().FirstOrDefault();

        Assert.NotNull(box);
        Assert.Equal("folder/file.bin", box!.Text);
    }

    [AvaloniaFact]
    public void ARemovePrompt_OffersEveryChoiceItWasGiven()
    {
        var dialog = Show(service => service.PromptForRemoveOptionsAsync(new RemoveTorrentPrompt(
            "Remove",
            "Remove this torrent?",
            "Remove",
            "Cancel",
            RemoveTorrentAction.DeleteFiles,
            new Dictionary<RemoveTorrentAction, string>
            {
                [RemoveTorrentAction.RemoveOnly] = "Remove only",
                [RemoveTorrentAction.DeleteFiles] = "Delete files",
            },
            "Remember")));

        var options = (dialog.Content as Control)!.GetSelfAndLogicalDescendants().OfType<RadioButton>().ToList();

        Assert.Equal(2, options.Count);
        Assert.Equal(["Remove only", "Delete files"], options.Select(option => option.Content as string));

        // The option the caller said was current is the one already selected, so accepting without
        // touching anything does what the settings say.
        Assert.True(options.Single(option => (option.Content as string) == "Delete files").IsChecked);
    }

    private static List<string> ShowConfirmAndReadButtons(ConfirmPrompt prompt)
    {
        return Show(service => service.ConfirmAsync(prompt))
            .ActionButtons
            .OfType<Button>()
            .Select(button => button.Content as string)
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList()!;
    }

    /// <summary>
    /// Shows a prompt and returns the dialog that reached the manager.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dialog itself rather than the rendered host: what is being checked is what the service
    /// built, and a host has to be measured and laid out before its contents appear in the tree.
    /// </para>
    /// <para>
    /// The prompt's task is deliberately not awaited. It completes when somebody presses a button,
    /// and nobody is going to.
    /// </para>
    /// </remarks>
    private static ISukiDialog Show(Func<IDialogService, Task> prompt)
    {
        var manager = new SukiDialogManager();
        var service = new DialogService(Substitute.For<ITopLevelService>(), []);
        ((IDialogHost)service).DialogManager = manager;

        ISukiDialog? shown = null;
        manager.OnDialogShown += (_, args) => shown = args.Dialog;

        _ = prompt(service);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(shown is not null, "the service did not show a dialog");
        return shown!;
    }
}
