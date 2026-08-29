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
        var sut = new TorrentService(engineService, messenger, new HttpClient());

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
        var sut = new TorrentService(engineService, messenger, new HttpClient());

        var stats = sut.GetStats();

        Assert.Equal(0, stats.DownloadSpeed);
        Assert.Equal(0, stats.UploadSpeed);
    }

    [Fact]
    public async Task AddMagnetAsync_RejectsASelfUpdatingLinkThatCarriesNoInfoHash()
    {
        // PeerSharp 3.0 parses BEP 46 links, which name no torrent yet: the current info hash lives
        // in the DHT. Adding one would register a torrent under an empty hash.
        var engine = Substitute.For<IClientEngine>();
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);
        var sut = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient());

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.AddMagnetAsync(
                $"magnet:?xs=urn:btpk:{new string('a', 64)}",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TorrentService.MagnetWithoutInfoHashMessage, exception.Message);
        await engine.DidNotReceive().AddMagnetAsync(
            Arg.Any<MagnetLink>(),
            Arg.Any<AddTorrentOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("magnet:?xs=urn:btpk:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    public void HasUsableInfoHash_TellsAddableLinksFromOnesThatNameNothingYet(string magnetUri, bool expected)
    {
        Assert.Equal(expected, TorrentService.HasUsableInfoHash(MagnetLink.Parse(magnetUri)));
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
        engine.GetTorrents().Returns([torrent]);

        var movedPath = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        torrent.SetDownloadPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                movedPath.TrySetResult(callInfo.Arg<string>());
                return Task.CompletedTask;
            });

        var sut = new TorrentService(engineService, messenger, new HttpClient());

        sut.PublishAlert(
            new SimpleMetadataAlert
            {
                Id = AlertId.MetadataInitialized,
                Torrent = torrent
            },
            TestContext.Current.CancellationToken);

        var uniquePath = await movedPath.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(defaultRoot, "Ubuntu ISO"), uniquePath);
        await torrent.Received(1).StopAsync(Arg.Any<CancellationToken>());
        await torrent.Received(1).SetDownloadPathAsync(uniquePath, Arg.Any<CancellationToken>());
        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
        messenger.Received(1).Publish(Arg.Is<TorrentAlertMessage>(message => ReferenceEquals(message.Torrent, torrent)));
    }

    [Fact]
    public async Task SavingTheSession_AsksTheEngineToWriteIt()
    {
        // Called on shutdown and on a timer. It is the only thing that makes a restart resume
        // rather than start over, so it must reach the engine rather than be swallowed.
        var engine = Substitute.For<IClientEngine>();
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);

        var sut = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient());

        await sut.SaveSessionAsync(TestContext.Current.CancellationToken);

        await engine.Received(1).SaveSessionAsync(Arg.Any<CancellationToken>());
    }

}
