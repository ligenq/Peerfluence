using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Messaging;
using PeerSharp.Interfaces;
using System;

namespace Peerfluence.ViewModels;

[ExcludeFromDI]
public sealed class TorrentListItemViewModel : ViewModelBase
{
    private bool _hasMetadata;

    public TorrentListItemViewModel(ITorrent torrent)
    {
        Torrent = torrent;
        Hash = torrent.Hash;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => UpdateStatusDetail());
        UpdateFrom(torrent);
    }

    public void Detach()
    {
        WeakReferenceMessenger.Default.Unregister<LanguageChangedMessage>(this);
    }

    public ITorrent Torrent { get; }

    public InfoHash Hash { get; }

    public string Name
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public float Progress
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    public double ProgressPercent => Progress * 100.0;

    public string State
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public long TotalSizeBytes
    {
        get;
        set => SetProperty(ref field, value);
    }

    public long DataLeftBytes
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int DownloadSpeedBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int UploadSpeedBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int ConnectedPeers
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string Eta
    {
        get;
        set => SetProperty(ref field, value);
    } = "\u221E";

    public string StatusDetail
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public void UpdateFrom(ITorrent torrent)
    {
        Name = torrent.Name;
        Progress = torrent.Progress;
        State = torrent.State.ToDisplayString();
        TotalSizeBytes = torrent.TotalSize;
        DataLeftBytes = torrent.DataLeft;
        _hasMetadata = torrent.HasMetadata;
        UpdateStatusDetail();

        if (torrent.State is TorrentState.Stopped or TorrentState.Stopping)
        {
            ClearTransferStats();
        }
    }

    public void UpdateProgress(ITorrent torrent)
    {
        Progress = torrent.Progress;
        TotalSizeBytes = torrent.TotalSize;
        DataLeftBytes = torrent.DataLeft;
    }

    private void UpdateStatusDetail()
    {
        StatusDetail = _hasMetadata
            ? Properties.Resources.Status_TorrentReady
            : Properties.Resources.Status_FetchingMetadata;
    }

    public void UpdateTransferStats(TransferStats stats)
    {
        if (DataLeftBytes == 0)
        {
            DownloadSpeedBytesPerSecond = 0;
            _smoothedDownloadSpeedBytesPerSecond = 0;
            _downloadSpeedSamples = 0;
        }
        else
        {
            DownloadSpeedBytesPerSecond = UpdateSmoothedSpeed(
                stats.DownloadSpeed,
                ref _smoothedDownloadSpeedBytesPerSecond,
                ref _downloadSpeedSamples);
        }

        UploadSpeedBytesPerSecond = UpdateSmoothedSpeed(
            stats.UploadSpeed,
            ref _smoothedUploadSpeedBytesPerSecond,
            ref _uploadSpeedSamples);
        ConnectedPeers = stats.ConnectedPeers;

        if (DownloadSpeedBytesPerSecond > 0 && DataLeftBytes > 0)
        {
            var seconds = DataLeftBytes / DownloadSpeedBytesPerSecond;
            var ts = TimeSpan.FromSeconds(seconds);

            if (ts.TotalSeconds < 60)
            {
                Eta = string.Format(Properties.Resources.Eta_Seconds, ts.Seconds);
            }
            else if (ts.TotalMinutes < 60)
            {
                Eta = string.Format(Properties.Resources.Eta_Minutes, ts.Minutes);
            }
            else if (ts.TotalHours < 24)
            {
                Eta = string.Format(Properties.Resources.Eta_HoursMinutes, ts.Hours, ts.Minutes);
            }
            else
            {
                Eta = string.Format(Properties.Resources.Eta_DaysHours, ts.Days, ts.Hours);
            }
        }
        else if (DataLeftBytes == 0)
        {
            Eta = string.Empty;
        }
        else
        {
            Eta = "\u221E";
        }
    }

    private void ClearTransferStats()
    {
        DownloadSpeedBytesPerSecond = 0;
        UploadSpeedBytesPerSecond = 0;
        ConnectedPeers = 0;
        Eta = string.Empty;
        _smoothedDownloadSpeedBytesPerSecond = 0;
        _smoothedUploadSpeedBytesPerSecond = 0;
        _downloadSpeedSamples = 0;
        _uploadSpeedSamples = 0;
    }

    private const int SpeedSmoothingWindow = 3;
    private double _smoothedDownloadSpeedBytesPerSecond;
    private double _smoothedUploadSpeedBytesPerSecond;
    private int _downloadSpeedSamples;
    private int _uploadSpeedSamples;

    private static int UpdateSmoothedSpeed(int current, ref double smoothed, ref int samples)
    {
        if (samples == 0)
        {
            smoothed = current;
            samples = 1;
            return current;
        }

        if (current == 0 && smoothed > 0 && samples > 1)
        {
            smoothed *= 0.7;
            if (smoothed < 1)
            {
                smoothed = 0;
            }
            samples++;
            return (int)Math.Round(smoothed);
        }

        double alpha = 2.0 / (SpeedSmoothingWindow + 1);
        smoothed = (alpha * current) + ((1.0 - alpha) * smoothed);
        samples++;
        return (int)Math.Round(smoothed);
    }
}
