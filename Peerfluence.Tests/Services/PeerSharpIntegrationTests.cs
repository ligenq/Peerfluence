using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
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
            var rejected = await service.SetSslCertificateAsync(ssl, other.Certificate, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => rejected.StartAsync(TestContext.Current.CancellationToken));
            rejected.QueueAutoStart = false;
            Assert.Contains(rejected, service.GetTorrents());
            PeerSharp.Interfaces.ITorrent attached;
            using (var credential = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                data.Certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12), null,
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable))
            {
                attached = await service.SetSslCertificateAsync(rejected, credential, TestContext.Current.CancellationToken);
            }
            Assert.False(attached.Started);
            Assert.Equal(12345, attached.DownloadLimitBytesPerSecond);
            Assert.Equal(23456, attached.UploadLimitBytesPerSecond);
            Assert.Equal(34567, attached.DiskReadLimitBytesPerSecond);
            Assert.Equal(45678, attached.DiskWriteLimitBytesPerSecond);
            Assert.Equal(7, attached.MaxConnections);
            Assert.Equal(2, attached.MaxUploadSlots);
            Assert.Equal(9, attached.QueuePriority);
            Assert.Equal(3, attached.RatioLimit);
            Assert.Equal(TimeSpan.FromMinutes(17), attached.SeedTimeLimit);
            Assert.Equal(Priority.High, attached.GetAllFileSelections()[0].Priority);
            Assert.Equal("renamed.bin", attached.GetRenamedFiles()[0]);
            Assert.Contains(attached.Trackers.GetTrackers(), t => t.Url == "https://tracker.invalid/announce");
            Assert.Contains("https://seed.invalid/files/", attached.WebSeeds.GetAll());
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
