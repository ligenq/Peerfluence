using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Peerfluence.Theme.Controls;

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
    private readonly IDialogManager _dialogManager;

    public DialogService(
        ITopLevelService topLevelService,
        IEnumerable<DialogRegistration> registrations,
        IDialogManager dialogManager)
    {
        _topLevelService = topLevelService;
        _registrations = registrations.ToDictionary(r => r.ViewModelType);
        _dialogManager = dialogManager;
    }

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
            MinWidth = 320,
            Text = prompt.InitialText ?? string.Empty,
            PlaceholderText = prompt.Watermark,
        };

        // The markup carries an automation id on everything a person can act on, checked by an
        // architecture test that reads .axaml files - which cannot see a control built here. These
        // prompts were already missed twice that way, once for button order and once for the
        // keyboard, so the ids are set explicitly rather than left to a rule that cannot reach them.
        textBox.SetValue(AutomationProperties.AutomationIdProperty, PromptTextBoxId);

        // Read aloud when the box takes focus. The placeholder is an example of what to type rather
        // than a name for the field, and is empty for the magnet prompt, so the title is the name.
        textBox.SetValue(AutomationProperties.NameProperty, prompt.Title);

        var validationError = new InfoBar
        {
            IsVisible = false,
            IsClosable = false,
            Severity = ToastSeverity.Error,
            MaxWidth = 420,
        };
        validationError.SetValue(AutomationProperties.AutomationIdProperty, PromptValidationErrorId);
        validationError.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Assertive);

        void ShowError(string? message)
        {
            validationError.Message = message;
            validationError.IsVisible = !string.IsNullOrEmpty(message);
            validationError.SetValue(AutomationProperties.NameProperty, message);
            textBox.SetValue(AutomationProperties.HelpTextProperty, message);
        }

        textBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty)
            {
                ShowError(null);
            }
        };

        bool Validate()
        {
            string? error = prompt.Validate?.Invoke(textBox.Text?.Trim() ?? string.Empty);
            ShowError(error);
            if (string.IsNullOrEmpty(error))
            {
                return true;
            }

            // Keep the entered text and return focus to the field that needs correcting.
            textBox.Focus();
            return false;
        }

        var content = new StackPanel { Spacing = 8, Children = { textBox, validationError } };
        bool accepted = await ShowAsync(
            prompt.Title,
            content,
            PromptSeverity.Information,
            prompt.ConfirmLabel,
            Properties.Resources.Common_Cancel,
            confirm => EnableOnlyWhenFilledIn(confirm, textBox),
            validateConfirm: Validate);

        return accepted ? textBox.Text?.Trim() : null;
    }

    public Task<bool> ConfirmAsync(ConfirmPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        Dispatcher.UIThread.VerifyAccess();

        return ShowAsync(
            prompt.Title,
            Wrapped(prompt.Message),
            prompt.Severity,
            prompt.ConfirmLabel,
            prompt.CancelLabel);
    }

    public async Task<RemoveTorrentChoice?> PromptForRemoveOptionsAsync(RemoveTorrentPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        Dispatcher.UIThread.VerifyAccess();

        var options = new StackPanel { Spacing = 4 };
        var buttons = new Dictionary<RemoveTorrentAction, RadioButton>();

        foreach (var (action, label) in prompt.OptionLabels)
        {
            var button = new RadioButton
            {
                Content = label,
                GroupName = "RemoveTorrentAction",
                IsChecked = action == prompt.DefaultAction,
            };

            buttons[action] = button;
            options.Children.Add(button);
        }

        var rememberChoice = new CheckBox { Content = prompt.RememberChoiceLabel };

        var content = new StackPanel
        {
            Spacing = 12,
            Children = { Wrapped(prompt.Message), options, rememberChoice },
        };

        bool accepted = await ShowAsync(
            prompt.Title,
            content,
            PromptSeverity.Warning,
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
    /// Dismissing without answering - Escape, or a click on the background - counts as declining.
    /// That arrives as the manager's content being replaced or cleared by something other than this
    /// method, which is what the subscription below watches for. Without it, a dismissed dialog
    /// would leave its caller awaiting an answer that can never come.
    /// </remarks>
    private async Task<bool> ShowAsync(
        string title,
        Control content,
        PromptSeverity severity,
        string confirmLabel,
        string cancelLabel,
        Action<Button>? configureConfirm = null,
        Func<bool>? validateConfirm = null)
    {
        Dispatcher.UIThread.VerifyAccess();

        var answered = new TaskCompletionSource<bool>();

        // The affirmative action first, so it sits on the left. Same order as the two dialogs
        // written in markup, and the same reason: Windows puts the "do it" button leftmost and the
        // safe one rightmost. Every prompt in the application is built here, so this is the only
        // place that decides it.
        //
        // IsDefault and IsCancel are what make Enter and Escape work. These prompts are an overlay
        // inside the main window rather than a window of their own, so their buttons share that
        // window's key handling, which is why they need not become windows to behave like dialogs.
        var confirm = new Button { Content = confirmLabel, IsDefault = true };
        confirm.Classes.Add("accent");
        confirm.SetValue(AutomationProperties.AutomationIdProperty, PromptConfirmButtonId);
        confirm.Click += (_, _) =>
        {
            if (validateConfirm is null || validateConfirm())
            {
                answered.TrySetResult(true);
            }
        };
        configureConfirm?.Invoke(confirm);

        var cancel = new Button { Content = cancelLabel, IsCancel = true };
        cancel.SetValue(AutomationProperties.AutomationIdProperty, PromptCancelButtonId);
        cancel.Click += (_, _) => answered.TrySetResult(false);

        // Severity shows in the heading's colour and nowhere else. A warning dialog is already
        // the whole screen's attention; an icon and a tinted panel on top of that is decoration.
        var heading = new TextBlock { Text = title };
        heading.Classes.Add("subtitle");
        if (severity == PromptSeverity.Warning)
        {
            heading.Classes.Add("toastWarning");
        }

        var dialog = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                heading,
                content,
                new StackPanel
                {
                    Classes = { "dialogActions" },
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { confirm, cancel },
                },
            },
        };

        void OnManagerChanged(object? sender, PropertyChangedEventArgs change)
        {
            if (!ReferenceEquals(_dialogManager.Content, dialog))
            {
                answered.TrySetResult(false);
            }
        }

        _dialogManager.PropertyChanged += OnManagerChanged;
        _dialogManager.Show(dialog);

        try
        {
            return await answered.Task;
        }
        finally
        {
            _dialogManager.PropertyChanged -= OnManagerChanged;

            // Only if this dialog is still the one showing: a dismissal has already replaced it,
            // and closing again would shut whatever opened next.
            if (ReferenceEquals(_dialogManager.Content, dialog))
            {
                _dialogManager.Close();
            }
        }
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

    /// <summary>The inline validation message in a text prompt.</summary>
    public const string PromptValidationErrorId = "PromptValidationError";

    /// <summary>The automation id of the accepting button on any prompt built here.</summary>
    public const string PromptConfirmButtonId = "PromptConfirmButton";

    /// <summary>The automation id of the dismissing button on any prompt built here.</summary>
    public const string PromptCancelButtonId = "PromptCancelButton";

    private static TextBlock Wrapped(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
}
