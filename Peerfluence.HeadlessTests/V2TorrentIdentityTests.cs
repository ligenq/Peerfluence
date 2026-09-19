using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.HeadlessTests;

public sealed class V2TorrentIdentityTests : IDisposable
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly TorrentCategoryService _categories;

    public V2TorrentIdentityTests()
    {
        _settings.Current.Returns(new AppSettings { Storage = { DownloadPath = Path.GetTempPath() } });
        _categories = new TorrentCategoryService(_settings, new AppMessenger());
    }

    [AvaloniaFact]
    public async Task RemovingAV2Torrent_RemovesOnlyItsRowAndClearsSelection()
    {
        var first = Torrent(1);
        var second = Torrent(2);
        using var vm = Downloads(first, second);
        vm.SelectedTorrent = vm.Torrents[0];

        WeakReferenceMessenger.Default.Send(new TorrentAlertMessage(first,
            new SimpleTorrentAlert { Id = AlertId.TorrentRemoved, Torrent = first }));
        await Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Same(second, Assert.Single(vm.Torrents).Torrent);
        Assert.Null(vm.SelectedTorrent);
    }

    [AvaloniaFact]
    public async Task V2Categories_AreIndependentAndSurviveReloadingTheList()
    {
        var first = Torrent(1);
        var second = Torrent(2);
        await _categories.AddAsync("Films", string.Empty, TestContext.Current.CancellationToken);
        await _categories.AddAsync("Music", string.Empty, TestContext.Current.CancellationToken);

        using (var vm = Downloads(first, second))
        {
            vm.SetSelectedTorrents([vm.Torrents[0]]);
            await vm.AssignCategoryCommand.ExecuteAsync("Films");
            vm.SetSelectedTorrents([vm.Torrents[1]]);
            await vm.AssignCategoryCommand.ExecuteAsync("Music");

            Assert.Equal("Films", vm.Torrents[0].Category);
            Assert.Equal("Music", vm.Torrents[1].Category);
            Assert.Equal("Films", _categories.GetCategory(first.HashV2));
            Assert.Equal("Music", _categories.GetCategory(second.HashV2));
        }

        using var reloaded = Downloads(first, second);
        Assert.Equal("Films", reloaded.Torrents[0].Category);
        Assert.Equal("Music", reloaded.Torrents[1].Category);
    }

    [AvaloniaFact]
    public async Task V2TransferAlerts_InTheSameBatchUpdateBothRows()
    {
        var first = Torrent(1);
        var second = Torrent(2);
        using var vm = Downloads(first, second);

        SendStats(first, 100);
        SendStats(second, 200);
        for (var attempt = 0; attempt < 100 && vm.Torrents.Any(row => row.DownloadSpeedBytesPerSecond == 0); attempt++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal(100, vm.Torrents[0].DownloadSpeedBytesPerSecond);
        Assert.Equal(200, vm.Torrents[1].DownloadSpeedBytesPerSecond);
    }

    [AvaloniaFact]
    public async Task AddingAV2Magnet_KeepsTheChosenCategory()
    {
        var torrent = Torrent(1);
        var service = Substitute.For<ITorrentService>();
        service.AddMagnetAsync(Arg.Any<string>(), Arg.Any<PeerSharp.Config.AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(torrent);
        await _categories.AddAsync("Films", string.Empty, TestContext.Current.CancellationToken);
        var vm = AddTorrentOptionsViewModel.CreateForMagnet(
            $"magnet:?xt=urn:btmh:1220{torrent.HashV2.ToHexString()}", service,
            Substitute.For<ITopLevelService>(), _settings, _categories);
        vm.SelectedCategory = "Films";

        await vm.AddCommand.ExecuteAsync(null);

        Assert.True(vm.WasAdded);
        Assert.Equal("Films", _categories.GetCategory(torrent.HashV2));
    }

    private DownloadsViewModel Downloads(params ITorrent[] torrents)
    {
        var service = Substitute.For<ITorrentService>();
        service.GetTorrents().Returns(torrents);
        service.GetStats().Returns(new EngineStats());
        return new DownloadsViewModel(service, new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(), Substitute.For<ITopLevelService>(), Substitute.For<IDialogService>(),
            Substitute.For<IAddTorrentDialogService>(), _settings, _categories, TestHelpers.CreateDetailsViewModel());
    }

    private static ITorrent Torrent(byte seed)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns($"V2 torrent {seed}");
        torrent.Started.Returns(true);
        torrent.State.Returns(TorrentState.Active);
        torrent.DataLeft.Returns(1000);
        torrent.Hash.Returns(InfoHash.Empty);
        torrent.HashV2.Returns(new InfoHash(Enumerable.Repeat(seed, InfoHash.V2Length).ToArray()));
        return torrent;
    }

    private static void SendStats(ITorrent torrent, long speed) =>
        WeakReferenceMessenger.Default.Send(new TorrentAlertMessage(torrent, new TransferStatsAlert
        {
            Id = AlertId.TransferStatsUpdated,
            Torrent = torrent,
            DownloadSpeed = speed,
            UploadSpeed = 0,
            Downloaded = 0,
            Uploaded = 0,
            ConnectedPeers = 0
        }));

    public void Dispose() => WeakReferenceMessenger.Default.Reset();
}
