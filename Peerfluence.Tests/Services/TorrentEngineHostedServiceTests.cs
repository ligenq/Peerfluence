using Peerfluence.Services;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class TorrentEngineHostedServiceTests
{
    [Fact]
    public async Task StartAsync_InitializesEngine()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        var sut = new TorrentEngineHostedService(engineService);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        await engineService.Received(1).InitializeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_DisposesEngine()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        var sut = new TorrentEngineHostedService(engineService);

        await sut.StopAsync(TestContext.Current.CancellationToken);

        await engineService.Received(1).DisposeAsync();
    }
}
