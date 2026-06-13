using Avalonia.Threading;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Core.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.HeadlessTests;

public sealed class DownloadsViewModelRuntimeTests
{
    [AvaloniaFact]
    public async Task Constructor_LoadsExistingTorrents()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Existing Torrent");
        torrent.Hash.Returns(new PeerSharp.Core.InfoHash(new byte[20]));
        torrent.HashV2.Returns(new PeerSharp.Core.InfoHash(new byte[32]));

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);
        torrentService.GetStats().Returns(new EngineStats());

        var vm = CreateViewModel(torrentService);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        var item = Assert.Single(vm.Torrents);
        Assert.Equal("Existing Torrent", item.Name);
        StopLoops(vm);
    }

    [AvaloniaFact]
    public async Task Constructor_WiresSelectionCommands_ForRealInstance()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Runtime Torrent");
        torrent.Hash.Returns(new PeerSharp.Core.InfoHash(new byte[20]));
        torrent.HashV2.Returns(new PeerSharp.Core.InfoHash(new byte[32]));
        torrent.State.Returns(TorrentState.Stopped);
        torrent.Started.Returns(false);

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetStats().Returns(new EngineStats());
        var vm = CreateViewModel(torrentService);
        var item = new TorrentListItemViewModel(torrent);

        vm.SelectedTorrent = item;
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.True(vm.StartSelectedCommand.CanExecute(null));
        Assert.False(vm.StopSelectedCommand.CanExecute(null));
        Assert.True(vm.RemoveSelectedCommand.CanExecute(null));
        StopLoops(vm);
    }

    private static DownloadsViewModel CreateViewModel(ITorrentService torrentService)
    {
        var selectionService = new Peerfluence.Core.Services.TorrentSelectionService(Substitute.For<IAppMessenger>());
        var localizationService = new Peerfluence.Services.LocalizationService();
        var topLevelService = Substitute.For<Peerfluence.Services.ITopLevelService>();
        var dialogService = Substitute.For<Peerfluence.Core.Services.IDialogService>();
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

    private static void StopLoops(DownloadsViewModel vm)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var cts = (CancellationTokenSource)typeof(DownloadsViewModel)
            .GetField("_loopCts", flags)!
            .GetValue(vm)!;
        cts.Cancel();
    }
}
