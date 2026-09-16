using System;
using System.Threading;
using System.Threading.Tasks;
using Peerfluence.Core;
using Peerfluence.Core.Services;
using Peerfluence.Properties;

namespace Peerfluence.ViewModels;

/// <summary>
/// What the engine has been doing, shown to the person doing it.
/// </summary>
/// <remarks>
/// <para>
/// The numbers come from <see cref="ITorrentService"/>, which reads PeerSharp's engine
/// statistics, including transfer totals for torrents that have since been removed.
/// </para>
/// <para>
/// Everything here is for a session rather than for all time. The counters live in the engine, and
/// include restored torrent counters and reset when a new engine is created.
/// </para>
/// </remarks>
public sealed class StatisticsViewModel : ViewModelBase, IFeatureViewModel, IDisposable
{
    private readonly ITorrentService _torrentService;
    private readonly CancellationTokenSource _stopping = new();
    private bool _disposed;

    public StatisticsViewModel(ITorrentService torrentService)
    {
        _torrentService = torrentService;
        Refresh();
        _ = KeepFreshAsync(_stopping.Token);
    }

    // IFeatureViewModel
    public string Title => Resources.Nav_Statistics;

    public string IconKind => "ChartLine";

    /// <summary>Between finding torrents and the settings: a place to look, not a thing to do.</summary>
    public int Order => 75;

    public long DownloadedBytes
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public long UploadedBytes
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Uploaded over downloaded, and zero until something has been downloaded.
    /// </summary>
    /// <remarks>
    /// A session that has uploaded without downloading - reseeding what is already on disk - has no
    /// meaningful ratio rather than an infinite one.
    /// </remarks>
    public double Ratio
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public long DownloadSpeedBytesPerSecond
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public long UploadSpeedBytesPerSecond
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public long Torrents
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public long ActiveTorrents
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public long ConnectedPeers
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Takes one reading and publishes it.</summary>
    public void Refresh()
    {
        var snapshot = _torrentService.GetStats();

        DownloadedBytes = snapshot.LifetimeDownloaded;
        UploadedBytes = snapshot.LifetimeUploaded;
        Ratio = snapshot.LifetimeDownloaded > 0
            ? (double)snapshot.LifetimeUploaded / snapshot.LifetimeDownloaded
            : 0d;
        DownloadSpeedBytesPerSecond = snapshot.DownloadSpeed;
        UploadSpeedBytesPerSecond = snapshot.UploadSpeed;
        Torrents = snapshot.TorrentCount;
        ActiveTorrents = snapshot.ActiveTorrents;
        ConnectedPeers = snapshot.TotalPeers;
    }

    public void Dispose()
    {
        // Idempotent, as the contract asks: cancelling a token source that has been disposed throws.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Cancel();
        _stopping.Dispose();
    }

    /// <summary>
    /// Reads once a second for as long as the application is running.
    /// </summary>
    /// <remarks>
    /// A second, because these are counters rather than an animation, and reading the engine statistics is not
    /// free. The screen is calm at that rate and still obviously live.
    /// </remarks>
    private async Task KeepFreshAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // Closing down.
        }
    }
}
