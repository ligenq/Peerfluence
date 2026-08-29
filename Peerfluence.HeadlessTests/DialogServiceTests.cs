using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
    public void ATextPrompt_CannotBeAcceptedWhileItIsEmpty()
    {
        // Adding a blank magnet and renaming a file to nothing are both refused further in. A button
        // that looks available and then does nothing is a worse way to say so.
        var dialog = Show(service => service.PromptForTextAsync(new TextPrompt("Add magnet", "Add magnet")));

        Assert.False(Confirm(dialog).IsEnabled);
    }

    [AvaloniaFact]
    public void ATextPrompt_CanBeAcceptedOnceSomethingIsTyped()
    {
        var dialog = Show(service => service.PromptForTextAsync(new TextPrompt("Add magnet", "Add magnet")));
        var box = Box(dialog);

        box.Text = "magnet:?xt=urn:btih:abc";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(Confirm(dialog).IsEnabled);

        // And off again, because a prompt can be emptied as easily as it is filled.
        box.Text = "   ";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(Confirm(dialog).IsEnabled);
    }

    [AvaloniaFact]
    public void ATextPrompt_OpensReadyWhenItWasGivenSomethingToStartWith()
    {
        // The rename prompt opens on the file's current path, and the magnet prompt on whatever was
        // on the clipboard.
        var dialog = Show(service => service.PromptForTextAsync(
            new TextPrompt("Rename", "Rename", InitialText: "folder/file.bin")));

        Assert.True(Confirm(dialog).IsEnabled);
    }

    [AvaloniaFact]
    public async Task Enter_DoesNotAcceptAnEmptyTextPrompt()
    {
        // The disabling is only worth anything if it also closes the keyboard route in. A default
        // button that is disabled is not a default button, but that is worth showing rather than
        // assuming, because it is the difference between a greyed-out button and a real refusal.
        var manager = new SukiDialogManager();
        var service = new DialogService(Substitute.For<ITopLevelService>(), []);
        ((IDialogHost)service).DialogManager = manager;

        var window = new Window { Content = new SukiDialogHost { Manager = manager }, Width = 600, Height = 400 };
        window.Show();

        var answered = service.PromptForTextAsync(new TextPrompt("Add magnet", "Add magnet"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(answered.IsCompleted, "Enter accepted a prompt with nothing in it");

        // Escape still works, so the dialog is not a trap.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(answered.IsCompleted, "Escape must still dismiss an empty prompt");
        Assert.Null(await answered);
    }

    [AvaloniaFact]
    public void APromptBuiltInCode_CanBeFoundByAutomationId()
    {
        // The architecture test that requires an automation id reads .axaml, so it is blind to a
        // control built in C#. These prompts have twice been missed by exactly that blindness. The
        // end-to-end tests drive them through the accessibility tree, so the ids are load-bearing.
        var dialog = Show(service => service.PromptForTextAsync(
            new TextPrompt("Add magnet", "Add magnet")));

        var box = Box(dialog);
        var buttons = dialog.ActionButtons.OfType<Button>().ToList();

        Assert.Equal(DialogService.PromptTextBoxId, box.GetValue(AutomationProperties.AutomationIdProperty));
        Assert.Equal("Add magnet", box.GetValue(AutomationProperties.NameProperty));
        Assert.Equal(DialogService.PromptConfirmButtonId, buttons[0].GetValue(AutomationProperties.AutomationIdProperty));
        Assert.Equal(DialogService.PromptCancelButtonId, buttons[1].GetValue(AutomationProperties.AutomationIdProperty));
    }

    private static Button Confirm(ISukiDialog dialog) => dialog.ActionButtons.OfType<Button>().First();

    private static TextBox Box(ISukiDialog dialog) =>
        (dialog.Content as Control)!.GetSelfAndLogicalDescendants().OfType<TextBox>().First();

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

    [AvaloniaFact]
    public async Task Enter_AcceptsAPromptBuiltInCode()
    {
        // Until these two tests existed the prompts answered neither key: a magnet could be pasted
        // and only added with the mouse. They work because a Suki dialog is an overlay inside the
        // main window, so its buttons share that window's key handling - which is why these do not
        // have to become windows of their own to behave like dialogs.
        var (window, answered) = ShowConfirm();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(await Answer(answered), "Enter must accept the dialog");
    }

    [AvaloniaFact]
    public async Task Escape_DismissesAPromptBuiltInCode()
    {
        var (window, answered) = ShowConfirm();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(await Answer(answered), "Escape must dismiss the dialog without accepting it");
    }

    private static async Task<bool> Answer(Task<bool> answered)
    {
        Assert.True(answered.IsCompleted, "the key press left the dialog waiting");
        return await answered;
    }

    /// <summary>
    /// A confirmation shown in a real window, so key presses have somewhere to land.
    /// </summary>
    private static (Window Window, Task<bool> Answered) ShowConfirm()
    {
        var manager = new SukiDialogManager();
        var service = new DialogService(Substitute.For<ITopLevelService>(), []);
        ((IDialogHost)service).DialogManager = manager;

        var window = new Window { Content = new SukiDialogHost { Manager = manager }, Width = 600, Height = 400 };
        window.Show();

        var answered = service.ConfirmAsync(new ConfirmPrompt("Title", "Message", "Remove", "Cancel"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (window, answered);
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
