using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using SukiUI.Dialogs;

namespace Peerfluence.Services;

/// <summary>
/// Every dialog the application puts in front of someone.
/// </summary>
/// <remarks>
/// <para>
/// The prompts used to be built where they were needed, which meant view models constructing
/// <see cref="TextBox"/> and <see cref="RadioButton"/> instances by hand - a view model deciding
/// what the screen is made of, which is the one thing the pattern exists to prevent. It also made
/// them untestable: there is no way to answer a dialog that is created inside the method under test.
/// </para>
/// <para>
/// The contract lives in <c>Peerfluence.Core</c> and names no Avalonia type, so a caller asks for a
/// line of text or a confirmation without knowing what a dialog looks like. The controls are here.
/// </para>
/// </remarks>
public sealed class DialogService : IDialogService
{
    private readonly ITopLevelService _topLevelService;
    private readonly IReadOnlyDictionary<Type, DialogRegistration> _registrations;

    public DialogService(
        ITopLevelService topLevelService,
        IEnumerable<DialogRegistration> registrations,
        ISukiDialogManager dialogManager)
    {
        _topLevelService = topLevelService;
        _registrations = registrations.ToDictionary(r => r.ViewModelType);
        DialogManager = dialogManager;
    }

    /// <summary>Where the dialogs appear.</summary>
    private ISukiDialogManager DialogManager { get; }

    /// <summary>
    /// Whether a prompt would be seen by anybody.
    /// </summary>
    /// <remarks>
    /// Always, now that the manager arrives through the constructor rather than being set afterwards
    /// by whoever happened to create it. Kept on <see cref="IDialogService"/> because the callers
    /// that ask still need an answer, and an implementation that genuinely cannot prompt - a headless
    /// one, an agent-driven one - would say no.
    /// </remarks>
    public bool CanPrompt => true;

    public async Task ShowAsync<TViewModel>() where TViewModel : class
    {
        var viewModelType = typeof(TViewModel);
        if (!_registrations.TryGetValue(viewModelType, out var registration))
        {
            throw new InvalidOperationException($"No dialog registered for view model type '{viewModelType.FullName}'.");
        }

        var window = registration.WindowFactory();
        window.DataContext = registration.ViewModelFactory();

        await _topLevelService.ShowDialogAsync(window);
    }

    public async Task<string?> PromptForTextAsync(TextPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        // Builds controls and shows them. Declared rather than assumed: every caller is a
        // command handler today, and the day one is not, this says so immediately.
        Dispatcher.UIThread.VerifyAccess();

        var textBox = new TextBox
        {
            Width = 420,
            MinWidth = 320,
            Text = prompt.InitialText ?? string.Empty,
            PlaceholderText = prompt.Watermark
        };

        // The markup carries an automation id on everything a person can act on, checked by an
        // architecture test that reads .axaml files - which cannot see a control built here. These
        // prompts were already missed twice that way, once for button order and once for the
        // keyboard, so the ids are set explicitly rather than left to a rule that cannot reach them.
        textBox.SetValue(AutomationProperties.AutomationIdProperty, PromptTextBoxId);

        // Read aloud when the box takes focus. The placeholder is an example of what to type rather
        // than a name for the field, and is empty for the magnet prompt, so the title is the name.
        textBox.SetValue(AutomationProperties.NameProperty, prompt.Title);

        var accepted = await ShowAsync(
            builder => builder.WithTitle(prompt.Title).WithContent(textBox),
            prompt.ConfirmLabel,
            Properties.Resources.Common_Cancel,
            confirm => EnableOnlyWhenFilledIn(confirm, textBox));

        return accepted ? textBox.Text?.Trim() : null;
    }

    public Task<bool> ConfirmAsync(ConfirmPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        // Builds controls and shows them. Declared rather than assumed: every caller is a
        // command handler today, and the day one is not, this says so immediately.
        Dispatcher.UIThread.VerifyAccess();

        return ShowAsync(
            builder => builder
                .OfType(ToNotificationType(prompt.Severity))
                .WithTitle(prompt.Title)
                .WithContent(Wrapped(prompt.Message)),
            prompt.ConfirmLabel,
            prompt.CancelLabel);
    }

    public async Task<RemoveTorrentChoice?> PromptForRemoveOptionsAsync(RemoveTorrentPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        // Builds controls and shows them. Declared rather than assumed: every caller is a
        // command handler today, and the day one is not, this says so immediately.
        Dispatcher.UIThread.VerifyAccess();

        var options = new StackPanel();
        var buttons = new Dictionary<RemoveTorrentAction, RadioButton>();

        foreach (var (action, label) in prompt.OptionLabels)
        {
            var button = new RadioButton
            {
                Content = label,
                GroupName = "RemoveTorrentAction",
                IsChecked = action == prompt.DefaultAction
            };

            buttons[action] = button;
            options.Children.Add(button);
        }

        var rememberChoice = new CheckBox
        {
            Content = prompt.RememberChoiceLabel,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        };

        var content = new StackPanel
        {
            Children =
            {
                Wrapped(prompt.Message),
                new StackPanel { Margin = new Avalonia.Thickness(0, 10, 0, 0), Children = { options } },
                rememberChoice
            }
        };

        var accepted = await ShowAsync(
            builder => builder
                .OfType(Avalonia.Controls.Notifications.NotificationType.Warning)
                .WithTitle(prompt.Title)
                .WithContent(content),
            prompt.ConfirmLabel,
            prompt.CancelLabel);

        if (!accepted)
        {
            return null;
        }

        var chosen = buttons.FirstOrDefault(pair => pair.Value.IsChecked == true).Key;
        return new RemoveTorrentChoice(chosen, rememberChoice.IsChecked == true);
    }

    /// <summary>
    /// Shows a dialog with a confirming and a declining button, and answers which was pressed.
    /// </summary>
    /// <remarks>
    /// Dismissing by clicking the background counts as declining, which is why the completion source
    /// is set from <c>OnDismissed</c> as well as from the buttons.
    /// </remarks>
    private async Task<bool> ShowAsync(
        Func<SukiDialogBuilder, SukiDialogBuilder> describe,
        string confirmLabel,
        string cancelLabel,
        Action<Button>? configureConfirm = null)
    {
        Dispatcher.UIThread.VerifyAccess();

        var answered = new TaskCompletionSource<bool>();

        var builder = describe(DialogManager.CreateDialog())
            .Dismiss().ByClickingBackground()
            .OnDismissed(_ => answered.TrySetResult(false))
            // The affirmative action first, so it sits on the left. Same order as the two dialogs
            // written in markup, and the same reason: Windows puts the "do it" button leftmost and
            // the safe one rightmost. Every prompt in the application is built here, so this is the
            // only place that decides it.
            .WithActionButton(confirmLabel, _ => answered.TrySetResult(true), true, "Flat")
            .WithActionButton(cancelLabel, _ => answered.TrySetResult(false), true);

        var buttons = builder.Dialog.ActionButtons.OfType<Button>().ToList();
        GiveTheKeyboardItsTwoAnswers(buttons);
        NameTheButtonsForAutomation(buttons);

        if (buttons.Count > 0)
        {
            configureConfirm?.Invoke(buttons[0]);
        }

        await builder.TryShowAsync();

        return answered.Task.IsCompletedSuccessfully && answered.Task.Result;
    }

    /// <summary>
    /// Makes Enter accept the dialog and Escape dismiss it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the two keys every dialog is expected to answer, and until this was added the
    /// prompts built here answered neither - they could only be dismissed with the mouse. The
    /// dialogs written in markup get it from <c>IsDefault</c> and <c>IsCancel</c> in the markup;
    /// this is the same two properties, set on the buttons Suki built.
    /// </para>
    /// <para>
    /// It works because a Suki dialog is an overlay inside the main window rather than a window of
    /// its own, so its buttons are in that window's top level and are reached by the same key
    /// handling. That is worth stating because it is not obvious, and because it is the reason
    /// these prompts do not need to become windows to behave like dialogs.
    /// </para>
    /// </remarks>
    private static void GiveTheKeyboardItsTwoAnswers(List<Button> buttons)
    {
        if (buttons.Count != 2)
        {
            return;
        }

        // Confirm is added first so that it renders leftmost, which makes it the default.
        buttons[0].IsDefault = true;
        buttons[1].IsCancel = true;
    }

    /// <summary>
    /// Keeps the accepting button switched off until there is something to accept.
    /// </summary>
    /// <remarks>
    /// A prompt asking for a value has nothing to do with an empty one: adding a blank magnet and
    /// renaming a file to nothing are both refused further in, and a button that looks available
    /// and then does nothing is a worse way to say so. It also stops Enter submitting an empty
    /// prompt, because a disabled default button is not a default button.
    /// </remarks>
    private static void EnableOnlyWhenFilledIn(Button confirm, TextBox textBox)
    {
        void Sync() => confirm.IsEnabled = !string.IsNullOrWhiteSpace(textBox.Text);

        textBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty)
            {
                Sync();
            }
        };

        // The magnet prompt opens with whatever was on the clipboard, which may be nothing.
        Sync();
    }

    /// <summary>The automation id of the text box in a prompt that asks for a value.</summary>
    public const string PromptTextBoxId = "PromptTextBox";

    /// <summary>The automation id of the accepting button on any prompt built here.</summary>
    public const string PromptConfirmButtonId = "PromptConfirmButton";

    /// <summary>The automation id of the dismissing button on any prompt built here.</summary>
    public const string PromptCancelButtonId = "PromptCancelButton";

    /// <summary>
    /// Gives the two action buttons ids a test or a screen reader can find them by.
    /// </summary>
    /// <remarks>
    /// By position rather than by label, because the label is translated into ten languages and the
    /// position is not: the affirmative button is added first everywhere, which is the same fact the
    /// button order and the keyboard defaults rest on.
    /// </remarks>
    private static void NameTheButtonsForAutomation(List<Button> buttons)
    {
        if (buttons.Count != 2)
        {
            return;
        }

        buttons[0].SetValue(AutomationProperties.AutomationIdProperty, PromptConfirmButtonId);
        buttons[1].SetValue(AutomationProperties.AutomationIdProperty, PromptCancelButtonId);
    }

    private static TextBlock Wrapped(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };

    private static Avalonia.Controls.Notifications.NotificationType ToNotificationType(PromptSeverity severity) => severity switch
    {
        PromptSeverity.Information => Avalonia.Controls.Notifications.NotificationType.Information,
        _ => Avalonia.Controls.Notifications.NotificationType.Warning
    };
}
