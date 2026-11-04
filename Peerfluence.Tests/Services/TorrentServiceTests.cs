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
    /// A restored SSL torrent needs this every session. Removing it and adding it back did the same
    /// thing, and lost the torrent if the second add failed.
    /// </summary>
    [Fact]
    public async Task SslCertificate_IsGivenToTheTorrentInPlace()
    {
        using var data = new SslTorrentTestData();
        var torrent = Substitute.For<ITorrent>();
        torrent.State.Returns(TorrentState.Stopped);
        torrent.QueueAutoStart = true;
        var engine = Substitute.For<IClientEngine>();
        var sut = Sut(engine);

        await sut.SetSslCertificateAsync(torrent, data.Certificate, TestContext.Current.CancellationToken);

        await torrent.Received(1).SetSslCertificateAsync(data.Certificate, Arg.Any<CancellationToken>());
        Assert.False(torrent.QueueAutoStart);
        await engine.DidNotReceive().RemoveTorrentAsync(Arg.Any<ITorrent>(), Arg.Any<RemoveOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SslCertificate_ForARunningTorrent_IsRefusedWithoutStoppingIt()
    {
        using var data = new SslTorrentTestData();
        var torrent = Substitute.For<ITorrent>();
        torrent.State.Returns(TorrentState.Active);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sut().SetSslCertificateAsync(torrent, data.Certificate, TestContext.Current.CancellationToken));

        await torrent.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>What a torrent looks like while it hashes the files already at its destination.</summary>
    private static ITorrent CheckingForever()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            torrent.QueueAutoStart = true;
            torrent.State.Returns(TorrentState.CheckingFiles);
            return new TaskCompletionSource().Task;
        });
        return torrent;
    }

    private static TorrentService Sut(IClientEngine? engine = null, IAppMessenger? messenger = null, ITorrentEngineService? engineService = null)
    {
        if (engineService == null)
        {
            engineService = Substitute.For<ITorrentEngineService>();
            engineService.Engine.Returns(engine ?? Substitute.For<IClientEngine>());
        }

        return new TorrentService(engineService, messenger ?? Substitute.For<IAppMessenger>(), new HttpClient(), SeedingDefaults.Off)
        {
            StartWait = TimeSpan.FromMilliseconds(20)
        };
    }

    /// <summary>Everything a start can be refused for happens at once, and the caller has to hear it.</summary>
    [Fact]
    public async Task Start_ThatFailsStraightAway_FailsForTheCaller()
    {
        var torrent = Substitute.For<ITorrent>();
        var refused = new System.Security.Authentication.AuthenticationException("Certificate required");
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(refused));

        var thrown = await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(
            () => Sut().StartAsync(torrent, TestContext.Current.CancellationToken));

        Assert.Same(refused, thrown);
    }

    [Fact]
    public async Task Start_ThatIsCheckingFiles_ReturnsAndCarriesOn()
    {
        var torrent = CheckingForever();
        var sut = Sut();

        await sut.StartAsync(torrent, TestContext.Current.CancellationToken);

        // Asking again changes nothing: the first start has not finished.
        await sut.StartAsync(torrent, TestContext.Current.CancellationToken);
        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_OfATorrentAlreadyChecking_LeavesItAlone()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.State.Returns(TorrentState.CheckingFiles);

        await Sut().StartAsync(torrent, TestContext.Current.CancellationToken);

        await torrent.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_ThatFailsAfterTheWait_IsReported()
    {
        var torrent = Substitute.For<ITorrent>();
        var check = new TaskCompletionSource();
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(check.Task);
        var published = new TaskCompletionSource<TorrentStartFailedMessage>();
        var messenger = Substitute.For<IAppMessenger>();
        messenger.When(m => m.Publish(Arg.Any<TorrentStartFailedMessage>()))
            .Do(call => published.TrySetResult(call.Arg<TorrentStartFailedMessage>()));

        await Sut(messenger: messenger).StartAsync(torrent, TestContext.Current.CancellationToken);
        var failure = new IOException("The disk went away");
        check.SetException(failure);

        var message = await published.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Same(torrent, message.Torrent);
        Assert.Same(failure, message.Exception);
    }

    /// <summary>
    /// The engine ends the check, and the start it ends has already set auto-start, which would have
    /// the queue start the torrent again.
    /// </summary>
    [Fact]
    public async Task Stop_DuringAFileCheck_KeepsTheQueueFromStartingTheTorrentAgain()
    {
        var torrent = CheckingForever();
        var sut = Sut();
        await sut.StartAsync(torrent, TestContext.Current.CancellationToken);
        Assert.True(torrent.QueueAutoStart);

        await sut.StopAsync(torrent, TestContext.Current.CancellationToken);

        await torrent.Received(1).StopAsync(Arg.Any<CancellationToken>());
        Assert.False(torrent.QueueAutoStart);
    }

    [Fact]
    public async Task Stop_OfARunningTorrent_LeavesTheQueueAlone()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.State.Returns(TorrentState.Active);
        torrent.QueueAutoStart = true;

        await Sut().StopAsync(torrent, TestContext.Current.CancellationToken);

        await torrent.Received(1).StopAsync(Arg.Any<CancellationToken>());
        Assert.True(torrent.QueueAutoStart);
    }

    private static IClientEngine EngineAdding(ITorrent torrent, Action<AddTorrentOptions>? onAdd = null)
    {
        var engine = Substitute.For<IClientEngine>();
        engine.Settings.Returns(new Settings());
        engine.AddTorrentAsync(Arg.Any<TorrentFile>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            onAdd?.Invoke(call.Arg<AddTorrentOptions>());
            return Task.FromResult(torrent);
        });
        return engine;
    }

    /// <summary>
    /// An add that lets the engine start the torrent returns after hashing whatever is already on
    /// disk, which would hold the add dialog or a Transmission client's request for all of it.
    /// </summary>
    [Fact]
    public async Task AddingATorrent_AddsItStopped_ThenStartsIt()
    {
        var torrent = Substitute.For<ITorrent>();
        var startImmediatelyAtAdd = true;
        var engine = EngineAdding(torrent, options => startImmediatelyAtAdd = options.StartImmediately);
        var options = new AddTorrentOptions { DownloadPath = Path.GetTempPath() };

        var added = await Sut(engine).AddTorrentAsync(PlainTorrentTestData.Metadata(), options, TestContext.Current.CancellationToken);

        Assert.Same(torrent, added);
        Assert.False(startImmediatelyAtAdd);
        Assert.True(options.StartImmediately);
        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddingATorrentOverExistingFiles_ReturnsWhileTheyAreChecked()
    {
        var torrent = CheckingForever();
        var sut = Sut(EngineAdding(torrent));

        var added = await sut.AddTorrentAsync(
            PlainTorrentTestData.Metadata(), new AddTorrentOptions { DownloadPath = Path.GetTempPath() }, TestContext.Current.CancellationToken);

        Assert.Same(torrent, added);
    }

    [Fact]
    public async Task AddingATorrentStopped_DoesNotStartIt()
    {
        var torrent = Substitute.For<ITorrent>();

        await Sut(EngineAdding(torrent)).AddTorrentAsync(
            PlainTorrentTestData.Metadata(),
            new AddTorrentOptions { DownloadPath = Path.GetTempPath(), StartImmediately = false },
            TestContext.Current.CancellationToken);

        await torrent.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The engine discards a torrent it could not start, and this has to go on doing so.</summary>
    [Fact]
    public async Task AddingATorrentThatCannotStart_FailsAndLeavesNothingBehind()
    {
        var torrent = Substitute.For<ITorrent>();
        var refused = new System.Security.Authentication.AuthenticationException("Certificate required");
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(refused));
        var engine = EngineAdding(torrent);

        var thrown = await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => Sut(engine).AddTorrentAsync(
            PlainTorrentTestData.Metadata(), new AddTorrentOptions { DownloadPath = Path.GetTempPath() }, TestContext.Current.CancellationToken));

        Assert.Same(refused, thrown);
        await engine.Received(1).RemoveTorrentAsync(torrent, RemoveOptions.None, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddingATorrentThatCannotStart_ReportsWhyEvenIfItCannotBeDiscarded()
    {
        var torrent = Substitute.For<ITorrent>();
        var refused = new System.Security.Authentication.AuthenticationException("Certificate required");
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(refused));
        var engine = EngineAdding(torrent);
        engine.RemoveTorrentAsync(torrent, Arg.Any<RemoveOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("Session folder is gone")));

        var thrown = await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => Sut(engine).AddTorrentAsync(
            PlainTorrentTestData.Metadata(), new AddTorrentOptions { DownloadPath = Path.GetTempPath() }, TestContext.Current.CancellationToken));

        Assert.Same(refused, thrown);
    }

    [Fact]
    public async Task AddingAMagnet_AddsItStopped_ThenStartsIt()
    {
        var (engineService, captured) = CapturingEngine();
        var torrent = Substitute.For<ITorrent>();
        var startImmediatelyAtAdd = true;
        engineService.Engine.AddMagnetAsync(Arg.Any<MagnetLink>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                startImmediatelyAtAdd = call.Arg<AddTorrentOptions>().StartImmediately;
                return Task.FromResult(torrent);
            });

        await Sut(engineService: engineService).AddMagnetAsync(Magnet, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(captured());
        Assert.False(startImmediatelyAtAdd);
        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

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
            Files =
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
