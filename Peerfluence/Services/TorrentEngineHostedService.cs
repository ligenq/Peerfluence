using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Peerfluence.Services;

public sealed class TorrentEngineHostedService : IHostedService
{
    private readonly ITorrentEngineService _engineService;

    public TorrentEngineHostedService(ITorrentEngineService engineService)
    {
        _engineService = engineService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _engineService.InitializeAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _engineService.DisposeAsync().ConfigureAwait(false);
    }
}
