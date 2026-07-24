using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Services;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class TorrentEngineHostedServiceTests
{
    [Fact]
    public async Task StartAsync_InitializesEngine()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        var sut = CreateService(engineService);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        await engineService.Received(1).InitializeAsync(TestContext.Current.CancellationToken);
        await engineService.DidNotReceive().LoadOptionalDataAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_ShutsDownEngineWithHostToken()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        var sut = CreateService(engineService);

        await sut.StopAsync(TestContext.Current.CancellationToken);

        await engineService.Received(1).ShutdownAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_DoesNotPropagateHostDeadlineCancellation()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        engineService
            .ShutdownAsync(cancellation.Token)
            .Returns(Task.FromCanceled(cancellation.Token));
        var sut = CreateService(engineService);

        var exception = await Record.ExceptionAsync(() => sut.StopAsync(cancellation.Token));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StopAsync_DoesNotPropagateShutdownFailure()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService
            .ShutdownAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("disposal failed")));
        var sut = CreateService(engineService);

        var exception = await Record.ExceptionAsync(() => sut.StopAsync(TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    private static TorrentEngineHostedService CreateService(ITorrentEngineService engineService) =>
        new(engineService, NullLogger<TorrentEngineHostedService>.Instance);
}
