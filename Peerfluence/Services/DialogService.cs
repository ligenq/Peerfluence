using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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
public sealed class DialogService : IDialogService, IDialogHost
{
    private readonly ITopLevelService _topLevelService;
    private readonly IReadOnlyDictionary<Type, DialogRegistration> _registrations;

    public DialogService(ITopLevelService topLevelService, IEnumerable<DialogRegistration> registrations)
    {
        _topLevelService = topLevelService;
        _registrations = registrations.ToDictionary(r => r.ViewModelType);
    }

    /// <summary>
    /// Where the dialogs appear. Set once by <see cref="ViewModels.MainWindowViewModel"/>, which
    /// creates the manager the window's dialog host is bound to.
    /// </summary>
    /// <remarks>
    /// A property rather than a constructor argument because the manager cannot exist until the UI
    /// thread does, and this service is built with the rest of the container. Null until then, and
    /// every prompt below answers "dismissed" while it is - which is what a prompt nobody can see
    /// amounts to.
    /// </remarks>
    public ISukiDialogManager? DialogManager { get; set; }

    public bool CanPrompt => DialogManager is not null;

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

        if (DialogManager is null)
        {
            return null;
        }

        var textBox = new TextBox
        {
            Width = 420,
            MinWidth = 320,
            Text = prompt.InitialText ?? string.Empty,
            PlaceholderText = prompt.Watermark
        };

        var accepted = await ShowAsync(
            builder => builder.WithTitle(prompt.Title).WithContent(textBox),
            prompt.ConfirmLabel,
            Properties.Resources.Common_Cancel);

        return accepted ? textBox.Text?.Trim() : null;
    }

    public Task<bool> ConfirmAsync(ConfirmPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (DialogManager is null)
        {
            return Task.FromResult(false);
        }

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

        if (DialogManager is null)
        {
            return null;
        }

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
        string cancelLabel)
    {
        var answered = new TaskCompletionSource<bool>();

        await describe(DialogManager!.CreateDialog())
            .Dismiss().ByClickingBackground()
            .OnDismissed(_ => answered.TrySetResult(false))
            .WithActionButton(cancelLabel, _ => answered.TrySetResult(false), true)
            .WithActionButton(confirmLabel, _ => answered.TrySetResult(true), true, "Flat")
            .TryShowAsync();

        return answered.Task.IsCompletedSuccessfully && answered.Task.Result;
    }

    private static TextBlock Wrapped(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };

    private static Avalonia.Controls.Notifications.NotificationType ToNotificationType(PromptSeverity severity) => severity switch
    {
        PromptSeverity.Information => Avalonia.Controls.Notifications.NotificationType.Information,
        _ => Avalonia.Controls.Notifications.NotificationType.Warning
    };
}
