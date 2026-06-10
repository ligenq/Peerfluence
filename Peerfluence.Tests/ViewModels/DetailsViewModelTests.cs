using System.IO.Abstractions;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.ViewModels;

[Collection("Messenger")]
public class DetailsViewModelTests
{
    private readonly TorrentSelectionService _selectionService = new(Substitute.For<IAppMessenger>());
    private readonly ITorrentService _torrentService;
    private readonly LocalizationService _localizationService = new();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly ITopLevelService _topLevelService = Substitute.For<ITopLevelService>();
    private readonly DetailsViewModel _sut;

    public DetailsViewModelTests()
    {
        WeakReferenceMessenger.Default.Reset();

        var store = Substitute.For<IAppSettingsStore>();
        var paths = new AppPaths();
        var settingsService = new AppSettingsService(paths, store, new FileSystem());
        var loggerFactory = Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>();
        var engineService = new TorrentEngineService(settingsService, loggerFactory);
        _torrentService = new TorrentService(engineService, Substitute.For<IAppMessenger>());

        _sut = new DetailsViewModel(
            _selectionService,
            _torrentService,
            _localizationService,
            _notificationService,
            _topLevelService,
            settingsService);
        _sut.UIDispatcher = action => action();
    }

    [Fact]
    public void InitialState_NameShowsSelectTorrent()
    {
        Assert.Equal(Properties.Resources.Details_SelectTorrent, _sut.Name);
    }

    [Fact]
    public void InitialState_PropertiesAreDefault()
    {
        Assert.Equal(string.Empty, _sut.InfoHash);
        Assert.Equal(string.Empty, _sut.State);
        Assert.Equal(0f, _sut.Progress);
        Assert.Equal(string.Empty, _sut.DownloadPath);
        Assert.Equal(0L, _sut.TotalSizeBytes);
        Assert.Equal(0L, _sut.DownloadedBytes);
        Assert.Equal(0, _sut.ConnectedPeers);
        Assert.Equal(0, _sut.DownloadLimitBytesPerSecond);
        Assert.Equal(0, _sut.UploadLimitBytesPerSecond);
        Assert.Equal(0, _sut.DiskReadLimitBytesPerSecond);
        Assert.Equal(0, _sut.DiskWriteLimitBytesPerSecond);
    }

    [Fact]
    public void InitialState_CollectionsAreEmpty()
    {
        Assert.Empty(_sut.Files);
        Assert.Empty(_sut.Trackers);
        Assert.Empty(_sut.Peers);
    }

    [Fact]
    public void InitialState_RecheckingIsFalse()
    {
        Assert.False(_sut.IsRechecking);
        Assert.Equal(0f, _sut.RecheckProgress);
        Assert.Equal(string.Empty, _sut.RecheckStatus);
    }

    [Fact]
    public void DownloadStrategies_ContainsAllValues()
    {
        var expected = Enum.GetValues<DownloadStrategy>();
        Assert.Equal(expected, _sut.DownloadStrategies.Select(option => option.Value));
        Assert.Contains(_sut.DownloadStrategies, option =>
            option.Value == DownloadStrategy.RarestFirst &&
            option.DisplayName == "Rarest first");
    }

    [Fact]
    public void PriorityOptions_ContainsAllValues()
    {
        var expected = Enum.GetValues<Priority>();
        Assert.Equal(expected, _sut.PriorityChoices.Select(option => option.Value));
        Assert.Contains(_sut.PriorityChoices, option =>
            option.Value == Priority.DoNotDownload &&
            option.DisplayName == "Do not download");
    }

    [Fact]
    public void ForceRecheckCommand_CannotExecuteWhenNoTorrentSelected()
    {
        Assert.False(_sut.ForceRecheckCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyTorrentSettingsCommand_CannotExecuteWhenNoTorrentSelected()
    {
        Assert.False(_sut.ApplyTorrentSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void SaveResumeDataCommand_CannotExecuteWhenNoTorrentSelected()
    {
        Assert.False(_sut.SaveResumeDataCommand.CanExecute(null));
    }

    [Fact]
    public void ChangeDownloadPathCommand_CannotExecuteWhenNoTorrentSelected()
    {
        Assert.False(_sut.ChangeDownloadPathCommand.CanExecute(null));
    }

    [Fact]
    public void AddTrackerCommand_CannotExecuteWhenNoTorrentSelected()
    {
        Assert.False(_sut.AddTrackerCommand.CanExecute(null));
    }

    [Fact]
    public void AnnounceCommand_CannotExecuteWhenNoTorrentSelected()
    {
        Assert.False(_sut.AnnounceCommand.CanExecute(null));
    }

    [Fact]
    public void IsRechecking_NotifiesForceRecheckCanExecuteChanged()
    {
        var changed = false;
        _sut.ForceRecheckCommand.CanExecuteChanged += (_, _) => changed = true;

        _sut.IsRechecking = true;
        Assert.True(changed);
    }

    [Fact]
    public void NewTrackerUrl_NotifiesAddTrackerCanExecuteChanged()
    {
        var changed = false;
        _sut.AddTrackerCommand.CanExecuteChanged += (_, _) => changed = true;

        _sut.NewTrackerUrl = "http://tracker.test/announce";
        Assert.True(changed);
    }

    [Fact]
    public void Properties_RaisePropertyChanged()
    {
        var changedProperties = new List<string>();
        _sut.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        _sut.DownloadLimitBytesPerSecond = 1000;
        _sut.UploadLimitBytesPerSecond = 500;
        _sut.DiskReadLimitBytesPerSecond = 2000;
        _sut.DiskWriteLimitBytesPerSecond = 3000;
        _sut.RatioLimit = "2.0";
        _sut.SeedTimeLimitMinutes = "60";
        _sut.QueuePriority = 5;

        Assert.Contains(nameof(_sut.DownloadLimitBytesPerSecond), changedProperties);
        Assert.Contains(nameof(_sut.UploadLimitBytesPerSecond), changedProperties);
        Assert.Contains(nameof(_sut.DiskReadLimitBytesPerSecond), changedProperties);
        Assert.Contains(nameof(_sut.DiskWriteLimitBytesPerSecond), changedProperties);
        Assert.Contains(nameof(_sut.RatioLimit), changedProperties);
        Assert.Contains(nameof(_sut.SeedTimeLimitMinutes), changedProperties);
        Assert.Contains(nameof(_sut.QueuePriority), changedProperties);
    }

    [Fact]
    public void HasStreamableFiles_DefaultsFalse()
    {
        Assert.False(_sut.HasStreamableFiles);
    }

    [Fact]
    public void IsStreaming_DefaultsFalse()
    {
        Assert.False(_sut.IsStreaming);
    }

    [Fact]
    public void PieceBitfield_DefaultsNull()
    {
        Assert.Null(_sut.PieceBitfield);
    }

    [Fact]
    public void PieceAvailability_DefaultsNull()
    {
        Assert.Null(_sut.PieceAvailability);
    }

    [Fact]
    public void PieceCount_DefaultsZero()
    {
        Assert.Equal(0, _sut.PieceCount);
    }

    [Fact]
    public async Task SelectingTorrent_UpdatesPropertiesAndEnablesCommands()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Test Torrent");
        torrent.Hash.Returns(new InfoHash(new byte[20]));
        torrent.State.Returns(TorrentState.Active);

        var files = Substitute.For<IFiles>();
        files.DownloadPath.Returns("C:\\Downloads");
        torrent.Files.Returns(files);

        var peers = Substitute.For<IPeers>();
        torrent.Peers.Returns(peers);

        var trackers = Substitute.For<ITrackers>();
        torrent.Trackers.Returns(trackers);

        _selectionService.SelectedTorrent = torrent;
        // Trigger and wait for background refresh
        _sut.RefreshFromSelection();

        // Wait for debounce and background work
        await Task.Delay(400);

        Assert.Equal("Test Torrent", _sut.Name);
        Assert.Equal(torrent.Hash.ToString(), _sut.InfoHash);
        Assert.True(_sut.ApplyTorrentSettingsCommand.CanExecute(null));
        Assert.True(_sut.SaveResumeDataCommand.CanExecute(null));
    }
}
