using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Core.Services.Rpc;
using PeerSharp.Config;
using PeerSharp.Core;

namespace Peerfluence.Tests.Services;

public sealed class PeerSharpIntegrationTests
{
    [Fact]
    public async Task SslTorrent_RestoresStoppedAndReattachesCertificateWithoutLosingProgressOrOptions()
    {
        using var data = new SslTorrentTestData();
        using var other = new SslTorrentTestData();
        var directory = Path.Combine(Path.GetTempPath(), $"peerfluence-ssl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings
        {
            Storage = { DownloadPath = directory, SessionPath = Path.Combine(directory, "session"), EnableSessionPersistence = true },
            Network = { EnableDht = false, EnableNatPmp = false, EnableUpnp = false, UseAutomaticListeningPort = true }
        });
        using var http = new HttpClient();
        var ordinary = new TorrentFileBuilder().WithName("ordinary.bin").AddFile("ordinary.bin", new byte[20]).Build();
        var stopped = new TorrentFileBuilder().WithName("stopped.bin").AddFile("stopped.bin", new byte[30]).Build();
        try
        {
            await using (var first = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System))
            {
                await first.InitializeAsync(TestContext.Current.CancellationToken);
                var torrent = await first.Engine.AddTorrentAsync(data.Metadata, new AddTorrentOptions
                {
                    DownloadPath = directory,
                    SslCertificate = data.Certificate,
                    StartImmediately = false,
                    DownloadLimitBytesPerSecond = 12345,
                    UploadLimitBytesPerSecond = 23456,
                    QueuePriority = 9,
                    RatioLimit = 3,
                    SeedTimeLimit = TimeSpan.FromMinutes(17),
                    FileSelections = [new FileSelection(true, Priority.High)]
                }, TestContext.Current.CancellationToken);
                await File.WriteAllBytesAsync(Path.Combine(directory, SslTorrentTestData.Name), data.Content, TestContext.Current.CancellationToken);
                await torrent.ForceRecheckAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.Equal(1024UL, torrent.FinishedBytes);
                await torrent.RenameFileAsync(0, "renamed.bin", TestContext.Current.CancellationToken);
                await torrent.StartAsync(TestContext.Current.CancellationToken);
                await first.Engine.AddTorrentAsync(ordinary, new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
                await first.Engine.AddTorrentAsync(stopped, new AddTorrentOptions(directory) { StartImmediately = false }, TestContext.Current.CancellationToken);
                await first.Engine.SaveSessionAsync(TestContext.Current.CancellationToken);
            }

            await using var restored = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
            await restored.InitializeAsync(TestContext.Current.CancellationToken);
            var ssl = Assert.Single(restored.Engine.GetTorrents(), t => t.Name == SslTorrentTestData.Name);
            Assert.False(ssl.Started);
            Assert.False(ssl.QueueAutoStart);
            Assert.True(Assert.Single(restored.Engine.GetTorrents(), t => t.Name == "ordinary.bin").Started);
            Assert.False(Assert.Single(restored.Engine.GetTorrents(), t => t.Name == "stopped.bin").Started);
            ssl.DiskReadLimitBytesPerSecond = 34567;
            ssl.DiskWriteLimitBytesPerSecond = 45678;
            ssl.MaxConnections = 7;
            ssl.MaxUploadSlots = 2;
            ssl.Trackers.AddTracker("https://tracker.invalid/announce");
            Assert.True(ssl.WebSeeds.Add("https://seed.invalid/files/"));
            var service = new TorrentService(restored, Substitute.For<IAppMessenger>(), http, settings);
            // Refused where it is given, and the torrent is left exactly as it was.
            await Assert.ThrowsAsync<ArgumentException>(() => service.SetSslCertificateAsync(ssl, other.Certificate, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => ssl.StartAsync(TestContext.Current.CancellationToken));
            ssl.QueueAutoStart = false;
            Assert.Contains(ssl, service.GetTorrents());
            using (var credential = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                data.Certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12), null,
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable))
            {
                await service.SetSslCertificateAsync(ssl, credential, TestContext.Current.CancellationToken);
            }
            var attached = Assert.Single(service.GetTorrents(), t => t.Name == SslTorrentTestData.Name);
            Assert.Same(ssl, attached);
            Assert.False(attached.Started);
            await attached.StartAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1024UL, attached.FinishedBytes);
            Assert.Equal(data.Content, await File.ReadAllBytesAsync(Path.Combine(directory, "renamed.bin"), TestContext.Current.CancellationToken));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, restored.Engine.BoundTcpPort, deadline.Token);
            using var peerCertificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                data.Certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12), null);
            await using var tls = new System.Net.Security.SslStream(client.GetStream(), false,
                (_, remote, _, _) => remote != null && remote.GetRawCertData().AsSpan().SequenceEqual(data.Certificate.RawData));
            await tls.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions
            {
                TargetHost = Convert.ToHexStringLower(attached.Hash.Span),
                ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection { peerCertificate },
                LocalCertificateSelectionCallback = (_, _, _, _, _) => peerCertificate
            }, deadline.Token);
            var handshake = new byte[68];
            handshake[0] = 19;
            "BitTorrent protocol"u8.CopyTo(handshake.AsSpan(1));
            attached.Hash.Span.CopyTo(handshake.AsSpan(28));
            System.Security.Cryptography.RandomNumberGenerator.Fill(handshake.AsSpan(48));
            await tls.WriteAsync(handshake, deadline.Token);
            var reply = new byte[68];
            await tls.ReadExactlyAsync(reply, deadline.Token);
            Assert.Equal(handshake[..20], reply[..20]);
            Assert.Equal(handshake[28..48], reply[28..48]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Adding a .torrent for files already on disk is the usual way to start seeding them. The real
    /// engine, because what matters is what its check does with real files.
    /// </summary>
    [Fact]
    public async Task AddingATorrentOverItsOwnFiles_ChecksThemRatherThanDownloadingThemAgain()
    {
        var (directory, settings) = ExistingFilesProfile();
        Directory.CreateDirectory(directory);
        var content = new byte[1024 * 1024];
        Random.Shared.NextBytes(content);
        await File.WriteAllBytesAsync(Path.Combine(directory, "existing.bin"), content, TestContext.Current.CancellationToken);
        var metadata = new TorrentFileBuilder().WithName("existing.bin").WithPieceLength(16 * 1024).AddFile("existing.bin", content).Build();
        var engine = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
        try
        {
            await engine.InitializeAsync(TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var service = new TorrentService(engine, Substitute.For<IAppMessenger>(), http, settings);

            var torrent = await service.AddTorrentAsync(metadata, new AddTorrentOptions(directory), TestContext.Current.CancellationToken);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!torrent.Finished)
            {
                await Task.Delay(10, deadline.Token);
            }

            Assert.Equal((ulong)content.Length, torrent.FinishedBytes);
        }
        finally
        {
            await engine.ShutdownAsync(TestContext.Current.CancellationToken);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Whichever way this races with the check, a stop that returns has to leave the torrent stopped,
    /// with the engine still willing to start it again.
    /// </summary>
    [Fact]
    public async Task StoppingATorrentCheckingItsFiles_LeavesItStopped()
    {
        var (directory, settings) = ExistingFilesProfile();
        Directory.CreateDirectory(directory);
        var content = new byte[64 * 1024 * 1024];
        await File.WriteAllBytesAsync(Path.Combine(directory, "large.bin"), content, TestContext.Current.CancellationToken);
        var metadata = new TorrentFileBuilder().WithName("large.bin").WithPieceLength(16 * 1024).AddFile("large.bin", content).Build();
        var engine = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
        try
        {
            await engine.InitializeAsync(TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var service = new TorrentService(engine, Substitute.For<IAppMessenger>(), http, settings)
            {
                StartWait = TimeSpan.FromMilliseconds(1)
            };
            var torrent = await service.AddTorrentAsync(metadata, new AddTorrentOptions(directory), TestContext.Current.CancellationToken);

            await service.StopAsync(torrent, TestContext.Current.CancellationToken);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (torrent.State != PeerSharp.Interfaces.TorrentState.Stopped)
            {
                await Task.Delay(5, deadline.Token);
            }

            Assert.False(torrent.Started);
            await service.StartAsync(torrent, TestContext.Current.CancellationToken);
            await service.StopAsync(torrent, TestContext.Current.CancellationToken);
        }
        finally
        {
            await engine.ShutdownAsync(TestContext.Current.CancellationToken);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// With queue management on, the engine looks for torrents to start every few seconds, and takes
    /// any that are stopped, unfinished and set to auto-start. A stop on its own leaves that set, so
    /// this has to outlast a queue tick to mean anything.
    /// </summary>
    [Fact]
    public async Task StoppingARunningTorrent_IsNotUndoneByTheQueue()
    {
        var (directory, settings) = ExistingFilesProfile();
        settings.Current.Queue.EnableQueueManagement = true;
        var metadata = new TorrentFileBuilder().WithName("queued.bin").WithPieceLength(16 * 1024).AddFile("queued.bin", new byte[20]).Build();
        var engine = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
        try
        {
            await engine.InitializeAsync(TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var service = new TorrentService(engine, Substitute.For<IAppMessenger>(), http, settings);
            var torrent = await service.AddTorrentAsync(metadata, new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
            Assert.True(torrent.Started);

            await service.StopAsync(torrent, TestContext.Current.CancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(engine.Engine.Settings.Queue.RecheckIntervalSeconds + 2), TestContext.Current.CancellationToken);
            Assert.False(torrent.Started);

            // Asking for it again hands it back to the queue.
            await service.StartAsync(torrent, TestContext.Current.CancellationToken);
            Assert.True(torrent.Started);
            Assert.True(torrent.QueueAutoStart);
        }
        finally
        {
            await engine.ShutdownAsync(TestContext.Current.CancellationToken);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// This service pauses the engine itself while it starts up, and resumes it afterwards, which must
    /// not be mistaken for the session having been paused, or for leave to end a pause that was.
    /// </summary>
    [Fact]
    public async Task ASessionLeftPaused_ComesBackPaused_AndAnotherComesBackRunning()
    {
        var (directory, settings) = ExistingFilesProfile();
        settings.Current.Storage.EnableSessionPersistence = true;
        settings.Current.Storage.SessionPath = Path.Combine(directory, "session");
        var metadata = new TorrentFileBuilder().WithName("left.bin").WithPieceLength(16 * 1024).AddFile("left.bin", new byte[20]).Build();
        using var http = new HttpClient();
        try
        {
            await using (var first = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System))
            {
                await first.InitializeAsync(TestContext.Current.CancellationToken);
                var service = new TorrentService(first, Substitute.For<IAppMessenger>(), http, settings);
                await service.AddTorrentAsync(metadata, new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
                await service.PauseSessionAsync(TestContext.Current.CancellationToken);
            }

            await using (var paused = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System))
            {
                await paused.InitializeAsync(TestContext.Current.CancellationToken);
                var service = new TorrentService(paused, Substitute.For<IAppMessenger>(), http, settings);
                Assert.True(service.IsSessionPaused);
                var held = Assert.Single(service.GetTorrents());
                Assert.False(held.Started);

                await service.ResumeSessionAsync(TestContext.Current.CancellationToken);

                Assert.False(service.IsSessionPaused);
                Assert.True(held.Started);
            }

            await using var running = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
            await running.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.False(running.Engine.IsPaused);
            Assert.True(Assert.Single(running.Engine.GetTorrents()).Started);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The pause lives on the session, not on its torrents, so a session with none left still has one.
    /// </summary>
    [Fact]
    public async Task AnEmptySessionLeftPaused_ComesBackPaused_UntilItIsResumed()
    {
        var (directory, settings) = ExistingFilesProfile();
        settings.Current.Storage.EnableSessionPersistence = true;
        settings.Current.Storage.SessionPath = Path.Combine(directory, "session");
        using var http = new HttpClient();
        try
        {
            await using (var first = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System))
            {
                await first.InitializeAsync(TestContext.Current.CancellationToken);
                var service = new TorrentService(first, Substitute.For<IAppMessenger>(), http, settings);
                await service.PauseSessionAsync(TestContext.Current.CancellationToken);
            }

            await using (var paused = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System))
            {
                await paused.InitializeAsync(TestContext.Current.CancellationToken);
                var service = new TorrentService(paused, Substitute.For<IAppMessenger>(), http, settings);
                Assert.True(service.IsSessionPaused);
                await service.ResumeSessionAsync(TestContext.Current.CancellationToken);
            }

            await using var running = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
            await running.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.False(running.Engine.IsPaused);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The downloads list is in the order torrents were added, which only holds across a restart if
    /// the engine brings back when each one was added rather than when it was loaded.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task EachTorrent_KeepsWhenItWasAdded_AcrossARestart()
    {
        var (directory, settings) = SessionProfile();
        using var http = new HttpClient();
        try
        {
            var added = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            await using (var first = await StartedEngineAsync(settings))
            {
                var service = Service(first, http, settings);
                foreach (var name in new[] { "older.bin", "newer.bin" })
                {
                    // The time added is kept to the second, so wait for the next one between adds.
                    var second = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await UntilAsync(() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() > second);
                    var torrent = await service.AddTorrentAsync(Small(name), new AddTorrentOptions(directory) { StartImmediately = false }, TestContext.Current.CancellationToken);
                    added[name] = torrent.TimeAdded;
                }
            }

            Assert.True(added["older.bin"] < added["newer.bin"]);

            await using var restarted = await StartedEngineAsync(settings);
            var restored = restarted.Engine.GetTorrents().ToDictionary(torrent => torrent.Name, torrent => torrent.TimeAdded, StringComparer.Ordinal);
            Assert.Equal(added, restored);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The Transmission client's "start all", with one of them already running.</summary>
    [Fact]
    public async Task StartingARunningTorrent_IsNotAFailure()
    {
        var (directory, settings) = ExistingFilesProfile();
        var metadata = new TorrentFileBuilder().WithName("running.bin").WithPieceLength(16 * 1024).AddFile("running.bin", new byte[20]).Build();
        var engine = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
        try
        {
            await engine.InitializeAsync(TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var service = new TorrentService(engine, Substitute.For<IAppMessenger>(), http, settings);
            var torrent = await service.AddTorrentAsync(metadata, new AddTorrentOptions(directory), TestContext.Current.CancellationToken);

            await service.StartAsync(torrent, TestContext.Current.CancellationToken);

            Assert.True(torrent.Started);
        }
        finally
        {
            await engine.ShutdownAsync(TestContext.Current.CancellationToken);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Renaming needs a stopped torrent, so a restart has to carry the new name through that state, or
    /// the torrent goes looking for the old one and downloads everything again.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task ARenameOfAStoppedTorrent_SurvivesARestart_AndKeepsItsProgress()
    {
        var (directory, settings) = SessionProfile();
        byte[] payload = new byte[32 * 1024];
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(Path.Combine(directory, "named.bin"), payload, TestContext.Current.CancellationToken);
        var metadata = new TorrentFileBuilder().WithName("named.bin").WithPieceLength(16 * 1024).AddFile("named.bin", payload).Build();
        using var http = new HttpClient();
        try
        {
            await using (var first = await StartedEngineAsync(settings))
            {
                var service = Service(first, http, settings);
                var torrent = await service.AddTorrentAsync(metadata, new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
                await UntilAsync(() => torrent.Started);
                Assert.Equal(1.0f, torrent.Progress);

                await service.StopAsync(torrent, TestContext.Current.CancellationToken);
                await torrent.RenameFileAsync(0, "renamed.bin", TestContext.Current.CancellationToken);
            }

            await using var restored = await StartedEngineAsync(settings);
            var back = Assert.Single(restored.Engine.GetTorrents());
            await Service(restored, http, settings).StartAsync(back, TestContext.Current.CancellationToken);
            await UntilAsync(() => back.Started);

            Assert.Equal(1.0f, back.Progress);
            Assert.Equal("renamed.bin", back.GetRenamedFiles()[0]);
            Assert.False(File.Exists(Path.Combine(directory, "named.bin")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The add dialog, with some files unticked and the start box cleared.</summary>
    [Fact(Timeout = 30000)]
    public async Task FilesLeftOutWhenAddingWithoutStarting_StayLeftOutAfterARestart()
    {
        var (directory, settings) = SessionProfile();
        var metadata = new TorrentFileBuilder().WithName("two").WithPieceLength(16 * 1024)
            .AddFile("wanted.bin", new byte[20]).AddFile("unwanted.bin", new byte[20]).Build();
        using var http = new HttpClient();
        try
        {
            await using (var first = await StartedEngineAsync(settings))
            {
                await Service(first, http, settings).AddTorrentAsync(metadata, new AddTorrentOptions(directory)
                {
                    StartImmediately = false,
                    FileSelections = [new FileSelection(true), new FileSelection(false)]
                }, TestContext.Current.CancellationToken);
            }

            await using var restored = await StartedEngineAsync(settings);
            var back = Assert.Single(restored.Engine.GetTorrents());

            Assert.True(back.GetFileSelection(0).Selected);
            Assert.False(back.GetFileSelection(1).Selected);
            Assert.False(back.Started);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// This service turns the queue off while it starts up and on again afterwards, so this is the
    /// restored queue as the application actually gets it. Also, stopping one in the queue is what
    /// takes it out, and that has to last too.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task ATorrentWaitingInTheQueue_WaitsAcrossARestart_UntilThereIsRoomOrItIsStopped()
    {
        var (directory, settings) = SessionProfile();
        settings.Current.Queue.EnableQueueManagement = true;
        settings.Current.Queue.MaxActiveDownloads = 1;
        using var http = new HttpClient();
        try
        {
            await using (var first = await StartedEngineAsync(settings))
            {
                var service = Service(first, http, settings);
                var a = await service.AddTorrentAsync(Small("a.bin"), new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
                var b = await service.AddTorrentAsync(Small("b.bin"), new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
                var c = await service.AddTorrentAsync(Small("c.bin"), new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
                await UntilAsync(() => new[] { a, b, c }.Count(t => t.Started) == 1);
                Assert.Equal(2, new[] { a, b, c }.Count(service.IsQueued));
            }

            await using var restored = await StartedEngineAsync(settings);
            var service2 = Service(restored, http, settings);
            var torrents = restored.Engine.GetTorrents();
            var running = Assert.Single(torrents, t => t.Started);
            var queued = torrents.Where(service2.IsQueued).ToList();
            Assert.Equal(2, queued.Count);

            await service2.StopAsync(queued[0], TestContext.Current.CancellationToken);
            Assert.False(service2.IsQueued(queued[0]));

            await service2.StopAsync(running, TestContext.Current.CancellationToken);
            await UntilAsync(() => queued[1].Started);

            // Past another pass, so the one taken out has had every chance to be picked up.
            await Task.Delay(TimeSpan.FromSeconds(restored.Engine.Settings.Queue.RecheckIntervalSeconds + 1), TestContext.Current.CancellationToken);
            Assert.False(queued[0].Started);
            Assert.False(running.Started);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The way pieces used to go missing: a pause stops and starts a torrent that a restart restored,
    /// and the start took the torrent's own writes for someone else's changes.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task PausingAndResumingAfterARestart_KeepsTheFilesTheTorrentChangedItself()
    {
        var (directory, settings) = SessionProfile();
        byte[] payload = new byte[32 * 1024];
        Random.Shared.NextBytes(payload);
        string path = Path.Combine(directory, "written.bin");
        await File.WriteAllBytesAsync(path, payload, TestContext.Current.CancellationToken);
        var metadata = new TorrentFileBuilder().WithName("written.bin").WithPieceLength(16 * 1024).AddFile("written.bin", payload).Build();
        using var http = new HttpClient();
        try
        {
            await using (var first = await StartedEngineAsync(settings))
            {
                var torrent = await Service(first, http, settings).AddTorrentAsync(metadata, new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
                await UntilAsync(() => torrent.Started);
            }

            await using var restored = await StartedEngineAsync(settings);
            var service = Service(restored, http, settings);
            var back = Assert.Single(restored.Engine.GetTorrents());
            Assert.True(back.Started);

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
            await service.PauseSessionAsync(TestContext.Current.CancellationToken);
            await service.ResumeSessionAsync(TestContext.Current.CancellationToken);
            await UntilAsync(() => back.Started);

            Assert.Equal(1.0f, back.Progress);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A Transmission client's "start all", with one running: all of it has to happen.</summary>
    [Fact(Timeout = 30000)]
    public async Task ATransmissionClientStartingEverything_IsNotStoppedByOneAlreadyRunning()
    {
        var (directory, settings) = ExistingFilesProfile();
        using var http = new HttpClient();
        var engine = await StartedEngineAsync(settings);
        try
        {
            var service = Service(engine, http, settings);
            var running = await service.AddTorrentAsync(Small("running.bin"), new AddTorrentOptions(directory), TestContext.Current.CancellationToken);
            var stopped = await service.AddTorrentAsync(Small("stopped.bin"), new AddTorrentOptions(directory) { StartImmediately = false }, TestContext.Current.CancellationToken);
            var rpc = new TransmissionRpcHandler(service, settings, Substitute.For<ITorrentCategoryService>(), "test");

            string response = await rpc.HandleAsync("""{"method":"torrent-start","arguments":{}}""", TestContext.Current.CancellationToken);

            Assert.Contains("\"success\"", response);
            await UntilAsync(() => stopped.Started);
            Assert.True(running.Started);
        }
        finally
        {
            await engine.ShutdownAsync(TestContext.Current.CancellationToken);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static TorrentFile Small(string name) =>
        new TorrentFileBuilder().WithName(name).WithPieceLength(16 * 1024).AddFile(name, new byte[20]).Build();

    private static TorrentService Service(TorrentEngineService engine, HttpClient http, IAppSettingsService settings) =>
        new(engine, Substitute.For<IAppMessenger>(), http, settings);

    private static async Task<TorrentEngineService> StartedEngineAsync(IAppSettingsService settings)
    {
        var engine = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        return engine;
    }

    private static (string Directory, IAppSettingsService Settings) SessionProfile()
    {
        var (directory, settings) = ExistingFilesProfile();
        Directory.CreateDirectory(directory);
        settings.Current.Storage.EnableSessionPersistence = true;
        settings.Current.Storage.SessionPath = Path.Combine(directory, "session");
        return (directory, settings);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        while (!condition())
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static (string Directory, IAppSettingsService Settings) ExistingFilesProfile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"peerfluence-peersharp-{Guid.NewGuid():N}");
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings
        {
            Storage = { DownloadPath = directory, EnableSessionPersistence = false },
            Network = { EnableDht = false, EnableNatPmp = false, EnableUpnp = false, UseAutomaticListeningPort = true },
        });
        return (directory, settings);
    }

    [Fact]
    public async Task AddingATorrent_AppliesSelectionsTrackersAndWebSeedsBeforeStarting()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"peerfluence-peersharp-{Guid.NewGuid():N}");
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings
        {
            Storage = { DownloadPath = directory, EnableSessionPersistence = false },
            Network = { EnableDht = false, EnableNatPmp = false, EnableUpnp = false, UseAutomaticListeningPort = true },
        });
        var engine = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
        try
        {
            await engine.InitializeAsync(TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var service = new TorrentService(engine, Substitute.For<IAppMessenger>(), http, settings);
            var metadata = new TorrentFileBuilder().WithName("Selected files").WithPieceLength(16 * 1024)
                .AddFile("skip.bin", new byte[10]).AddFile("keep.bin", new byte[20]).Build();
            var torrent = await service.AddTorrentAsync(metadata, new AddTorrentOptions
            {
                DownloadPath = directory,
                StartImmediately = false,
                FileSelections = [new FileSelection(false, Priority.DoNotDownload), new FileSelection(true, Priority.High)],
                AdditionalTrackers = ["https://tracker.invalid/announce"],
                AdditionalWebSeeds = ["https://seed.invalid/files/"],
            }, TestContext.Current.CancellationToken);

            Assert.False(torrent.Started);
            var selection = torrent.GetAllFileSelections();
            Assert.False(selection[0].Selected);
            Assert.True(selection[1].Selected);
            Assert.Equal(Priority.High, selection[1].Priority);
            Assert.Contains(torrent.Trackers.GetTrackers(), tracker => tracker.Url == "https://tracker.invalid/announce");
            Assert.Contains("https://seed.invalid/files/", torrent.WebSeeds.GetAll());
        }
        finally
        {
            await engine.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
