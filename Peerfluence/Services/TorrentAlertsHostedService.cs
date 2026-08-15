using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Peerfluence.Core.Services.Rpc;
using PeerSharp.Core;

namespace Peerfluence.Services;

public sealed class TorrentAlertsHostedService : IHostedService
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentTransferSnapshots _snapshots;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _monitorTask;

    public TorrentAlertsHostedService(ITorrentService torrentService, ITorrentTransferSnapshots snapshots)
    {
        _torrentService = torrentService;
        _snapshots = snapshots;
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
                // Kept as well as published. Transfer rates exist only as they go past - nothing can
                // be asked how fast a torrent is going - so anything that has to answer that later,
                // such as a remote client polling, needs the last figures written down here.
                if (alert is TransferStatsAlert stats)
                {
                    _snapshots.Record(stats.Torrent.Hash, new TorrentTransferSnapshot(
                        stats.DownloadSpeed,
                        stats.UploadSpeed,
                        stats.Downloaded,
                        stats.Uploaded,
                        stats.ConnectedPeers));
                }

                _torrentService.PublishAlert(alert, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
