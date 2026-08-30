using Peerfluence.Core.Services;
using Peerfluence.Core.Messaging;
using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

[Collection("Messenger")]
public sealed class TorrentServiceTests
{
    /// <summary>
    /// A settings service whose seeding defaults say what the test needs them to.
    /// </summary>
    private static IAppSettingsService SeedingSettings(
        bool limitRatio = false,
        float ratio = 2.0f,
        bool limitTime = false,
        int minutes = 1440)
    {
        var settings = new Peerfluence.Core.Config.AppSettings();
        settings.Seeding.LimitRatio = limitRatio;
        settings.Seeding.RatioLimit = ratio;
        settings.Seeding.LimitSeedTime = limitTime;
        settings.Seeding.SeedTimeLimitMinutes = minutes;

        var service = Substitute.For<IAppSettingsService>();
        service.Current.Returns(settings);
        return service;
    }

    /// <summary>An engine that records the options it was handed.</summary>
    private static (ITorrentEngineService Service, Func<AddTorrentOptions?> Captured) CapturingEngine()
    {
        AddTorrentOptions? captured = null;
        var engine = Substitute.For<IClientEngine>();
        engine.Settings.Returns(new Settings());
        engine.AddMagnetAsync(Arg.Any<MagnetLink>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<AddTorrentOptions>();
                return Task.FromResult(Substitute.For<ITorrent>());
            });

        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);
        return (engineService, () => captured);
    }

    private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task AddingATorrent_TakesTheSeedingGoalsFromTheSettings()
    {
        // Without a default, every torrent seeds for ever unless somebody sets it individually.
        var (engineService, captured) = CapturingEngine();
        var sut = new TorrentService(
            engineService,
            Substitute.For<IAppMessenger>(),
            new HttpClient(),
            SeedingSettings(limitRatio: true, ratio: 1.5f, limitTime: true, minutes: 90));

        await sut.AddMagnetAsync(Magnet, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1.5f, captured()!.RatioLimit);
        Assert.Equal(TimeSpan.FromMinutes(90), captured()!.SeedTimeLimit);
    }

    [Fact]
    public async Task ATorrentAddedWithGoalsOfItsOwn_KeepsThem()
    {
        // The add dialog and the details pane set these per torrent, and a default must not overrule
        // somebody who has just said what they want.
        var (engineService, captured) = CapturingEngine();
        var sut = new TorrentService(
            engineService,
            Substitute.For<IAppMessenger>(),
            new HttpClient(),
            SeedingSettings(limitRatio: true, ratio: 1.5f));

        await sut.AddMagnetAsync(
            Magnet,
            new AddTorrentOptions { RatioLimit = 9.0f },
            TestContext.Current.CancellationToken);

        Assert.Equal(9.0f, captured()!.RatioLimit);
    }

    [Fact]
    public async Task WithNoSeedingGoalsSet_ATorrentIsGivenNone()
    {
        var (engineService, captured) = CapturingEngine();
        var sut = new TorrentService(
            engineService,
            Substitute.For<IAppMessenger>(),
            new HttpClient(),
            SeedingSettings());

        await sut.AddMagnetAsync(Magnet, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(captured()!.RatioLimit);
        Assert.Null(captured()!.SeedTimeLimit);
    }

    [Fact]
    public void GetStats_ReturnsEmptyStats_WhenEngineIsUnavailable()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(_ => throw new InvalidOperationException("Torrent engine is not initialized."));
        var messenger = Substitute.For<IAppMessenger>();
        var sut = new TorrentService(engineService, messenger, new HttpClient(), SeedingDefaults.Off);

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
        var sut = new TorrentService(engineService, messenger, new HttpClient(), SeedingDefaults.Off);

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
        var sut = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient(), SeedingDefaults.Off);

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

        var sut = new TorrentService(engineService, messenger, new HttpClient(), SeedingDefaults.Off);

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

        var sut = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient(), SeedingDefaults.Off);

        await sut.SaveSessionAsync(TestContext.Current.CancellationToken);

        await engine.Received(1).SaveSessionAsync(Arg.Any<CancellationToken>());
    }

}
