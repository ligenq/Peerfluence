using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Peerfluence.Services;

public sealed class TorrentEngineHostedService : IHostedService
{
    private readonly ITorrentEngineService _engineService;
    private readonly ILogger<TorrentEngineHostedService> _logger;

    public TorrentEngineHostedService(ITorrentEngineService engineService, ILogger<TorrentEngineHostedService> logger)
    {
        _engineService = engineService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _engineService.InitializeAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _engineService.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host's shutdown deadline elapsed. Engine disposal continues in
            // the background and TorrentEngineService observes any late failure.
        }
        catch (Exception ex)
        {
            // A failure while shutting the engine down must not crash the app on
            // the way out. TorrentEngineService already logs disposal faults; log
            // here too in case the failure originated elsewhere in ShutdownAsync.
            _logger.LogError(ex, "Error shutting down the torrent engine during application stop.");
        }
    }
}
