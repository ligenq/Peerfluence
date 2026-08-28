using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Material.Icons;
using Microsoft.Extensions.Hosting;
using Peerfluence.Core.Messaging;
using Peerfluence.Properties;
using Peerfluence.ViewModels;

namespace Peerfluence.Services;

public sealed class TorrentNotificationHostedService : IHostedService
{
    private readonly INotificationService _notificationService;

    public TorrentNotificationHostedService(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        WeakReferenceMessenger.Default.Register<TorrentAlertMessage>(this, (_, msg) => OnTorrentAlert(msg));
        WeakReferenceMessenger.Default.Register<EngineAlertMessage>(this, (_, msg) => OnEngineAlert(msg));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        WeakReferenceMessenger.Default.Unregister<TorrentAlertMessage>(this);
        WeakReferenceMessenger.Default.Unregister<EngineAlertMessage>(this);
        return Task.CompletedTask;
    }

    private void OnTorrentAlert(TorrentAlertMessage msg)
    {
        switch (msg.Alert.Id)
        {
            case AlertId.TorrentFinished:
                Publish(msg, Resources.Notification_DownloadFinished, NotificationKind.Success, MaterialIconKind.Check, TimeSpan.FromSeconds(6));
                break;
            case AlertId.TorrentInterrupted:
                Publish(msg, Resources.Notification_TorrentInterrupted, NotificationKind.Warning, MaterialIconKind.StopCircleOutline, TimeSpan.FromSeconds(8));
                break;
            case AlertId.TorrentError:
                PublishError(msg);
                break;
            case AlertId.MetadataInitialized:
                Publish(msg, Resources.Notification_MetadataReady, NotificationKind.Info, MaterialIconKind.InformationOutline, TimeSpan.FromSeconds(6));
                break;
            case AlertId.MetadataDownloadStalled:
                // Fires once, not per attempt, so this cannot become a stream of toasts. It is the
                // only warning a magnet that "just sits there" ever produces.
                Publish(msg, Resources.Notification_MetadataStalled, NotificationKind.Warning, MaterialIconKind.CloudQuestion, TimeSpan.FromSeconds(10));
                break;
        }
    }

    /// <summary>
    /// Alerts about the session rather than a torrent.
    /// </summary>
    private void OnEngineAlert(EngineAlertMessage msg)
    {
        if (msg.Alert is not ListenPortChangedAlert port)
        {
            return;
        }

        // Worth interrupting for: the session is reachable, but on a port nobody forwarded, so
        // inbound connections stop arriving and nothing else would ever say why.
        var notification = new NotificationItem(
            Resources.Notification_ListenPortChanged,
            string.Format(
                CultureInfo.CurrentCulture,
                Resources.Notification_ListenPortChangedBody,
                port.RequestedPort,
                port.ActualPort),
            NotificationType.Warning,
            MaterialIconKind.LanDisconnect.ToString());

        _notificationService.Publish(notification, TimeSpan.FromSeconds(15));
    }

    private void Publish(TorrentAlertMessage msg, string title, NotificationKind kind, MaterialIconKind icon, TimeSpan? autoDismiss, string? message = null)
    {
        message ??= msg.Torrent.Name;
        var type = kind switch
        {
            NotificationKind.Success => NotificationType.Success,
            NotificationKind.Warning => NotificationType.Warning,
            NotificationKind.Error => NotificationType.Error,
            _ => NotificationType.Info
        };

        var notification = new NotificationItem(title, message, type, icon.ToString());
        _notificationService.Publish(notification, autoDismiss);
    }

    private void PublishError(TorrentAlertMessage msg)
    {
        var message = msg.Torrent.Name;
        if (msg.Alert is TorrentErrorAlert errorAlert)
        {
            var detail = errorAlert.Exception.Message;
            if (!string.IsNullOrWhiteSpace(detail))
            {
                message = $"{message}: {detail}";
            }
        }

        Publish(msg, Resources.Notification_TorrentError, NotificationKind.Error, MaterialIconKind.DeleteOutline, TimeSpan.FromSeconds(12), message);
    }
}
