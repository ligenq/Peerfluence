using Peerfluence.Core.Services;
using Peerfluence.Core.Messaging;
using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

[Collection("Messenger")]
public sealed class TorrentServiceTests
{
    [Fact]
    public void GetStats_ReturnsEmptyStats_WhenEngineIsUnavailable()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(_ => throw new InvalidOperationException("Torrent engine is not initialized."));
        var messenger = Substitute.For<IAppMessenger>();
        var sut = new TorrentService(engineService, messenger);

        var stats = sut.GetStats();

        Assert.Equal(0, stats.DownloadSpeed);
        Assert.Equal(0, stats.UploadSpeed);
    }

    [Fact]
    public void GetStats_ReturnsEmptyStats_WhenEngineHasBeenDisposed()
    {
        var engine = Substitute.For<IClientEngine>();
        engine.GetStats().Returns(_ => throw new ObjectDisposedException("PeerSharp.Internals.ClientEngine"));
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);
        var messenger = Substitute.For<IAppMessenger>();
        var sut = new TorrentService(engineService, messenger);

        var stats = sut.GetStats();

        Assert.Equal(0, stats.DownloadSpeed);
        Assert.Equal(0, stats.UploadSpeed);
    }

    [Fact]
    public async Task PublishAlert_MetadataInitialized_MovesTorrentIntoUniqueSubfolder_AndRestartsIfNeeded()
    {
        var defaultRoot = Path.Combine(Path.GetTempPath(), "peerfluence-default-root");
        var engine = Substitute.For<IClientEngine>();
        engine.Settings.Returns(new Settings
        {
            Files = new FilesSettings
            {
                DefaultDownloadPath = defaultRoot
            }
        });

        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);
        var messenger = Substitute.For<IAppMessenger>();

        var files = Substitute.For<IFiles>();
        files.DownloadPath.Returns(defaultRoot);

        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Ubuntu ISO");
        torrent.Files.Returns(files);
        torrent.Started.Returns(true);
        engine.GetTorrents().Returns(new[] { torrent });

        var movedPath = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        torrent.SetDownloadPathAsync(Arg.Any<string>())
            .Returns(callInfo =>
            {
                movedPath.TrySetResult(callInfo.Arg<string>());
                return Task.CompletedTask;
            });

        var sut = new TorrentService(engineService, messenger);

        sut.PublishAlert(new SimpleMetadataAlert
        {
            Id = AlertId.MetadataInitialized,
            Torrent = torrent
        });

        var uniquePath = await movedPath.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(defaultRoot, "Ubuntu ISO"), uniquePath);
        await torrent.Received(1).StopAsync();
        await torrent.Received(1).SetDownloadPathAsync(uniquePath);
        await torrent.Received(1).StartAsync();
        messenger.Received(1).Publish(Arg.Is<TorrentAlertMessage>(message => ReferenceEquals(message.Torrent, torrent)));
    }
}
