using Peerfluence.Core;
using Peerfluence.Core.Services;
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
        // Substituted rather than real: what is being tested is what this service says, and a
        // substitute records it without a dispatcher having to run.
        var published = new List<NotificationItem>();
        var notificationService = Substitute.For<INotificationService>();
        notificationService
            .When(service => service.Publish(Arg.Any<NotificationItem>(), Arg.Any<TimeSpan?>()))
            .Do(call => published.Add(call.Arg<NotificationItem>()));
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

        var notification = Assert.Single(published, n => n.Title == "Torrent error");
        Assert.Equal("Torrent error", notification.Title);
        Assert.Contains("Ubuntu ISO", notification.Message);
        Assert.Contains("disk full", notification.Message);

        await sut.StopAsync(TestContext.Current.CancellationToken);
    }
}
