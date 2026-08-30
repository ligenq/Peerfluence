using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using SukiUI.Toasts;
using System.Linq;

namespace Peerfluence.Services;

public sealed class NotificationService : INotificationService
{
    private readonly ObservableCollection<NotificationItem> _notifications = new();

    private readonly ISukiToastManager _toastManager;

    public NotificationService(ISukiToastManager toastManager)
    {
        _toastManager = toastManager;
        Notifications = new ReadOnlyObservableCollection<NotificationItem>(_notifications);
    }

    public ReadOnlyObservableCollection<NotificationItem> Notifications { get; }

    public void Publish(NotificationItem notification, TimeSpan? autoDismiss = null)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _notifications.Add(notification);

        var type = notification.Type switch
        {
            NotificationType.Success => Avalonia.Controls.Notifications.NotificationType.Success,
            NotificationType.Warning => Avalonia.Controls.Notifications.NotificationType.Warning,
            NotificationType.Error => Avalonia.Controls.Notifications.NotificationType.Error,
            _ => Avalonia.Controls.Notifications.NotificationType.Information
        };

        Dispatcher.UIThread.Post(() =>
        {
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
        });
    }

    public void Dismiss(NotificationItem notification)
    {
        _notifications.Remove(notification);
    }
}
