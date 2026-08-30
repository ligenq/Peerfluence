using System.IO.Abstractions;
using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.ViewModels;

[Collection("Messenger")]
public class DetailsViewModelTests
{
    private static readonly string[] ExpectedPeerAddresses =
        ["192.168.1.10:51413", "[::1]:6881", "10.0.0.5:6889"];

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
        // The pane is closed by default and does no work while it is; these tests are about what it
        // does when someone is looking at it.
        settingsService.Current.ShowDetailsPane = true;
        var loggerFactory = Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>();
        var engineService = new TorrentEngineService(settingsService, loggerFactory, TimeProvider.System);
        _torrentService = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient(), SeedingDefaults.Off);

        _sut = new DetailsViewModel(
            _selectionService,
            _torrentService,
            _localizationService,
            _notificationService,
            _topLevelService,
            settingsService, Substitute.For<IDialogService>());
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
        torrent.Hash.Returns(new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray()));
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
        await Task.Delay(400, TestContext.Current.CancellationToken);

        Assert.Equal("Test Torrent", _sut.Name);
        Assert.Equal(torrent.Hash.ToString(), _sut.InfoHash);
        Assert.True(_sut.ApplyTorrentSettingsCommand.CanExecute(null));
        Assert.True(_sut.SaveResumeDataCommand.CanExecute(null));
    }

    [Fact]
    public async Task AlertForAnotherTorrent_LeavesTheSelectedTorrentsDetailsAlone()
    {
        // Two ordinary V1 torrents. Both carry an empty V2 hash, so identity cannot be decided by
        // comparing hashes that are not there: doing so makes every V1 torrent match every other,
        // and the pane redraws itself from whichever one last raised an alert.
        var selected = CreateRefreshableTorrent("Selected", 1);
        var other = CreateRefreshableTorrent("Other", 2);

        _selectionService.SelectedTorrent = selected;
        _sut.RefreshFromSelection();
        await WaitForNameAsync("Selected");

        WeakReferenceMessenger.Default.Send(
            new TorrentAlertMessage(other, new SimpleTorrentAlert { Id = AlertId.ProgressChanged, Torrent = other }));
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Equal("Selected", _sut.Name);
    }

    [Fact]
    public async Task TorrentStateChange_RefreshesRenameFileAvailability()
    {
        var state = TorrentState.Active;
        var torrent = CreateRefreshableTorrent("Selected", 1);
        torrent.State.Returns(_ => state);
        var file = new TorrentFileItemViewModel(
            new TorrentFileInfo("file.bin", 100, 0, 0),
            new FileSelection());

        _selectionService.SelectedTorrent = torrent;
        _sut.RefreshFromSelection();
        await WaitForNameAsync("Selected");
        Assert.False(_sut.RenameFileCommand.CanExecute(file));

        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut.RenameFileCommand.CanExecuteChanged += (_, _) => notified.TrySetResult();
        state = TorrentState.Stopped;

        WeakReferenceMessenger.Default.Send(
            new TorrentAlertMessage(torrent, new SimpleTorrentAlert { Id = AlertId.TorrentStateChanged, Torrent = torrent }));
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(_sut.RenameFileCommand.CanExecute(file));
    }

    [Fact]
    public async Task ScrapeCompletingAfterSelectionChanges_DoesNotReplaceTheNewTorrentsTrackers()
    {
        var first = CreateRefreshableTorrent("First", 1);
        var second = CreateRefreshableTorrent("Second", 2);
        var scrape = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Trackers
            .ScrapeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(scrape.Task);
        first.Trackers.GetTrackers().Returns([
            new TrackerStatus("https://first.invalid/announce", TrackerStatusType.Working)
        ]);
        _sut.Trackers.Add(new TrackerStatusItemViewModel(
            new TrackerStatus("https://second.invalid/announce", TrackerStatusType.Working)));

        _selectionService.SelectedTorrent = first;
        var command = _sut.ScrapeCommand.ExecuteAsync(null);
        _selectionService.SelectedTorrent = second;
        scrape.SetResult();
        await command;

        var tracker = Assert.Single(_sut.Trackers);
        Assert.Equal("https://second.invalid/announce", tracker.Url);
    }

    private static ITorrent CreateRefreshableTorrent(string name, byte hashSeed)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns(name);
        torrent.Hash.Returns(new InfoHash(Enumerable.Repeat(hashSeed, 20).ToArray()));
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        torrent.State.Returns(TorrentState.Active);

        var files = Substitute.For<IFiles>();
        files.DownloadPath.Returns("C:\\Downloads");
        torrent.Files.Returns(files);
        torrent.Peers.Returns(Substitute.For<IPeers>());
        torrent.Trackers.Returns(Substitute.For<ITrackers>());

        return torrent;
    }

    private async Task WaitForNameAsync(string expected)
    {
        for (var attempt = 0; attempt < 50 && _sut.Name != expected; attempt++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(expected, _sut.Name);
    }

    [Fact]
    public void AddPeers_OffersEveryAddressItCanRead_AndClearsTheBox()
    {
        var peers = SelectTorrentWithPeers();

        _sut.NewPeerAddresses = "192.168.1.10:51413, [::1]:6881\n10.0.0.5:6889";
        _sut.AddPeersCommand.Execute(null);

        peers.Received(1).Add(Arg.Is<IEnumerable<IPEndPoint>>(endPoints =>
            endPoints.Select(endPoint => endPoint.ToString()).SequenceEqual(ExpectedPeerAddresses)));
        Assert.Equal(string.Empty, _sut.NewPeerAddresses);
    }

    [Fact]
    public void AddPeers_OffersNothingWhenNoAddressCanBeRead()
    {
        var peers = SelectTorrentWithPeers();

        // A bare address names no port, and a hostname is not something the engine can dial.
        _sut.NewPeerAddresses = "192.168.1.10 seedbox.example:6881";
        _sut.AddPeersCommand.Execute(null);

        peers.DidNotReceive().Add(Arg.Any<IEnumerable<IPEndPoint>>());
        Assert.Equal("192.168.1.10 seedbox.example:6881", _sut.NewPeerAddresses);
        _notificationService.Received(1).Publish(
            Arg.Is<NotificationItem>(item => item.Type == NotificationType.Warning),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public void AddPeersCommand_IsDisabledWithoutAnAddress()
    {
        SelectTorrentWithPeers();

        Assert.False(_sut.AddPeersCommand.CanExecute(null));

        _sut.NewPeerAddresses = "192.168.1.10:51413";

        Assert.True(_sut.AddPeersCommand.CanExecute(null));
    }

    private IPeers SelectTorrentWithPeers()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray()));
        var peers = Substitute.For<IPeers>();
        torrent.Peers.Returns(peers);
        _selectionService.SelectedTorrent = torrent;
        return peers;
    }
}
