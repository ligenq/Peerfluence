using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Peerfluence.Services;

public sealed class TorrentAlertsHostedService : IHostedService
{
    private readonly ITorrentService _torrentService;
    private readonly ITransientTorrentTracker _transientTorrentTracker;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _monitorTask;

    public TorrentAlertsHostedService(
        ITorrentService torrentService,
        ITransientTorrentTracker transientTorrentTracker)
    {
        _torrentService = torrentService;
        _transientTorrentTracker = transientTorrentTracker;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        const AlertId alerts =
            AlertId.TorrentAdded |
            AlertId.TorrentRemoved |
            AlertId.TorrentCheckStarted |
            AlertId.TorrentCheckFinished |
            AlertId.TorrentInterrupted |
            AlertId.TorrentStarted |
            AlertId.TorrentStopped |
            AlertId.TorrentStateChanged |
            AlertId.ProgressChanged |
            AlertId.TransferStatsUpdated |
            AlertId.TorrentError |
            AlertId.MetadataInitialized |
            AlertId.MetadataProgressChanged |
            AlertId.TorrentFinished;

        _torrentService.RegisterAlertMask((uint)alerts);

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = MonitorAlertsAsync(_cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cancellationTokenSource == null || _monitorTask == null)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        try
        {
            await _monitorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task MonitorAlertsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var alert in _torrentService.GetAlertsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                // Torrents the engine added only to read a magnet's metadata are removed again
                // moments later. Publishing their lifecycle would put a row in the downloads list,
                // raise a "metadata ready" notification and rewrite a download path, all for a
                // torrent the user never asked for.
                if (_transientTorrentTracker.ShouldSuppress(alert))
                {
                    continue;
                }

                _torrentService.PublishAlert(alert, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
