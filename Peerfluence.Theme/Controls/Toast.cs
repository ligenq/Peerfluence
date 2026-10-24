using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;

namespace Peerfluence.Theme.Controls;

/// <summary>How much attention a toast asks for, and therefore which colour it takes.</summary>
public enum ToastSeverity
{
    Information,
    Success,
    Warning,
    Error,
}

/// <summary>One transient message.</summary>
/// <remarks>
/// Immutable. A toast that changed after it appeared would be a message the reader had already
/// finished with, and nothing here needs it.
/// </remarks>
public sealed class Toast
{
    public required string Title { get; init; }

    public string? Message { get; init; }

    public ToastSeverity Severity { get; init; } = ToastSeverity.Information;

    /// <summary>Run when the toast is clicked, in addition to dismissing it.</summary>
    public Action? Clicked { get; init; }

    /// <summary>
    /// Severity as flags, because a style class binds to a bool and nothing else.
    /// </summary>
    /// <remarks>
    /// Four properties rather than a converter: a converter would have to resolve a theme brush
    /// from static code, which means it cannot follow a variant or an accent change.
    /// </remarks>
    public bool IsInformation => Severity == ToastSeverity.Information;

    public bool IsSuccess => Severity == ToastSeverity.Success;

    public bool IsWarning => Severity == ToastSeverity.Warning;

    public bool IsError => Severity == ToastSeverity.Error;
}

/// <summary>
/// The toasts currently on screen, and how long each one stays.
/// </summary>
/// <remarks>
/// An interface so that a consumer can inject it as an abstraction and substitute it in a test.
/// The host binds to this rather than to the implementation for the same reason.
/// </remarks>
public interface IToastManager
{
    /// <summary>What is on screen, in the order it arrived.</summary>
    ObservableCollection<Toast> Toasts { get; }

    void Show(Toast toast, TimeSpan? autoDismiss = null);

    void Dismiss(Toast toast);
}

/// <summary>The one dialog on screen, if any.</summary>
public interface IDialogManager : INotifyPropertyChanged
{
    /// <summary>What to show, or null when nothing is open.</summary>
    object? Content { get; }

    bool IsOpen { get; }

    void Show(object content);

    void Close();
}

/// <summary>
/// The toasts currently on screen, and how long each one stays.
/// </summary>
/// <remarks>
/// <para>
/// UI-thread affine, and says so: <see cref="Show"/> verifies rather than marshalling, because a
/// caller that does not know it is touching the interface is a caller that will eventually mutate
/// this collection from a background thread and misbehave for somebody else, later, occasionally.
/// Marshalling belongs at the boundary where that is a known fact.
/// </para>
/// <para>
/// Oldest is dropped past <see cref="Capacity"/>: a burst of failures should not fill the screen
/// and hide the interface it is reporting on.
/// </para>
/// </remarks>
public sealed class ToastManager : IToastManager
{
    private const int Capacity = 4;

    private readonly Dictionary<Toast, DispatcherTimer> _timers = [];

    public ObservableCollection<Toast> Toasts { get; } = [];

    public void Show(Toast toast, TimeSpan? autoDismiss = null)
    {
        ArgumentNullException.ThrowIfNull(toast);
        Dispatcher.UIThread.VerifyAccess();

        while (Toasts.Count >= Capacity)
        {
            Dismiss(Toasts[0]);
        }

        Toasts.Add(toast);

        if (autoDismiss is not { } delay || delay <= TimeSpan.Zero)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => Dismiss(toast);
        _timers[toast] = timer;
        timer.Start();
    }

    public void Dismiss(Toast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        Dispatcher.UIThread.VerifyAccess();

        if (_timers.Remove(toast, out var timer))
        {
            timer.Stop();
        }

        Toasts.Remove(toast);
    }
}

/// <summary>
/// The one dialog on screen, if any.
/// </summary>
/// <remarks>
/// One at a time by construction rather than by convention. Stacked modals are a way of asking two
/// questions where the second answer invalidates the first, and nothing in a desktop application of
/// this shape needs them.
/// </remarks>
public sealed class DialogManager : IDialogManager
{
    private object? _content;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What to show, or null when nothing is open.</summary>
    public object? Content
    {
        get => _content;
        private set
        {
            if (ReferenceEquals(_content, value))
            {
                return;
            }

            _content = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen)));
        }
    }

    public bool IsOpen => _content is not null;

    public void Show(object content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Dispatcher.UIThread.VerifyAccess();
        Content = content;
    }

    public void Close()
    {
        Dispatcher.UIThread.VerifyAccess();
        Content = null;
    }
}
