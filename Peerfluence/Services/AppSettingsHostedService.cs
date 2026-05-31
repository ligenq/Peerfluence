using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Peerfluence.Services;

public sealed class AppSettingsHostedService : IHostedService
{
    private readonly IAppSettingsService _settingsService;

    public AppSettingsHostedService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _settingsService.LoadAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Don't pass the host's shutdown token — settings must be saved even if
        // the shutdown timeout is firing.
        return _settingsService.SaveAsync(CancellationToken.None);
    }
}
