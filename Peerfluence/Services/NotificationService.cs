using System;
using Avalonia.Threading;
using Peerfluence.Theme.Controls;

namespace Peerfluence.Services;

public sealed class NotificationService : INotificationService
{
    private readonly IToastManager _toastManager;

    public NotificationService(IToastManager toastManager)
    {
        _toastManager = toastManager;
    }

    /// <summary>
    /// Shows a notification, from whichever thread happens to have something to say.
    /// </summary>
    /// <remarks>
    /// The marshalling is here rather than at the call sites. Most of them are hosted services -
    /// a torrent finishing, a completion action failing - which have no reason to know that saying
    /// so touches the interface, and a contract of "call me only from the UI thread" would be ten
    /// chances to forget.
    /// </remarks>
    public void Publish(NotificationItem notification, TimeSpan? autoDismiss = null)
    {
        ArgumentNullException.ThrowIfNull(notification);

        Dispatcher.UIThread.Post(() => Show(notification, autoDismiss));
    }

    /// <summary>
    /// Queues the toast. Must be on the UI thread.
    /// </summary>
    /// <remarks>
    /// <see cref="ToastManager"/> verifies this for itself, so a lost Post is an exception on the
    /// first notification in every build rather than a collection mutated from a background thread
    /// that misbehaves for somebody else, later, occasionally.
    /// </remarks>
    private void Show(NotificationItem notification, TimeSpan? autoDismiss)
    {
        var severity = notification.Type switch
        {
            NotificationType.Success => ToastSeverity.Success,
            NotificationType.Warning => ToastSeverity.Warning,
            NotificationType.Error => ToastSeverity.Error,
            _ => ToastSeverity.Information,
        };

        _toastManager.Show(
            new Toast
            {
                Title = notification.Title,
                Message = notification.Message,
                Severity = severity,
                Clicked = notification.OnClick,
            },
            autoDismiss);
    }
}
