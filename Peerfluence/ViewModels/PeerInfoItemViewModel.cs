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

    public int DownloadSpeedBytesPerSecond
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public int UploadSpeedBytesPerSecond
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public float Progress
    {
        get;
        private set => SetProperty(ref field, value);
    }

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
