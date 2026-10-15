using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using System.Collections.Specialized;

namespace Peerfluence.Theme.Controls;

/// <summary>
/// Shows the dialog a <see cref="DialogManager"/> is holding, over whatever is behind it.
/// </summary>
/// <remarks>
/// Place one high in the window's visual tree, above the content it should cover. It occupies no
/// space and hit-tests nothing while closed.
/// </remarks>
public class DialogHost : TemplatedControl
{
    private TopLevel? _topLevel;
    private IInputElement? _previousFocus;

    public static readonly StyledProperty<IDialogManager?> ManagerProperty =
        AvaloniaProperty.Register<DialogHost, IDialogManager?>(nameof(Manager));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<DialogHost, bool>(nameof(IsOpen));

    public static readonly StyledProperty<object?> DialogContentProperty =
        AvaloniaProperty.Register<DialogHost, object?>(nameof(DialogContent));

    static DialogHost()
    {
        ManagerProperty.Changed.AddClassHandler<DialogHost>((host, args) => host.OnManagerChanged(args));
    }

    public IDialogManager? Manager
    {
        get => GetValue(ManagerProperty);
        set => SetValue(ManagerProperty, value);
    }

    /// <summary>Mirrors the manager, so the template can bind without reaching through it.</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        private set => SetValue(IsOpenProperty, value);
    }

    public object? DialogContent
    {
        get => GetValue(DialogContentProperty);
        private set => SetValue(DialogContentProperty, value);
    }

    private void OnManagerChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (args.OldValue is IDialogManager previous)
        {
            previous.PropertyChanged -= OnManagerPropertyChanged;
        }

        if (_topLevel is not null && args.NewValue is IDialogManager current)
        {
            current.PropertyChanged += OnManagerPropertyChanged;
        }

        Sync();
    }

    private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs args) => Sync();

    private void Sync()
    {
        bool wasOpen = IsOpen;
        object? previousContent = DialogContent;
        DialogContent = Manager?.Content;
        IsOpen = Manager?.IsOpen == true;

        if (IsOpen && (!wasOpen || !ReferenceEquals(previousContent, DialogContent)))
        {
            if (!wasOpen)
            {
                _previousFocus = _topLevel?.FocusManager?.GetFocusedElement();
            }

            Dispatcher.UIThread.Post(FocusDialog, DispatcherPriority.Loaded);
        }
        else if (!IsOpen && wasOpen)
        {
            var previousFocus = _previousFocus;
            _previousFocus = null;
            previousFocus?.Focus();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (Manager is { } manager)
        {
            manager.PropertyChanged += OnManagerPropertyChanged;
        }

        Sync();
        _topLevel?.AddHandler(GotFocusEvent, OnTopLevelGotFocus, RoutingStrategies.Bubble,
            handledEventsToo: true);

        if (IsOpen)
        {
            _previousFocus = _topLevel?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(FocusDialog, DispatcherPriority.Loaded);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Manager is { } manager)
        {
            manager.PropertyChanged -= OnManagerPropertyChanged;
        }

        _topLevel?.RemoveHandler(GotFocusEvent, OnTopLevelGotFocus);
        _topLevel = null;
        var previousFocus = _previousFocus;
        _previousFocus = null;
        previousFocus?.Focus();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (IsOpen && e.Source is Visual focused
            && !ReferenceEquals(focused, this) && !this.IsVisualAncestorOf(focused))
        {
            FocusDialog();
        }
    }

    private void FocusDialog()
    {
        if (!IsOpen || _topLevel is null)
        {
            return;
        }

        // The presenter must realize the content before its first field can receive focus.
        _topLevel.UpdateLayout();
        var first = this.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(control => control.Focusable && control.IsTabStop
                && control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
        if (first is null || !first.Focus())
        {
            Focus();
        }
    }

    /// <summary>
    /// A click on the background, but not on the dialog itself, dismisses it.
    /// </summary>
    /// <remarks>
    /// Matched on the scrim exactly rather than on "not the card", so a click that lands on a
    /// disabled control inside the dialog does not close it by falling through.
    /// </remarks>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (IsOpen && e.Source is Border { Name: "PART_Scrim" })
        {
            Manager?.Close();
            e.Handled = true;
        }
    }

    /// <summary>Escape dismisses any dialog, including content without a cancel button.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsOpen)
        {
            Manager?.Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}

/// <summary>
/// Shows the toasts a <see cref="ToastManager"/> is holding, stacked in a corner.
/// </summary>
public class ToastHost : ItemsControl
{
    private TopLevel? _topLevel;
    private IInputElement? _previousFocus;
    private Toast? _focusedToast;

    public static readonly StyledProperty<IToastManager?> ManagerProperty =
        AvaloniaProperty.Register<ToastHost, IToastManager?>(nameof(Manager));

    public static readonly StyledProperty<string> DismissLabelProperty =
        AvaloniaProperty.Register<ToastHost, string>(nameof(DismissLabel), "Close");

    static ToastHost()
    {
        ManagerProperty.Changed.AddClassHandler<ToastHost>((host, args) =>
        {
            if (args.OldValue is IToastManager previous)
            {
                previous.Toasts.CollectionChanged -= host.OnToastsChanged;
            }

            if (host._topLevel is not null && args.NewValue is IToastManager current)
            {
                current.Toasts.CollectionChanged += host.OnToastsChanged;
            }

            host.ItemsSource = (args.NewValue as IToastManager)?.Toasts;
            host.RestoreFocusIfRemoved();
        });
    }

    public ToastHost()
    {
        AddHandler(Button.ClickEvent, OnToastButtonClick);
    }

    public IToastManager? Manager
    {
        get => GetValue(ManagerProperty);
        set => SetValue(ManagerProperty, value);
    }

    public string DismissLabel
    {
        get => GetValue(DismissLabelProperty);
        set => SetValue(DismissLabelProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _previousFocus = _topLevel?.FocusManager?.GetFocusedElement();
        _topLevel?.AddHandler(GotFocusEvent, OnTopLevelGotFocus, RoutingStrategies.Bubble,
            handledEventsToo: true);
        if (Manager is { } manager)
        {
            manager.Toasts.CollectionChanged += OnToastsChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(GotFocusEvent, OnTopLevelGotFocus);
        if (Manager is { } manager)
        {
            manager.Toasts.CollectionChanged -= OnToastsChanged;
        }

        _topLevel = null;
        var previousFocus = _focusedToast is not null ? _previousFocus : null;
        _previousFocus = null;
        _focusedToast = null;
        previousFocus?.Focus();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Visual focused && this.IsVisualAncestorOf(focused))
        {
            _focusedToast = (e.Source as StyledElement)?.DataContext as Toast;
        }
        else
        {
            _focusedToast = null;
            _previousFocus = e.Source as IInputElement;
        }
    }

    private void OnToastsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RestoreFocusIfRemoved();

    private void RestoreFocusIfRemoved()
    {
        if (_focusedToast is { } toast && Manager?.Toasts.Contains(toast) != true)
        {
            _focusedToast = null;
            _previousFocus?.Focus();
        }
    }

    private void OnToastButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { DataContext: Toast toast } button
            || button.Name is not ("PART_ActivateToast" or "PART_DismissToast")
            || Manager is not { } manager || !manager.Toasts.Contains(toast))
        {
            return;
        }

        manager.Dismiss(toast);
        e.Handled = true;
        if (button.Name == "PART_ActivateToast")
        {
            toast.Clicked?.Invoke();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _focusedToast is { } toast && Manager is { } manager)
        {
            manager.Dismiss(toast);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
