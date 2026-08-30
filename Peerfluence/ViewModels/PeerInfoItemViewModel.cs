using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Peerfluence.ViewModels;

public sealed class PeerInfoItemViewModel : ObservableObject
{
    public PeerInfoItemViewModel(PeerInfo peer)
    {
        EndPoint = peer.EndPoint.ToString();
        UpdateFrom(peer);
    }

    public void UpdateFrom(PeerInfo peer)
    {
        Client = peer.ClientName;
        Country = peer.Country;
        DownloadSpeedBytesPerSecond = peer.DownloadSpeed;
        UploadSpeedBytesPerSecond = peer.UploadSpeed;
        Progress = peer.Progress;
        HasReportedPieces = peer.HasReportedPieces;
        IsEncrypted = peer.IsEncrypted;
        IsUtp = peer.IsUtp;
    }

    public string EndPoint { get; }

    public string Client
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string Country
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

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

    public float Progress
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    /// <summary>
    /// False until the peer sends a bitfield or a have message. A peer that has said nothing reports
    /// <see cref="Progress"/> of zero, which is indistinguishable from one that genuinely holds
    /// nothing.
    /// </summary>
    public bool HasReportedPieces
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string ProgressText => HasReportedPieces
        ? Progress.ToString("P1", CultureInfo.CurrentCulture)
        : "—";

    public bool IsEncrypted
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsUtp
    {
        get;
        private set => SetProperty(ref field, value);
    }
}
