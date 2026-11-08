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

        Assert.Equal(TorrentState.CheckingFiles, torrent.State);
    }

    /// <summary>Asked of every torrent by a Transmission client starting all of them.</summary>
    [Fact]
    public async Task Start_OfARunningTorrent_DoesNothing()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Started.Returns(true);

        await Sut().StartAsync(torrent, TestContext.Current.CancellationToken);

        await torrent.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// CheckingFiles alone cannot say whether this is a start on its way or a recheck of a stopped
    /// torrent, which will stay stopped unless this start goes ahead.
    /// </summary>
    [Fact]
    public async Task Start_DuringACheck_GoesAhead()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.State.Returns(TorrentState.CheckingFiles);

        await Sut().StartAsync(torrent, TestContext.Current.CancellationToken);

        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_ThatFindsAnotherStartGotThereFirst_IsNotAFailure()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            torrent.Started.Returns(true);
            return Task.FromException(new InvalidOperationException("Torrent is already started."));
        });

        await Sut().StartAsync(torrent, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Start_ThatFindsAnotherStartGotThereFirst_AfterTheWait_IsNotReported()
    {
        var torrent = Substitute.For<ITorrent>();
        var check = new TaskCompletionSource();
        var messenger = Substitute.For<IAppMessenger>();
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(check.Task);

        await Sut(messenger: messenger).StartAsync(torrent, TestContext.Current.CancellationToken);
        torrent.Started.Returns(true);
        check.SetException(new InvalidOperationException("Torrent is already started."));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        messenger.DidNotReceive().Publish(Arg.Any<TorrentStartFailedMessage>());
    }

    /// <summary>The start's own timeout, which is a failure like any other, and not the wait's.</summary>
    [Fact]
    public async Task Start_ThatFailsWithATimeoutOfItsOwn_FailsForTheCaller()
    {
        var torrent = Substitute.For<ITorrent>();
        var timedOut = new TimeoutException("The disk did not answer.");
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(timedOut));

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => Sut().StartAsync(torrent, TestContext.Current.CancellationToken));

        Assert.Same(timedOut, thrown);
    }

    /// <summary>
    /// Someone who stopped waiting for an add has not asked for the torrent to go. The start's comment
    /// says as much, and this used to remove the torrent all the same.
    /// </summary>
    [Fact]
    public async Task Add_WhoseCallerStopsWaiting_KeepsTheTorrent()
    {
        var torrent = CheckingForever();
        var engine = Substitute.For<IClientEngine>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.AddTorrentAsync(Arg.Any<TorrentFile>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>()).Returns(torrent);
        torrent.When(t => t.StartAsync(Arg.Any<CancellationToken>())).Do(_ => started.TrySetResult());
        using var gaveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var sut = new TorrentService(Engine(engine), Substitute.For<IAppMessenger>(), new HttpClient(), SeedingDefaults.Off)
        {
            StartWait = TimeSpan.FromMinutes(5)
        };

        var add = sut.AddTorrentAsync(Metadata(), new AddTorrentOptions("C:\\downloads"), gaveUp.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await gaveUp.CancelAsync();

        Assert.Same(torrent, await add);
        await engine.DidNotReceive().RemoveTorrentAsync(Arg.Any<ITorrent>(), Arg.Any<RemoveOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 10000)]
    public async Task ResumingTheSession_DoesNotWaitForFileChecks()
    {
        var engine = Substitute.For<IClientEngine>();
        engine.ResumeAsync(Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource().Task);

        await Sut(engine).ResumeSessionAsync(TestContext.Current.CancellationToken);

        await engine.Received(1).ResumeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumingTheSession_StillReportsWhatFailsStraightAway()
    {
        var engine = Substitute.For<IClientEngine>();
        var refused = new ObjectDisposedException("engine");
        engine.ResumeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(refused));

        var thrown = await Assert.ThrowsAsync<ObjectDisposedException>(() => Sut(engine).ResumeSessionAsync(TestContext.Current.CancellationToken));

        Assert.Same(refused, thrown);
    }

    [Theory]
    [InlineData(true, TorrentState.Stopped, true, true)]
    [InlineData(false, TorrentState.Stopped, true, false)]
    [InlineData(true, TorrentState.Stopped, false, false)]
    [InlineData(true, TorrentState.Active, true, false)]
    [InlineData(true, TorrentState.CheckingFiles, true, false)]
    public void IsQueued_MeansStoppedAndLeftForTheQueueToStart(bool queueManagement, TorrentState state, bool autoStart, bool expected)
    {
        var settings = SeedingDefaults.Off;
        settings.Current.Queue.EnableQueueManagement = queueManagement;
        var torrent = Substitute.For<ITorrent>();
        torrent.State.Returns(state);
        torrent.QueueAutoStart = autoStart;
        var sut = new TorrentService(Engine(Substitute.For<IClientEngine>()), Substitute.For<IAppMessenger>(), new HttpClient(), settings);

        Assert.Equal(expected, sut.IsQueued(torrent));
    }

    private static ITorrentEngineService Engine(IClientEngine engine)
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);
        return engineService;
    }

    private static TorrentFile Metadata() =>
        new TorrentFileBuilder().WithName("added.bin").AddFile("added.bin", new byte[20]).Build();

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
    /// The start the stop ends has already set auto-start, so this one is not left to the state the
    /// torrent was in before.
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
    public async Task Stop_OfARunningTorrent_KeepsTheQueueFromStartingItAgain()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.State.Returns(TorrentState.Active);
        torrent.QueueAutoStart = true;

        await Sut().StopAsync(torrent, TestContext.Current.CancellationToken);

        await torrent.Received(1).StopAsync(Arg.Any<CancellationToken>());
        Assert.False(torrent.QueueAutoStart);
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
        torrent.MoveStorageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        await torrent.Received(1).MoveStorageAsync(uniquePath, Arg.Any<CancellationToken>());
        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
        messenger.Received(1).Publish(Arg.Is<TorrentAlertMessage>(message => ReferenceEquals(message.Torrent, torrent)));
    }

    /// <summary>
    /// Moving a torrent into its own folder needs it stopped for a moment, and the queue must not
    /// start it again in that moment. It wants to run again once this is over.
    /// </summary>
    [Fact]
    public async Task PublishAlert_MetadataInitialized_KeepsTheQueueOffTheTorrentWhileItMoves()
    {
        var (sut, torrent, moved) = MovingTorrent();
        torrent.QueueAutoStart = true;
        bool? queueWouldTakeIt = null;
        torrent.When(t => t.StopAsync(Arg.Any<CancellationToken>())).Do(_ => queueWouldTakeIt = torrent.QueueAutoStart);

        sut.PublishAlert(new SimpleMetadataAlert { Id = AlertId.MetadataInitialized, Torrent = torrent }, TestContext.Current.CancellationToken);
        await moved.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(queueWouldTakeIt);
    }

    [Fact]
    public async Task PublishAlert_MetadataInitialized_RestartsTheTorrentEvenIfItCouldNotMove()
    {
        var (sut, torrent, _) = MovingTorrent();
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        torrent.MoveStorageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Torrent must be stopped.")));
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            restarted.TrySetResult();
            return Task.CompletedTask;
        });

        sut.PublishAlert(new SimpleMetadataAlert { Id = AlertId.MetadataInitialized, Torrent = torrent }, TestContext.Current.CancellationToken);

        await restarted.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PublishAlert_MetadataInitialized_ReportsARestartThatFails()
    {
        var messenger = Substitute.For<IAppMessenger>();
        var (sut, torrent, _) = MovingTorrent(messenger);
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        messenger.When(m => m.Publish(Arg.Any<TorrentStartFailedMessage>())).Do(_ => reported.TrySetResult());
        torrent.Started.Returns(true, false);
        torrent.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("The disk went away.")));

        sut.PublishAlert(new SimpleMetadataAlert { Id = AlertId.MetadataInitialized, Torrent = torrent }, TestContext.Current.CancellationToken);

        await reported.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The folder would have the name of the file already in the root, so the restart could not make
    /// it, and every single-file magnet added with no folder stopped itself partway through.
    /// </summary>
    [Fact]
    public async Task PublishAlert_MetadataInitialized_LeavesASingleFileTorrentRunningWhereItIs()
    {
        var (sut, torrent, _) = MovingTorrent();
        torrent.GetAllFileInfo().Returns([new TorrentFileInfo("plain.bin", 20, 0)]);

        sut.PublishAlert(new SimpleMetadataAlert { Id = AlertId.MetadataInitialized, Torrent = torrent }, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        await torrent.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
        await torrent.DidNotReceive().MoveStorageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The name is the torrent maker's to choose, and must not choose where files go.</summary>
    [Fact]
    public async Task PublishAlert_MetadataInitialized_KeepsAHostileNameInsideTheDownloadFolder()
    {
        var (sut, torrent, moved) = MovingTorrent();
        torrent.Name.Returns("..\\..\\Startup");

        sut.PublishAlert(new SimpleMetadataAlert { Id = AlertId.MetadataInitialized, Torrent = torrent }, TestContext.Current.CancellationToken);
        var path = await moved.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "peerfluence-default-root"), Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    [Fact]
    public async Task Add_WithNoFolderChosen_KeepsAHostileNameInsideTheDownloadFolder()
    {
        var engine = Substitute.For<IClientEngine>();
        var defaultRoot = Path.Combine(Path.GetTempPath(), "peerfluence-default-root");
        engine.Settings.Returns(new Settings { Files = { DefaultDownloadPath = defaultRoot } });
        AddTorrentOptions? used = null;
        engine.AddTorrentAsync(Arg.Any<TorrentFile>(), Arg.Do<AddTorrentOptions>(o => used = o), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<ITorrent>());
        var hostile = new TorrentFileBuilder().WithName("..\\..\\Startup").AddFile("file.bin", new byte[20]).Build();

        await Sut(engine).AddTorrentAsync(hostile, new AddTorrentOptions { StartImmediately = false }, TestContext.Current.CancellationToken);

        Assert.Equal(defaultRoot, Path.GetDirectoryName(Path.GetFullPath(used!.DownloadPath!)));
    }

    private static (TorrentService Sut, ITorrent Torrent, TaskCompletionSource<string> Moved) MovingTorrent(IAppMessenger? messenger = null)
    {
        var defaultRoot = Path.Combine(Path.GetTempPath(), "peerfluence-default-root");
        var engine = Substitute.For<IClientEngine>();
        engine.Settings.Returns(new Settings { Files = { DefaultDownloadPath = defaultRoot } });
        var files = Substitute.For<IFiles>();
        files.DownloadPath.Returns(defaultRoot);
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Ubuntu ISO");
        torrent.Files.Returns(files);
        torrent.Started.Returns(true);
        engine.GetTorrents().Returns([torrent]);
        var moved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        torrent.MoveStorageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            moved.TrySetResult(callInfo.Arg<string>());
            return Task.CompletedTask;
        });

        return (new TorrentService(Engine(engine), messenger ?? Substitute.For<IAppMessenger>(), new HttpClient(), SeedingDefaults.Off), torrent, moved);
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
