using Peerfluence.Services;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class AppSettingsHostedServiceTests
{
    [Fact]
    public async Task StartAsync_LoadsSettings_WithHostToken()
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        var sut = new AppSettingsHostedService(settingsService);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        await settingsService.Received(1).LoadAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_SavesSettings_WithoutUsingHostToken()
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        var sut = new AppSettingsHostedService(settingsService);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await sut.StopAsync(cts.Token);

        await settingsService.Received(1).SaveAsync(Arg.Is<CancellationToken>(token => token == CancellationToken.None));
    }
}
