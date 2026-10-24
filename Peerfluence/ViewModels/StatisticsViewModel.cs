using System;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Properties;
using PeerSharp.Core;

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
/// <para>
/// Nothing is read while the page is not on screen. It reads the engine once on being shown, so it
/// never opens on old numbers, and after that takes the reading the downloads screen already makes
/// every second rather than reading the engine a second time.
/// </para>
/// </remarks>
public sealed class StatisticsViewModel : ViewModelBase, IFeatureViewModel, IOnScreenAware, IDisposable
{
    private readonly ITorrentService _torrentService;
    private bool _disposed;

    public StatisticsViewModel(ITorrentService torrentService)
    {
        _torrentService = torrentService;
    }

    // IFeatureViewModel
    public string Title => Resources.Nav_Statistics;

    public string IconKind => "ChartLine";

    /// <summary>Between finding torrents and the settings: a place to look, not a thing to do.</summary>
    public int Order => 75;

    /// <summary>
    /// Whether the page is showing. Becoming visible reads the engine straight away and starts taking
    /// the downloads screen's readings; becoming hidden stops taking them.
    /// </summary>
    public bool IsOnScreen
    {
        get;
        set
        {
            if (_disposed || !SetProperty(ref field, value))
            {
                return;
            }

            if (value)
            {
                Refresh();
                WeakReferenceMessenger.Default.Register<StatisticsViewModel, EngineStatsReadMessage>(
                    this,
                    static (recipient, message) => recipient.Apply(message.Stats));
            }
            else
            {
                WeakReferenceMessenger.Default.Unregister<EngineStatsReadMessage>(this);
            }
        }
    }

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
        Apply(_torrentService.GetStats());
    }

    private void Apply(EngineStats snapshot)
    {
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
