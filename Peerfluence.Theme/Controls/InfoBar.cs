using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Peerfluence.Theme.Controls;

/// <summary>
/// A single line of status, shown in place rather than over the top of anything.
/// </summary>
/// <remarks>
/// For a message that belongs to a screen and should stay until it stops being true - a failed
/// search, a session that could not be saved. A <see cref="Toast"/> is the other half of that
/// pair: it passes, and it belongs to the application rather than to the screen.
/// </remarks>
public class InfoBar : TemplatedControl
{
    private Button? _closeButton;

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<InfoBar, string?>(nameof(Message));

    public static readonly StyledProperty<bool> IsClosableProperty =
        AvaloniaProperty.Register<InfoBar, bool>(nameof(IsClosable), defaultValue: true);

    public static readonly StyledProperty<string> DismissLabelProperty =
        AvaloniaProperty.Register<InfoBar, string>(nameof(DismissLabel), "Close");

    public static readonly StyledProperty<ToastSeverity> SeverityProperty =
        AvaloniaProperty.Register<InfoBar, ToastSeverity>(nameof(Severity));

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>How much attention the message asks for, which sets the text colour.</summary>
    public ToastSeverity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>Whether the reader can dismiss it.</summary>
    public bool IsClosable
    {
        get => GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>Localized accessible name and tooltip for the close action.</summary>
    public string DismissLabel
    {
        get => GetValue(DismissLabelProperty);
        set => SetValue(DismissLabelProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_closeButton is not null)
        {
            _closeButton.Click -= OnClose;
        }

        base.OnApplyTemplate(e);
        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        if (_closeButton is not null)
        {
            _closeButton.Click += OnClose;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (IsClosable)
        {
            // Preserve any visibility binding so a later status can show the bar again.
            SetCurrentValue(IsVisibleProperty, false);
        }
    }
}
