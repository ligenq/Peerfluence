using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.ViewModels;

public class TorrentListItemViewModelTests
{
    private readonly ITorrent _torrent = Substitute.For<ITorrent>();
    private readonly TorrentListItemViewModel _sut;

    public TorrentListItemViewModelTests()
    {
        _torrent.Name.Returns("Test Torrent");
        _torrent.Hash.Returns(InfoHash.CreateRandom());
        _torrent.Progress.Returns(0.5f);
        _torrent.State.Returns(TorrentState.Active);
        _torrent.TotalSize.Returns(1024L * 1024);
        _torrent.DataLeft.Returns(512L * 1024);
        _torrent.HasMetadata.Returns(true);
        _torrent.FileTransfer.EndGameMode.Returns(false);

        _sut = new TorrentListItemViewModel(_torrent);
    }

    [Fact]
    public void Constructor_SetsPropertiesFromTorrent()
    {
        Assert.Equal("Test Torrent", _sut.Name);
        Assert.Equal(0.5f, _sut.Progress);
        Assert.Equal("Active", _sut.State);
        Assert.Equal(1024L * 1024, _sut.TotalSizeBytes);
        Assert.Equal(512L * 1024, _sut.DataLeftBytes);
    }

    [Fact]
    public void ProgressPercent_ReturnsPercentageValue()
    {
        Assert.Equal(50.0, _sut.ProgressPercent);
    }

    [Fact]
    public void ProgressPercent_UpdatesWhenProgressChanges()
    {
        var raised = false;
        _sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_sut.ProgressPercent)) raised = true;
        };

        _torrent.Progress.Returns(0.75f);
        _torrent.FileTransfer.EndGameMode.Returns(false);
        _sut.UpdateFrom(_torrent);

        Assert.Equal(75.0, _sut.ProgressPercent);
        Assert.True(raised);
    }

    [Fact]
    public void UpdateFrom_UpdatesAllProperties()
    {
        _torrent.Name.Returns("Updated Torrent");
        _torrent.Progress.Returns(0.9f);
        _torrent.State.Returns(TorrentState.Stopped);
        _torrent.TotalSize.Returns(2048L);
        _torrent.DataLeft.Returns(100L);
        _torrent.FileTransfer.EndGameMode.Returns(false);

        _sut.UpdateFrom(_torrent);

        Assert.Equal("Updated Torrent", _sut.Name);
        Assert.Equal(0.9f, _sut.Progress);
        Assert.Equal("Stopped", _sut.State);
        Assert.Equal(2048L, _sut.TotalSizeBytes);
        Assert.Equal(100L, _sut.DataLeftBytes);
    }

    [Fact]
    public void UpdateFrom_StateIsPlain()
    {
        _torrent.State.Returns(TorrentState.Active);

        _sut.UpdateFrom(_torrent);

        Assert.Equal("Active", _sut.State);
    }

    [Fact]
    public void UpdateProgress_UpdatesProgressAndSize()
    {
        _torrent.Progress.Returns(0.8f);
        _torrent.TotalSize.Returns(5000L);
        _torrent.DataLeft.Returns(1000L);

        _sut.UpdateProgress(_torrent);

        Assert.Equal(0.8f, _sut.Progress);
        Assert.Equal(5000L, _sut.TotalSizeBytes);
        Assert.Equal(1000L, _sut.DataLeftBytes);
    }

    [Fact]
    public void UpdateTransferStats_UpdatesSpeeds()
    {
        var stats = new TransferStats
        {
            DownloadSpeed = 1000,
            UploadSpeed = 500,
            ConnectedPeers = 5,
            Downloaded = 10000,
            Uploaded = 5000
        };

        _sut.UpdateTransferStats(stats);

        Assert.Equal(1000, _sut.DownloadSpeedBytesPerSecond);
        Assert.Equal(500, _sut.UploadSpeedBytesPerSecond);
        Assert.Equal(5, _sut.ConnectedPeers);
    }

    [Fact]
    public void UpdateTransferStats_CalculatesEta_WhenDownloading()
    {
        _torrent.DataLeft.Returns(10000L);
        _torrent.FileTransfer.EndGameMode.Returns(false);
        _sut.UpdateFrom(_torrent);

        var stats = new TransferStats
        {
            DownloadSpeed = 1000,
            UploadSpeed = 0,
            ConnectedPeers = 1
        };

        _sut.UpdateTransferStats(stats);

        Assert.NotEqual("\u221E", _sut.Eta);
        Assert.NotEqual(string.Empty, _sut.Eta);
    }

    [Fact]
    public void UpdateTransferStats_EtaIsInfinity_WhenNoSpeed()
    {
        _torrent.DataLeft.Returns(10000L);
        _torrent.FileTransfer.EndGameMode.Returns(false);
        _sut.UpdateFrom(_torrent);

        var stats = new TransferStats
        {
            DownloadSpeed = 0,
            UploadSpeed = 0,
            ConnectedPeers = 0
        };

        _sut.UpdateTransferStats(stats);

        Assert.Equal("\u221E", _sut.Eta);
    }

    [Fact]
    public void UpdateTransferStats_EtaIsEmpty_WhenComplete()
    {
        _torrent.DataLeft.Returns(0L);
        _torrent.FileTransfer.EndGameMode.Returns(false);
        _sut.UpdateFrom(_torrent);

        var stats = new TransferStats
        {
            DownloadSpeed = 0,
            UploadSpeed = 100,
            ConnectedPeers = 1
        };

        _sut.UpdateTransferStats(stats);

        Assert.Equal(string.Empty, _sut.Eta);
    }

    [Fact]
    public void Hash_MatchesTorrentHash()
    {
        Assert.Equal(_torrent.Hash, _sut.Hash);
    }

    [Fact]
    public void Torrent_ReturnsInjectedTorrent()
    {
        Assert.Same(_torrent, _sut.Torrent);
    }

    [Fact]
    public void StatusDetail_ShowsReadyWhenHasMetadata()
    {
        Assert.Equal(Properties.Resources.Status_TorrentReady, _sut.StatusDetail);
    }

    [Fact]
    public void StatusDetail_ShowsFetchingWhenNoMetadata()
    {
        _torrent.HasMetadata.Returns(false);
        _torrent.FileTransfer.EndGameMode.Returns(false);
        _sut.UpdateFrom(_torrent);

        Assert.Equal(Properties.Resources.Status_FetchingMetadata, _sut.StatusDetail);
    }
}
