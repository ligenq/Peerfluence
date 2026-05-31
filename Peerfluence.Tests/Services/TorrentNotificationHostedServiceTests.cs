using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Services;
using Peerfluence.Core.Messaging;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

[Collection("Messenger")]
public sealed class TorrentNotificationHostedServiceTests
{
    [Fact]
    public async Task TorrentErrorNotification_IncludesExceptionMessage()
    {
        var notificationService = new NotificationService();
        var sut = new TorrentNotificationHostedService(notificationService);
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Ubuntu ISO");

        await sut.StartAsync(TestContext.Current.CancellationToken);

        WeakReferenceMessenger.Default.Send(new TorrentAlertMessage(
            torrent,
            new TorrentErrorAlert
            {
                Id = AlertId.TorrentError,
                Torrent = torrent,
                Exception = new InvalidOperationException("disk full")
            }));

        var notification = Assert.Single(notificationService.Notifications, n => n.Title == "Torrent error");
        Assert.Equal("Torrent error", notification.Title);
        Assert.Contains("Ubuntu ISO", notification.Message);
        Assert.Contains("disk full", notification.Message);

        await sut.StopAsync(TestContext.Current.CancellationToken);
    }
}
