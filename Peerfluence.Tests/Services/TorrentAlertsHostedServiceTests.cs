using Peerfluence.Services;
using Peerfluence.Core.Services;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

public sealed class TorrentAlertsHostedServiceTests
{
    [Fact]
    public async Task StartAsync_RegistersAlertMask_AndPublishesIncomingAlerts()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(new InfoHash(new byte[20]));
        var alert = new SimpleTorrentAlert
        {
            Id = AlertId.TorrentAdded,
            Torrent = torrent
        };

        torrentService.GetAlertsAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => StreamAlerts(alert, callInfo.ArgAt<CancellationToken>(1)));

        var sut = new TorrentAlertsHostedService(torrentService);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await sut.StopAsync(TestContext.Current.CancellationToken);

        torrentService.Received(1).RegisterAlertMask(Arg.Any<uint>());
        torrentService.Received(1).PublishAlert(alert);
    }

    private static async IAsyncEnumerable<Alert> StreamAlerts(Alert alert, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return alert;
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
