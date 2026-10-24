using Microsoft.Extensions.Logging;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using PeerSharp.Config;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

public class TorrentEngineServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task I2pSettings_MapToEngineAtStartupOnly(bool enabled, bool allowMixed)
    {
        var settings = BaseSettings(s =>
        {
            s.Network.EnableI2p = enabled;
            s.Network.I2pSamHost = "  localhost  ";
            s.Network.I2pSamPort = 7666;
            s.Network.I2pAllowMixedNetworks = allowMixed;
        });
        await using var sut = CreateSut(settings);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(enabled ? "localhost" : null, sut.Engine.Settings.Connection.I2pSamHost);
        Assert.Equal(7666, sut.Engine.Settings.Connection.I2pSamPort);
        Assert.Equal(enabled && allowMixed, sut.Engine.Settings.Connection.I2pAllowMixedNetworks);
        settings.Network.I2pSamPort = 7777;
        sut.ApplyEncryptionSettings();
        Assert.Equal(7666, sut.Engine.Settings.Connection.I2pSamPort);
    }

    [Theory]
    [InlineData("Inherit", "Inherit", null, null)]
    [InlineData("Allow", "Refuse", PeerSharp.Config.Encryption.Allow, PeerSharp.Config.Encryption.Refuse)]
    [InlineData("invalid", "", null, null)]
    public async Task EncryptionPolicies_PreserveInheritanceAndApplyWithoutRestarting(
        string incoming, string outgoing, PeerSharp.Config.Encryption? expectedIncoming, PeerSharp.Config.Encryption? expectedOutgoing)
    {
        var settings = BaseSettings(s =>
        {
            s.EncryptionMode = "Require";
            s.Network.IncomingEncryptionMode = incoming;
            s.Network.OutgoingEncryptionMode = outgoing;
        });
        var sut = CreateSut(settings);
        try
        {
            sut.ApplyEncryptionSettings(); // Safe before initialization too.
            await sut.InitializeAsync(TestContext.Current.CancellationToken);
            var connection = sut.Engine.Settings.Connection;
            Assert.Equal(PeerSharp.Config.Encryption.Require, connection.Encryption);
            Assert.Equal(expectedIncoming, connection.IncomingEncryption);
            Assert.Equal(expectedOutgoing, connection.OutgoingEncryption);
            Assert.True(connection.PreferRc4);

            settings.Network.IncomingEncryptionMode = "Require";
            settings.Network.OutgoingEncryptionMode = "Allow";
            settings.Network.PreferRc4 = false;
            sut.ApplyEncryptionSettings();
            Assert.Same(connection, sut.Engine.Settings.Connection);
            Assert.Equal(PeerSharp.Config.Encryption.Require, connection.IncomingEncryption);
            Assert.Equal(PeerSharp.Config.Encryption.Allow, connection.OutgoingEncryption);
            Assert.False(connection.PreferRc4);

            settings.Network.IncomingEncryptionMode = "Inherit";
            settings.Network.OutgoingEncryptionMode = "Inherit";
            sut.ApplyEncryptionSettings();
            Assert.Null(connection.IncomingEncryption);
            Assert.Null(connection.OutgoingEncryption);
            Assert.Equal(PeerSharp.Config.Encryption.Require, connection.Encryption);
        }
        finally { await sut.DisposeAsync(); }
    }

    [Fact]
    public async Task StreamingPolicy_IsAppliedAtStartupAndUpdatedForFutureStreams()
    {
        var settings = BaseSettings(s =>
        {
            s.Streaming.ReadAheadBytes = 80L * 1024 * 1024;
            s.Streaming.ReadAheadSeconds = 45;
            s.Streaming.RequestQueueSecondsWhileBuffering = 2;
            s.Streaming.DataWaitTimeoutSeconds = 0;
            s.Streaming.MetadataWaitTimeoutSeconds = 120;
            s.Queue.MaxActiveDownloads = -1;
            s.Queue.MaxActiveSeeds = -2;
        });
        var sut = CreateSut(settings);
        try
        {
            await sut.InitializeAsync(TestContext.Current.CancellationToken);
            var policy = sut.Engine.Settings.Streaming;
            Assert.Equal(80L * 1024 * 1024, policy.ReadAheadBytes);
            Assert.Equal(45, policy.ReadAheadSeconds);
            Assert.Equal(2, policy.RequestQueueSecondsWhileBuffering);
            Assert.Equal(0, policy.DataWaitTimeoutSeconds);
            Assert.Equal(120, policy.MetadataWaitTimeoutSeconds);
            Assert.Equal(0, sut.Engine.Settings.Queue.MaxActiveDownloads);
            Assert.Equal(0, sut.Engine.Settings.Queue.MaxActiveSeeds);
            settings.Streaming.ReadAheadBytes = -1;
            settings.Streaming.ReadAheadSeconds = 0;
            settings.Streaming.RequestQueueSecondsWhileBuffering = 0;
            settings.Streaming.DataWaitTimeoutSeconds = 15;
            settings.Streaming.MetadataWaitTimeoutSeconds = 0;
            sut.ApplyStreamingSettings();
            Assert.Same(policy, sut.Engine.Settings.Streaming);
            Assert.Equal(1024 * 1024, policy.ReadAheadBytes);
            Assert.Equal(0, policy.ReadAheadSeconds);
            Assert.Equal(0, policy.RequestQueueSecondsWhileBuffering);
            Assert.Equal(15, policy.DataWaitTimeoutSeconds);
            Assert.Equal(0, policy.MetadataWaitTimeoutSeconds);
        }
        finally { await sut.DisposeAsync(); }
    }

    [Fact]
    public async Task InitializeAsync_WhenAutomaticListeningPortIsEnabled_BindsRealPorts()
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings
        {
            Storage =
            {
                DownloadPath = Path.Combine(Path.GetTempPath(), $"peerfluence-engine-test-{Guid.NewGuid():N}"),
                EnableSessionPersistence = false
            },
            Network =
            {
                EnableDht = false,
                EnableNatPmp = false,
                EnableUpnp = false,
                UseAutomaticListeningPort = true,
                ListeningPort = 55125
            }
        });
        var sut = new TorrentEngineService(settingsService, Substitute.For<ILoggerFactory>(), TimeProvider.System);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(sut.Engine.Settings.Connection.TcpPort > 0);
        Assert.True(sut.Engine.Settings.Connection.UdpPort > 0);
        Assert.NotEqual(55125, sut.Engine.Settings.Connection.TcpPort);

        await sut.DisposeAsync();
    }

    private static AppSettings BaseSettings(Action<AppSettings>? configure = null)
    {
        var settings = new AppSettings
        {
            Storage =
            {
                DownloadPath = Path.Combine(Path.GetTempPath(), $"peerfluence-engine-test-{Guid.NewGuid():N}"),
                EnableSessionPersistence = false
            },
            Network =
            {
                EnableDht = false,
                EnableNatPmp = false,
                EnableUpnp = false,
                UseAutomaticListeningPort = true
            }
        };

        configure?.Invoke(settings);
        return settings;
    }

    /// <summary>
    /// The blocklist is consulted as a peer is added or connects, never against connections already
    /// open, so it has to be in place before restored torrents are allowed to start.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_LoadsTheBlocklistBeforeRestoredTorrentsResume()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var blocklistPath = Path.Combine(Path.GetTempPath(), $"peerfluence-blocklist-{Guid.NewGuid():N}.p2p");
        await File.WriteAllTextAsync(blocklistPath, "Example:10.0.0.0-10.0.0.255\n", cancellationToken);
        try
        {
            var engine = SubstituteEngine();
            await using var sut = CreateSut(
                BaseSettings(s =>
                {
                    s.EnableBlocklist = true;
                    s.BlocklistPath = blocklistPath;
                }),
                engine);
            await sut.InitializeAsync(cancellationToken);

            Received.InOrder(() =>
            {
                _ = engine.PauseAsync(Arg.Any<CancellationToken>());
                _ = engine.InitializeAsync(Arg.Any<CancellationToken>());
                _ = engine.LoadBlocklistAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());
                _ = engine.ResumeAsync(Arg.Any<CancellationToken>());
            });
        }
        finally
        {
            File.Delete(blocklistPath);
        }
    }

    /// <summary>
    /// Asking whether a torrent is SSL serializes its whole metadata, so startup asks only the
    /// torrents whose answer changes something: the ones the queue would otherwise retry.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_StopsOnlyAutoStartingSslTorrentsRetrying_AndLeavesTheRestUnexamined()
    {
        using var ssl = new SslTorrentTestData();
        var idleTorrent = StoppedTorrent(queueAutoStart: false, PlainTorrentTestData.Metadata());
        var waitingPlainTorrent = StoppedTorrent(queueAutoStart: true, PlainTorrentTestData.Metadata());
        var waitingSslTorrent = StoppedTorrent(queueAutoStart: true, ssl.Metadata);
        var engine = SubstituteEngine(idleTorrent, waitingPlainTorrent, waitingSslTorrent);

        await using var sut = CreateSut(BaseSettings(), engine);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        idleTorrent.DidNotReceive().ExportTorrentFile();
        Assert.True(waitingPlainTorrent.QueueAutoStart);
        Assert.False(waitingSslTorrent.QueueAutoStart);
    }

    [Fact]
    public async Task InitializeAsync_WhenTheEngineFailsToStart_ReportsThatFailure_AndRestoresTheQueue()
    {
        var cause = new InvalidOperationException("Could not bind the listening port.");
        var unreadable = StoppedTorrent(queueAutoStart: true, PlainTorrentTestData.Metadata());
        unreadable.ExportTorrentFile().Returns(_ => throw new ObjectDisposedException("torrent"));
        var engine = SubstituteEngine(unreadable);
        engine.Settings.Queue.Enabled = true;
        engine.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(cause));

        await using var sut = CreateSut(BaseSettings(), engine);
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Same(cause, thrown);
        Assert.True(engine.Settings.Queue.Enabled);
    }

    /// <summary>
    /// The settings screen applies its own changes as they are made. Anything else that saves -
    /// an MCP client, for one - has only the save to go on, so the save has to put them in force.
    /// </summary>
    [Fact]
    public async Task SavingSettings_PutsLimitsStreamingAndEncryptionIntoTheRunningEngine()
    {
        var settings = BaseSettings();
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);
        var engine = SubstituteEngine();
        await using var sut = new TorrentEngineService(
            settingsService, Substitute.For<ILoggerFactory>(), TimeProvider.System, _ => engine);
        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        settings.Network.MaxDownloadSpeedBytesPerSecond = 250 * 1024;
        settings.Streaming.ReadAheadBytes = 32 * 1024 * 1024;
        settings.EncryptionMode = "Require";
        settingsService.SettingsSaved += Raise.Event<Func<CancellationToken, Task>>(CancellationToken.None);

        Assert.Equal(250 * 1024, engine.Settings.Transfer.MaxDownloadSpeed);
        Assert.Equal(32 * 1024 * 1024, engine.Settings.Streaming.ReadAheadBytes);
        Assert.Equal(Encryption.Require, engine.Settings.Connection.Encryption);
    }

    private static IClientEngine SubstituteEngine(params ITorrent[] torrents)
    {
        var engine = Substitute.For<IClientEngine>();
        engine.Settings.Returns(new Settings());
        engine.GetTorrents().Returns(torrents);
        return engine;
    }

    private static ITorrent StoppedTorrent(bool queueAutoStart, PeerSharp.Core.TorrentFile metadata)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Started.Returns(false);
        torrent.HasMetadata.Returns(true);
        torrent.QueueAutoStart = queueAutoStart;
        torrent.ExportTorrentFile().Returns(metadata);
        return torrent;
    }

    private static TorrentEngineService CreateSut(AppSettings settings, IClientEngine engine)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);
        return new TorrentEngineService(settingsService, Substitute.For<ILoggerFactory>(), TimeProvider.System, _ => engine);
    }

    private static TorrentEngineService CreateSut(AppSettings settings)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);
        return new TorrentEngineService(settingsService, Substitute.For<ILoggerFactory>(), TimeProvider.System);
    }

    /// <summary>
    /// The upgrade hazard: PeerSharp 4.0 refuses UDP an HTTP proxy cannot carry, and that refusal is
    /// a throw out of <c>InitializeAsync</c>. A stored configuration that ran yesterday must not stop
    /// the application opening today.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_WithAnHttpProxy_StartsWithoutDhtRatherThanThrowing()
    {
        var settings = BaseSettings(s =>
        {
            s.Network.EnableDht = true;
            s.Proxy.ProxyType = "Http";
            s.Proxy.ProxyHost = "proxy.example";
            s.Proxy.ProxyPort = 8080;
        });
        var sut = CreateSut(settings);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(sut.Engine.Settings.Dht.Enabled);
        Assert.False(sut.Engine.Settings.Connection.EnableUtpIn);
        Assert.False(sut.Engine.Settings.Connection.EnableUtpOut);
        Assert.True(sut.ProxyRestrictionApplied);

        // The proxy itself is kept: it is the thing the user asked for.
        Assert.Equal("proxy.example", sut.Engine.Settings.Proxy.Host);

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_StartsWithTheCurrentlyScheduledLimits()
    {
        var now = DateTimeOffset.Now;
        var settings = BaseSettings(s =>
        {
            s.Network.MaxDownloadSpeedBytesPerSecond = 900;
            s.Network.MaxUploadSpeedBytesPerSecond = 800;
            s.Schedule.Enabled = true;
            s.Schedule.From = now.AddMinutes(-5).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            s.Schedule.To = now.AddMinutes(5).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            s.Schedule.DownloadLimitBytesPerSecond = 100;
            s.Schedule.UploadLimitBytesPerSecond = 200;
        });
        var sut = CreateSut(settings);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(100, sut.Engine.Settings.Transfer.MaxDownloadSpeed);
        Assert.Equal(200, sut.Engine.Settings.Transfer.MaxUploadSpeed);

        await sut.DisposeAsync();
    }

    /// <summary>
    /// Limits used to be clamped into a <c>uint</c> because that is what the engine took. PeerSharp
    /// 3.2 widened them to <c>long</c>, so a limit above 4 GB/s must now survive rather than quietly
    /// becoming 4 GB/s.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_CarriesALimitAboveWhatAUintCouldHold()
    {
        const long aboveUintMax = 5_000_000_000;
        var settings = BaseSettings(s =>
        {
            s.Network.MaxDownloadSpeedBytesPerSecond = aboveUintMax;
            s.Network.MaxDiskWriteSpeedBytesPerSecond = aboveUintMax;
        });
        var sut = CreateSut(settings);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(aboveUintMax, sut.Engine.Settings.Transfer.MaxDownloadSpeed);
        Assert.Equal(aboveUintMax, sut.Engine.Settings.Files.MaxDiskWriteSpeed);

        await sut.DisposeAsync();
    }

    /// <summary>
    /// The engine rejects a negative limit outright now rather than clamping it, so a stored minus
    /// sign would otherwise stop the engine being built at all.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_ClampsANegativeStoredLimitToUnlimited()
    {
        var settings = BaseSettings(s => s.Network.MaxUploadSpeedBytesPerSecond = -1);
        var sut = CreateSut(settings);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, sut.Engine.Settings.Transfer.MaxUploadSpeed);

        await sut.DisposeAsync();
    }

    /// <summary>
    /// The engine throws when handed <c>IPAddress.Any</c>, because "any address" is not the
    /// single-address guarantee a bind address exists to make. Blank and 0.0.0.0 both have to reach
    /// it as "no bind address" instead.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("not an address")]
    public async Task InitializeAsync_TreatsAnUnusableBindAddressAsNone(string stored)
    {
        var settings = BaseSettings(s => s.Network.BindAddress = stored);
        var sut = CreateSut(settings);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Null(sut.Engine.Settings.Connection.BindAddress);

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_WithABindAddress_DisablesBothPortMappers()
    {
        var settings = BaseSettings(s =>
        {
            s.Network.BindAddress = System.Net.IPAddress.Loopback.ToString();
            s.Network.EnableNatPmp = true;
            s.Network.EnableUpnp = true;
        });
        var sut = CreateSut(settings);

        await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.IPAddress.Loopback, sut.Engine.Settings.Connection.BindAddress);
        Assert.False(sut.Engine.Settings.Connection.NatPmpPortMapping);
        Assert.False(sut.Engine.Settings.Connection.UpnpPortMapping);

        await sut.DisposeAsync();
    }
}
