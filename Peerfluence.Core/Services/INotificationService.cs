using System.Collections.ObjectModel;

namespace Peerfluence.Core.Services;

public interface INotificationService
{
    ReadOnlyObservableCollection<NotificationItem> Notifications { get; }

    void Publish(NotificationItem notification, TimeSpan? autoDismiss = null);

    void Dismiss(NotificationItem notification);
}
