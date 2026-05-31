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
}
