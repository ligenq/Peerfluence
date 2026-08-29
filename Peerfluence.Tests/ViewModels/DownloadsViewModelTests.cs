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
        _torrentService = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient());
        var notificationService = Substitute.For<INotificationService>();

        _detailsVm = new DetailsViewModel(
            _selectionService,
            _torrentService,
            _localizationService,
            notificationService,
            _topLevelService,
            settingsService,Substitute.For<IDialogService>());

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
            Substitute.For<ITorrentCategoryService>(),
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
            Substitute.For<ITorrentCategoryService>(),
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
    public async Task CopyHashCommand_PutsTheHashOnTheClipboardAndFlushesIt()
    {
        var clipboard = Substitute.For<IClipboard>();
        var sut = CreateViewModelWithSelectedTorrent(clipboard, out var torrent);

        try
        {
            await sut.CopyHashCommand.ExecuteAsync(null);

            Assert.Equal(torrent.Hash.ToString(), await CapturedTextAsync(clipboard));

            // Without the flush the text stays this process's to render on request, so it never
            // reaches another application and dies with Peerfluence.
            await clipboard.Received(1).FlushAsync();
            Assert.Equal(string.Empty, sut.StatusMessage);
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public async Task CopyMagnetCommand_PutsAMagnetForTheHashOnTheClipboard()
    {
        var clipboard = Substitute.For<IClipboard>();
        var sut = CreateViewModelWithSelectedTorrent(clipboard, out var torrent);

        try
        {
            await sut.CopyMagnetCommand.ExecuteAsync(null);

            Assert.Equal($"magnet:?xt=urn:btih:{torrent.Hash}", await CapturedTextAsync(clipboard));
            await clipboard.Received(1).FlushAsync();
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public async Task CopyHashCommand_SaysSoWhenTheClipboardRefusesTheWrite()
    {
        var clipboard = Substitute.For<IClipboard>();
        clipboard.SetDataAsync(Arg.Any<IAsyncDataTransfer>())
            .Returns(_ => Task.FromException(new InvalidOperationException("clipboard is busy")));
        var sut = CreateViewModelWithSelectedTorrent(clipboard, out _);

        try
        {
            await sut.CopyHashCommand.ExecuteAsync(null);

            Assert.Contains("clipboard is busy", sut.StatusMessage);
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public async Task CopyHashCommand_SaysSoWhenThereIsNoClipboardAtAll()
    {
        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.GetClipboard().Returns(_ => throw new InvalidOperationException("TopLevel has not been initialized."));
        var sut = CreateViewModelWithSelectedTorrent(topLevelService, out _);

        try
        {
            await sut.CopyHashCommand.ExecuteAsync(null);

            Assert.Equal(Properties.Resources.Downloads_ClipboardUnavailable, sut.StatusMessage);
        }
        finally
        {
            StopLoops(sut);
        }
    }

    private DownloadsViewModel CreateViewModelWithSelectedTorrent(IClipboard clipboard, out ITorrent torrent)
    {
        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.GetClipboard().Returns(clipboard);
        return CreateViewModelWithSelectedTorrent(topLevelService, out torrent);
    }

    private DownloadsViewModel CreateViewModelWithSelectedTorrent(ITopLevelService topLevelService, out ITorrent torrent)
    {
        var selected = Substitute.For<ITorrent>();
        selected.Name.Returns("Selected");
        selected.Hash.Returns(new InfoHash(Enumerable.Repeat((byte)0xAB, 20).ToArray()));
        selected.HashV2.Returns(InfoHash.EmptyV2);
        torrent = selected;

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(Array.Empty<ITorrent>());
        torrentService.GetStats().Returns(new EngineStats());

        var sut = new DownloadsViewModel(
            torrentService,
            new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(),
            topLevelService,
            Substitute.For<IDialogService>(),
            Substitute.For<IAddTorrentDialogService>(),
            _settingsService,
            Substitute.For<ITorrentCategoryService>(),
            _detailsVm)
        {
            SelectedTorrent = new TorrentListItemViewModel(selected)
        };

        return sut;
    }

    /// <summary>
    /// Reads back what was handed to the clipboard. <c>SetTextAsync</c> is an extension method and
    /// so cannot be received by a substitute; it wraps the text in a data transfer and calls
    /// <see cref="IClipboard.SetDataAsync"/>, which can.
    /// </summary>
    private static async Task<string?> CapturedTextAsync(IClipboard clipboard)
    {
        var call = clipboard.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IClipboard.SetDataAsync));
        var dataTransfer = (IAsyncDataTransfer)call.GetArguments()[0]!;
        return await dataTransfer.TryGetTextAsync();
    }

    [Fact]
    public void Search_NarrowsTheVisibleListWithoutTouchingTheRealOne()
    {
        var sut = CreateViewModelWithTorrents(
            ("ubuntu-24.04.iso", Complete: false, Running: true),
            ("debian-12.iso", Complete: false, Running: true));

        try
        {
            sut.SearchText = "ubu";

            Assert.Equal(["ubuntu-24.04.iso"], sut.VisibleTorrents.Select(t => t.Name));
            // The counts and the alert plumbing are about everything, not about what is on screen.
            Assert.Equal(2, sut.Torrents.Count);
            Assert.False(sut.HasNoMatches);
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public void SearchingEverythingAway_IsSaidDifferentlyFromHavingNothing()
    {
        var sut = CreateViewModelWithTorrents(("ubuntu-24.04.iso", Complete: false, Running: true));

        try
        {
            sut.SearchText = "nothing matches this";

            Assert.Empty(sut.VisibleTorrents);
            Assert.True(sut.HasNoMatches);
            Assert.True(sut.HasTorrents);
            Assert.False(sut.HasNoTorrents);
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Theory]
    [InlineData(TorrentFilter.All, new[] { "downloading", "seeding", "finished-and-stopped" })]
    [InlineData(TorrentFilter.Downloading, new[] { "downloading" })]
    [InlineData(TorrentFilter.Seeding, new[] { "seeding" })]
    [InlineData(TorrentFilter.Completed, new[] { "seeding", "finished-and-stopped" })]
    public void EachFilter_ShowsWhatItSays(TorrentFilter filter, string[] expected)
    {
        var sut = CreateViewModelWithTorrents(
            ("downloading", Complete: false, Running: true),
            ("seeding", Complete: true, Running: true),
            ("finished-and-stopped", Complete: true, Running: false));

        try
        {
            sut.SetFilterCommand.Execute(filter);

            Assert.Equal(expected, sut.VisibleTorrents.Select(t => t.Name));
        }
        finally
        {
            StopLoops(sut);
        }
    }

    private DownloadsViewModel CreateViewModelWithTorrents(params (string Name, bool Complete, bool Running)[] torrents)
    {
        var torrentService = Substitute.For<ITorrentService>();
        var built = torrents.Select(spec =>
        {
            var torrent = Substitute.For<ITorrent>();
            torrent.Name.Returns(spec.Name);
            torrent.Hash.Returns(InfoHash.CreateRandom());
            torrent.HashV2.Returns(InfoHash.EmptyV2);
            torrent.Progress.Returns(spec.Complete ? 1f : 0.5f);
            torrent.Started.Returns(spec.Running);
            torrent.State.Returns(spec.Running ? TorrentState.Active : TorrentState.Stopped);
            return torrent;
        }).ToArray();

        torrentService.GetTorrents().Returns(built);
        torrentService.GetStats().Returns(new EngineStats());

        return new DownloadsViewModel(
            torrentService,
            new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(),
            Substitute.For<ITopLevelService>(),
            Substitute.For<IDialogService>(),
            Substitute.For<IAddTorrentDialogService>(),
            _settingsService,
            Substitute.For<ITorrentCategoryService>(),
            _detailsVm);
    }

    [Fact]
    public async Task StartSelected_StartsEveryStoppedTorrentInTheSelection()
    {
        var sut = CreateViewModelWithTorrents(
            ("stopped-one", Complete: false, Running: false),
            ("stopped-two", Complete: false, Running: false),
            ("already-running", Complete: false, Running: true));

        try
        {
            sut.SetSelectedTorrents(sut.Torrents);

            Assert.True(sut.StartSelectedCommand.CanExecute(null));
            await sut.StartSelectedCommand.ExecuteAsync(null);

            // The running one is left alone rather than restarted.
            foreach (var item in sut.Torrents.Where(t => t.Name.StartsWith("stopped")))
            {
                await item.Torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
            }

            var running = sut.Torrents.Single(t => t.Name == "already-running");
            await running.Torrent.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public async Task StopSelected_StopsEveryRunningTorrentInTheSelection()
    {
        var sut = CreateViewModelWithTorrents(
            ("running-one", Complete: false, Running: true),
            ("running-two", Complete: true, Running: true));

        try
        {
            sut.SetSelectedTorrents(sut.Torrents);
            await sut.StopSelectedCommand.ExecuteAsync(null);

            foreach (var item in sut.Torrents)
            {
                await item.Torrent.Received(1).StopAsync(Arg.Any<CancellationToken>());
            }
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public void AMixedSelection_StillOffersBothStartAndStop()
    {
        var sut = CreateViewModelWithTorrents(
            ("stopped", Complete: false, Running: false),
            ("running", Complete: false, Running: true));

        try
        {
            sut.SetSelectedTorrents(sut.Torrents);

            // Enabled when any of the selection can be acted on, not only when all of it can.
            Assert.True(sut.StartSelectedCommand.CanExecute(null));
            Assert.True(sut.StopSelectedCommand.CanExecute(null));
            Assert.True(sut.RemoveSelectedCommand.CanExecute(null));
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public void WithNothingMultiSelected_TheCommandsStillFollowTheFocusedRow()
    {
        var sut = CreateViewModelWithTorrents(("stopped", Complete: false, Running: false));

        try
        {
            sut.SelectedTorrent = sut.Torrents[0];

            Assert.Empty(sut.SelectedTorrents);
            Assert.True(sut.StartSelectedCommand.CanExecute(null));
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public void EveryRow_CarriesTheActionsItsContextMenuNeeds()
    {
        // A context menu is a popup with its own visual tree, so the row has to hold the commands
        // rather than reach up for them - reaching up left every menu item disabled.
        var sut = CreateViewModelWithTorrents(("anything", Complete: false, Running: true));

        try
        {
            var row = Assert.Single(sut.Torrents);
            Assert.Same(sut, row.Actions);
            Assert.NotNull(row.Actions!.OpenTorrentFolderCommand);
            Assert.NotNull(row.Actions.RemoveTorrentCommand);
            Assert.NotNull(row.Actions.ToggleTorrentCommand);
        }
        finally
        {
            StopLoops(sut);
        }
    }

    [Fact]
    public void ToRemoveOptions_MapsActionToPeerSharpOptions()
    {
        Assert.Equal(RemoveOptions.None, DownloadsViewModel.ToRemoveOptions(RemoveTorrentAction.RemoveOnly));
        Assert.Equal(RemoveOptions.DeleteFiles, DownloadsViewModel.ToRemoveOptions(RemoveTorrentAction.DeleteFiles));
        Assert.Equal(RemoveOptions.DeleteTorrentFile, DownloadsViewModel.ToRemoveOptions(RemoveTorrentAction.DeleteMetadata));
        Assert.Equal(RemoveOptions.DeleteAll, DownloadsViewModel.ToRemoveOptions(RemoveTorrentAction.DeleteAll));
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
            Substitute.For<ITorrentCategoryService>(),
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
            // A previously remembered destructive choice must not be used when there is nowhere
            // to ask. The safe fallback is always to leave data on disk.
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
            Substitute.For<ITorrentCategoryService>(),
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

    [Fact]
    public async Task RemoveSelectedCommand_UsesTheDialogsChoiceInsteadOfTheRememberedDefault()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(Array.Empty<ITorrent>());
        torrentService.GetStats().Returns(new EngineStats());

        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new Peerfluence.Core.Config.AppSettings
        {
            ShowRemoveTorrentOptions = true,
            DefaultRemoveTorrentAction = "DeleteAll"
        });

        var dialogService = Substitute.For<IDialogService>();
        dialogService.CanPrompt.Returns(true);
        dialogService.PromptForRemoveOptionsAsync(Arg.Any<RemoveTorrentPrompt>())
            .Returns(new RemoveTorrentChoice(RemoveTorrentAction.RemoveOnly, RememberChoice: false));

        var sut = new DownloadsViewModel(
            torrentService,
            new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(),
            Substitute.For<ITopLevelService>(),
            dialogService,
            Substitute.For<IAddTorrentDialogService>(),
            settingsService,
            Substitute.For<ITorrentCategoryService>(),
            _detailsVm);
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Test");
        torrent.Hash.Returns(InfoHash.CreateRandom());
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        torrent.State.Returns(TorrentState.Stopped);
        torrent.TotalSize.Returns(100);
        torrent.HasMetadata.Returns(true);
        sut.SelectedTorrent = new TorrentListItemViewModel(torrent);

        try
        {
            await sut.RemoveSelectedCommand.ExecuteAsync(null);

            await torrentService.Received(1).RemoveAsync(
                torrent,
                RemoveOptions.None,
                Arg.Any<CancellationToken>());
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

    [Fact]
    public void WithNoCategoryChosen_TheListIsNotNarrowed()
    {
        SetCategoryFilter(_sut, string.Empty);

        Assert.False(_sut.HasCategoryFilter);
        Assert.True(_sut.IsAllCategories);
    }

    [Fact]
    public void WithACategoryChosen_TheListSaysItIsNarrowed()
    {
        // Set through the backing field for the same reason the whole class is built that way: the
        // property is set by a command this uninitialized instance does not carry. What is being
        // pinned down is that "no category" is the empty string rather than null, and that the two
        // chips - "All" and a named one - can never both be lit.
        SetCategoryFilter(_sut, "Films");

        Assert.True(_sut.HasCategoryFilter);
        Assert.False(_sut.IsAllCategories);
    }

    private static void SetCategoryFilter(DownloadsViewModel viewModel, string value)
    {
        typeof(DownloadsViewModel)
            .GetField("<CategoryFilter>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(viewModel, value);
    }

    [Fact]
    public void DisposingTheList_StopsItsLoopsAndCanHappenTwice()
    {
        // The window is closed once, but the host disposes the container afterwards, so a second
        // call has to be harmless rather than throwing on an already-cancelled source.
#pragma warning disable SYSLIB0050
        var sut = (DownloadsViewModel)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(DownloadsViewModel));
#pragma warning restore SYSLIB0050

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var fields = typeof(DownloadsViewModel).GetFields(flags);
        fields.First(f => f.Name == "<Torrents>k__BackingField").SetValue(sut, new ObservableCollection<TorrentListItemViewModel>());
        fields.First(f => f.Name == "_loopCts").SetValue(sut, new CancellationTokenSource());
        fields.First(f => f.Name == "_alertChannel").SetValue(
            sut,
            System.Threading.Channels.Channel.CreateBounded<TorrentAlertEventArgs>(
                new System.Threading.Channels.BoundedChannelOptions(1)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
                }));

        sut.Dispose();
        sut.Dispose();
    }

}
