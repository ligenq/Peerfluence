using Microsoft.Extensions.Logging;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public class TorrentEngineServiceTests
{
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
        var sut = new TorrentEngineService(settingsService, Substitute.For<ILoggerFactory>());

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

    private static TorrentEngineService CreateSut(AppSettings settings)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);
        return new TorrentEngineService(settingsService, Substitute.For<ILoggerFactory>());
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
