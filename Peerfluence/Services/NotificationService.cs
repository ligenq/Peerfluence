using System;
using Avalonia.Threading;
using SukiUI.Toasts;

namespace Peerfluence.Services;

public sealed class NotificationService : INotificationService
{
    private readonly ISukiToastManager _toastManager;

    public NotificationService(ISukiToastManager toastManager)
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
    /// The check is the point. Nothing about <see cref="ISukiToastManager"/> says it is thread
    /// affine - it is an interface like any other - so the requirement lived in whoever remembered
    /// it, and half of this method used to marshal while the other half did not. Verifying it here
    /// turns losing the Post into an exception on the first notification in every build, rather than
    /// into a collection mutated from a background thread that misbehaves for somebody else, later,
    /// occasionally.
    /// </remarks>
    private void Show(NotificationItem notification, TimeSpan? autoDismiss)
    {
        Dispatcher.UIThread.VerifyAccess();

        var type = notification.Type switch
        {
            NotificationType.Success => Avalonia.Controls.Notifications.NotificationType.Success,
            NotificationType.Warning => Avalonia.Controls.Notifications.NotificationType.Warning,
            NotificationType.Error => Avalonia.Controls.Notifications.NotificationType.Error,
            _ => Avalonia.Controls.Notifications.NotificationType.Information
        };

        var builder = _toastManager.CreateToast()
            .OfType(type)
            .WithTitle(notification.Title)
            .WithContent(notification.Message)
            .Dismiss().ByClicking();

        if (autoDismiss.HasValue)
        {
            builder.Dismiss().After(autoDismiss.Value);
        }

        builder.Queue();
    }
}
