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
            AlertId.TorrentFinished |

            // Added in PeerSharp 4.0. A magnet that has asked capable peers for a long time and been
            // given nothing is the failure people describe as "it just sits there"; a piece failing
            // its hash repeatedly and a peer being refused are what a support question needs
            // answering; and a listener that could not bind the configured port is the case where
            // port forwarding silently stops reaching the session.
            AlertId.MetadataDownloadStalled |
            AlertId.PieceHashFailed |
            AlertId.PeerBlocked |
            AlertId.ListenPortChanged;

        // Deliberately not registered: PeerDisconnected. It fires once per departing peer, which in
        // a busy swarm is constant, and nothing here needs it - the peer list is rebuilt from a
        // snapshot on each refresh rather than accumulated from arrivals and departures.

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
