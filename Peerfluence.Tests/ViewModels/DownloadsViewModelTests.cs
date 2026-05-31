using System.Collections.ObjectModel;
using System.IO.Abstractions;
using System.Runtime.Serialization;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Config;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.ViewModels;

[Collection("Messenger")]
public class DownloadsViewModelTests
{
    private readonly ITorrentService _torrentService;
    private readonly TorrentSelectionService _selectionService = new(Substitute.For<IAppMessenger>());
    private readonly LocalizationService _localizationService = new();
    private readonly ITopLevelService _topLevelService = Substitute.For<ITopLevelService>();
    private readonly IDialogService _dialogService = Substitute.For<IDialogService>();
    private readonly IAddTorrentDialogService _addTorrentDialogService = Substitute.For<IAddTorrentDialogService>();
    private readonly IAppSettingsService _settingsService;
    private readonly DetailsViewModel _detailsVm;
    private readonly DownloadsViewModel _sut;

    public DownloadsViewModelTests()
    {
        WeakReferenceMessenger.Default.Reset();

        var store = Substitute.For<IAppSettingsStore>();
        var paths = new AppPaths();
        var settingsService = new AppSettingsService(paths, store, new FileSystem());
        _settingsService = settingsService;
        var loggerFactory = Substitute.For<Microsoft.Extensions.Logging.ILoggerFactory>();
        var engineService = new TorrentEngineService(settingsService, loggerFactory);
        _torrentService = new TorrentService(engineService, Substitute.For<IAppMessenger>());
        var notificationService = Substitute.For<INotificationService>();

        _detailsVm = new DetailsViewModel(
            _selectionService,
            _torrentService,
            _localizationService,
            notificationService,
            _topLevelService,
            settingsService);

        // Workaround for Dispatcher dependencies in constructor
#pragma warning disable SYSLIB0050
        _sut = (DownloadsViewModel)FormatterServices.GetUninitializedObject(typeof(DownloadsViewModel));
#pragma warning restore SYSLIB0050

        // Manually inject dependencies since we bypassed constructor
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var fields = typeof(DownloadsViewModel).GetFields(flags);

        fields.First(f => f.Name == "_torrentService").SetValue(_sut, _torrentService);
        fields.First(f => f.Name == "_selectionService").SetValue(_sut, _selectionService);
        fields.First(f => f.Name == "_localizationService").SetValue(_sut, _localizationService);
        fields.First(f => f.Name == "_topLevelService").SetValue(_sut, _topLevelService);
        fields.First(f => f.Name == "_dialogService").SetValue(_sut, _dialogService);
        fields.First(f => f.Name == "_addTorrentDialogService").SetValue(_sut, _addTorrentDialogService);
        fields.First(f => f.Name == "_settingsService").SetValue(_sut, _settingsService);

        fields.First(f => f.Name == "<SelectedTorrentDetailViewModel>k__BackingField").SetValue(_sut, _detailsVm);
        fields.First(f => f.Name == "<Torrents>k__BackingField").SetValue(_sut, new ObservableCollection<TorrentListItemViewModel>());

        // Initialize commands via backing fields
        fields.First(f => f.Name == "<AddTorrentCommand>k__BackingField").SetValue(_sut, new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask));
        fields.First(f => f.Name == "<AddMagnetCommand>k__BackingField").SetValue(_sut, new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask));
        fields.First(f => f.Name == "<ClearStatusCommand>k__BackingField").SetValue(_sut, new CommunityToolkit.Mvvm.Input.RelayCommand(() => _sut.StatusMessage = string.Empty));
        fields.First(f => f.Name == "<CreateTorrentCommand>k__BackingField").SetValue(_sut, new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask));

        var startCmd = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask, () => _sut.SelectedTorrent is { Torrent.State: TorrentState.Stopped });
        var stopCmd = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask, () => _sut.SelectedTorrent is { Torrent.Started: true });
        var removeCmd = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask, () => _sut.SelectedTorrent is not null);

        fields.First(f => f.Name == "<StartSelectedCommand>k__BackingField").SetValue(_sut, startCmd);
        fields.First(f => f.Name == "<StopSelectedCommand>k__BackingField").SetValue(_sut, stopCmd);
        fields.First(f => f.Name == "<RemoveSelectedCommand>k__BackingField").SetValue(_sut, removeCmd);
    }

    [Fact]
    public void SelectedTorrent_NullByDefault()
    {
        Assert.Null(_sut.SelectedTorrent);
    }

    [Fact]
    public void SelectedTorrent_UpdatesSelectionService()
    {
        var torrent = Substitute.For<ITorrent>();
        var vm = new TorrentListItemViewModel(torrent);

        _sut.SelectedTorrent = vm;

        Assert.Same(vm, _sut.SelectedTorrent);
        Assert.Same(torrent, _selectionService.SelectedTorrent);
    }

    [Fact]
    public void SelectedTorrent_Null_ClearsSelectionService()
    {
        var torrent = Substitute.For<ITorrent>();
        var vm = new TorrentListItemViewModel(torrent);
        _sut.SelectedTorrent = vm;

        _sut.SelectedTorrent = null;

        Assert.Null(_selectionService.SelectedTorrent);
    }

    [Fact]
    public void Commands_CanExecute_ReflectSelection()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.State.Returns(TorrentState.Stopped);
        torrent.Started.Returns(false);
        var vm = new TorrentListItemViewModel(torrent);

        Assert.False(_sut.StartSelectedCommand.CanExecute(null));
        Assert.False(_sut.StopSelectedCommand.CanExecute(null));
        Assert.False(_sut.RemoveSelectedCommand.CanExecute(null));

        _sut.SelectedTorrent = vm;

        Assert.True(_sut.StartSelectedCommand.CanExecute(null));
        Assert.False(_sut.StopSelectedCommand.CanExecute(null));
        Assert.True(_sut.RemoveSelectedCommand.CanExecute(null));

        torrent.Started.Returns(true);
        torrent.State.Returns(TorrentState.Active);

        Assert.False(_sut.StartSelectedCommand.CanExecute(null));
        Assert.True(_sut.StopSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddMagnetCommand_UsesClipboardWhenInputIsEmpty()
    {
        const string magnet = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(Array.Empty<ITorrent>());
        torrentService.GetStats().Returns(new EngineStats());

        var clipboard = CreateClipboard(magnet);

        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.GetClipboard().Returns(clipboard);

        var sut = new DownloadsViewModel(
            torrentService,
            new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(),
            topLevelService,
            Substitute.For<IDialogService>(),
            Substitute.For<IAddTorrentDialogService>(),
            _settingsService,
            _detailsVm);

        try
        {
            await sut.AddMagnetCommand.ExecuteAsync(null);

            var addTorrentDialogService = GetAddTorrentDialogService(sut);
            await addTorrentDialogService.Received(1).ShowMagnetAsync(magnet);
            Assert.Equal(string.Empty, sut.MagnetLink);
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public async Task AddMagnetCommand_DoesNotAddInvalidClipboardText()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(Array.Empty<ITorrent>());
        torrentService.GetStats().Returns(new EngineStats());

        var clipboard = CreateClipboard("not a magnet");

        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.GetClipboard().Returns(clipboard);

        var sut = new DownloadsViewModel(
            torrentService,
            new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(),
            topLevelService,
            Substitute.For<IDialogService>(),
            Substitute.For<IAddTorrentDialogService>(),
            _settingsService,
            _detailsVm);

        try
        {
            await sut.AddMagnetCommand.ExecuteAsync(null);

            var addTorrentDialogService = GetAddTorrentDialogService(sut);
            await addTorrentDialogService.DidNotReceive().ShowMagnetAsync(Arg.Any<string>());
            Assert.Equal(string.Empty, sut.StatusMessage);
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public void ToRemoveOptions_MapsActionToPeerSharpOptions()
    {
        Assert.Equal(RemoveOptions.None, DownloadsViewModel.ToRemoveOptions(DownloadsViewModel.RemoveTorrentAction.RemoveOnly));
        Assert.Equal(RemoveOptions.DeleteFiles, DownloadsViewModel.ToRemoveOptions(DownloadsViewModel.RemoveTorrentAction.DeleteFiles));
        Assert.Equal(RemoveOptions.DeleteTorrentFile, DownloadsViewModel.ToRemoveOptions(DownloadsViewModel.RemoveTorrentAction.DeleteMetadata));
        Assert.Equal(RemoveOptions.DeleteAll, DownloadsViewModel.ToRemoveOptions(DownloadsViewModel.RemoveTorrentAction.DeleteAll));
    }

    [Fact]
    public async Task RemoveSelectedCommand_WhenConfirmationIsDisabled_UsesRememberedAction()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(Array.Empty<ITorrent>());
        torrentService.GetStats().Returns(new EngineStats());

        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new Peerfluence.Core.Config.AppSettings
        {
            ShowRemoveTorrentOptions = false,
            DefaultRemoveTorrentAction = "DeleteAll"
        });

        var sut = new DownloadsViewModel(
            torrentService,
            new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(),
            Substitute.For<ITopLevelService>(),
            Substitute.For<IDialogService>(),
            Substitute.For<IAddTorrentDialogService>(),
            settingsService,
            _detailsVm);
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Test");
        torrent.Hash.Returns(InfoHash.CreateRandom());
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        torrent.State.Returns(TorrentState.Stopped);
        torrent.Started.Returns(false);
        torrent.TotalSize.Returns(100);
        torrent.HasMetadata.Returns(true);
        sut.SelectedTorrent = new TorrentListItemViewModel(torrent);

        try
        {
            await sut.RemoveSelectedCommand.ExecuteAsync(null);

            await torrentService.Received(1).RemoveAsync(torrent, RemoveOptions.DeleteAll, Arg.Any<CancellationToken>());
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public async Task RemoveSelectedCommand_WhenDialogManagerIsMissing_UsesSafeDefaultAction()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(Array.Empty<ITorrent>());
        torrentService.GetStats().Returns(new EngineStats());

        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new Peerfluence.Core.Config.AppSettings
        {
            ShowRemoveTorrentOptions = true,
            DefaultRemoveTorrentAction = "RemoveOnly"
        });

        var sut = new DownloadsViewModel(
            torrentService,
            new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(),
            Substitute.For<ITopLevelService>(),
            Substitute.For<IDialogService>(),
            Substitute.For<IAddTorrentDialogService>(),
            settingsService,
            _detailsVm);
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Test");
        torrent.Hash.Returns(InfoHash.CreateRandom());
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        torrent.State.Returns(TorrentState.Stopped);
        torrent.Started.Returns(false);
        torrent.TotalSize.Returns(100);
        torrent.HasMetadata.Returns(true);
        sut.SelectedTorrent = new TorrentListItemViewModel(torrent);

        try
        {
            await sut.RemoveSelectedCommand.ExecuteAsync(null);

            await torrentService.Received(1).RemoveAsync(torrent, RemoveOptions.None, Arg.Any<CancellationToken>());
        }
        finally
        {
            StopLoops(sut);
        }
    }

    private static void StopLoops(DownloadsViewModel vm)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var cts = (CancellationTokenSource)typeof(DownloadsViewModel)
            .GetField("_loopCts", flags)!
            .GetValue(vm)!;
        cts.Cancel();
    }

    private static IClipboard CreateClipboard(string? text)
    {
        var clipboard = Substitute.For<IClipboard>();
        IAsyncDataTransfer? dataTransfer = null;
        if (text != null)
        {
            var item = new DataTransferItem();
            item.SetText(text);

            var data = new DataTransfer();
            data.Add(item);
            dataTransfer = data;
        }

        clipboard.TryGetDataAsync().Returns(Task.FromResult(dataTransfer));
        return clipboard;
    }

    private static IAddTorrentDialogService GetAddTorrentDialogService(DownloadsViewModel vm)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        return (IAddTorrentDialogService)typeof(DownloadsViewModel)
            .GetField("_addTorrentDialogService", flags)!
            .GetValue(vm)!;
    }
}
