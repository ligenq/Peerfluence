using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.HeadlessTests;

public sealed class DownloadsAlertFlowTests : IDisposable
{
    [AvaloniaFact]
    public async Task TorrentAddedAlert_ImmediatelyAddsTorrent_ToCollection()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(Array.Empty<ITorrent>());

        var vm = CreateRuntimeViewModel(torrentService);
        var torrent = CreateTorrent("Added Torrent", started: false);

        WeakReferenceMessenger.Default.Send(new TorrentAlertMessage(
            torrent,
            new SimpleTorrentAlert
            {
                Id = AlertId.TorrentAdded,
                Torrent = torrent
            }));

        await Dispatcher.UIThread.InvokeAsync(() => { });
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var item = Assert.Single(vm.Torrents);
        Assert.Equal("Added Torrent", item.Name);
        StopLoops(vm);
    }

    [AvaloniaFact]
    public async Task TransferStatsAlert_IsApplied_AfterDebouncedBatch()
    {
        var torrent = CreateTorrent("Stats Torrent", started: true, dataLeft: 1000);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });
        torrentService.GetStats().Returns(new EngineStats());

        var vm = CreateRuntimeViewModel(torrentService);

        WeakReferenceMessenger.Default.Send(new TorrentAlertMessage(
            torrent,
            new TransferStatsAlert
            {
                Id = AlertId.TransferStatsUpdated,
                Torrent = torrent,
                ConnectedPeers = 4,
                DownloadSpeed = 200,
                UploadSpeed = 50,
                Downloaded = 500,
                Uploaded = 250
            }));

        await Task.Delay(350, TestContext.Current.CancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        var item = Assert.Single(vm.Torrents);
        Assert.Equal(200, item.DownloadSpeedBytesPerSecond);
        Assert.Equal(50, item.UploadSpeedBytesPerSecond);
        Assert.Equal(4, item.ConnectedPeers);
        StopLoops(vm);
    }

    [AvaloniaFact]
    public async Task TorrentRemovedAlert_ClearsSelection_AndRemovesItem()
    {
        var torrent = CreateTorrent("Removed Torrent", started: false);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });
        torrentService.GetStats().Returns(new EngineStats());

        var vm = CreateRuntimeViewModel(torrentService);
        vm.SelectedTorrent = vm.Torrents[0];

        WeakReferenceMessenger.Default.Send(new TorrentAlertMessage(
            torrent,
            new SimpleTorrentAlert
            {
                Id = AlertId.TorrentRemoved,
                Torrent = torrent
            }));

        await Dispatcher.UIThread.InvokeAsync(() => { });
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Empty(vm.Torrents);
        Assert.Null(vm.SelectedTorrent);
        StopLoops(vm);
    }

    private static DownloadsViewModel CreateRuntimeViewModel(ITorrentService torrentService)
    {
        var selectionService = new Peerfluence.Core.Services.TorrentSelectionService(Substitute.For<IAppMessenger>());
        var localizationService = new Peerfluence.Services.LocalizationService();
        var topLevelService = Substitute.For<Peerfluence.Services.ITopLevelService>();
        var dialogService = Substitute.For<IDialogService>();
        var addTorrentDialogService = Substitute.For<Peerfluence.Services.IAddTorrentDialogService>();
        var settingsService = new AppSettingsService(new AppPaths(), Substitute.For<IAppSettingsStore>(), new System.IO.Abstractions.FileSystem());
        var detailsViewModel = TestHelpers.CreateDetailsViewModel();

        return new DownloadsViewModel(
            torrentService,
            selectionService,
            localizationService,
            topLevelService,
            dialogService,
            addTorrentDialogService,
            settingsService,
            detailsViewModel);
    }

    private static ITorrent CreateTorrent(string name, bool started, long dataLeft = 0)
    {
        var torrent = Substitute.For<ITorrent>();
        var hash = InfoHash.CreateRandom();
        torrent.Name.Returns(name);
        torrent.Hash.Returns(hash);
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        torrent.Started.Returns(started);
        torrent.State.Returns(started ? TorrentState.Active : TorrentState.Stopped);
        torrent.Progress.Returns(0.5f);
        torrent.TotalSize.Returns(2000);
        torrent.DataLeft.Returns(dataLeft);
        torrent.HasMetadata.Returns(true);
        return torrent;
    }

    private static void StopLoops(DownloadsViewModel vm)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var cts = (CancellationTokenSource)typeof(DownloadsViewModel)
            .GetField("_loopCts", flags)!
            .GetValue(vm)!;
        cts.Cancel();
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Reset();
    }
}
