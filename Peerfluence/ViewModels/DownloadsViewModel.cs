using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Messaging;
using PeerSharp.Interfaces;
using PeerSharp.Config;
using SukiUI.Dialogs;
using SukiUI.Toasts;

using System.Linq;
using Avalonia.Platform.Storage;
using System.Threading;
using System.IO;
using System.Diagnostics;
using Avalonia.Input.Platform;
using Peerfluence.Properties;

namespace Peerfluence.ViewModels;

[SingletonService]
public sealed class DownloadsViewModel : ViewModelBase, IFeatureViewModel, ITorrentRowActions, IDisposable
{
    private static readonly TimeSpan StatusAutoClearDelay = TimeSpan.FromSeconds(4);
    private readonly Dictionary<string, TorrentListItemViewModel> _torrentLookup = new();
    private readonly ITorrentSelectionService _selectionService;
    private readonly ITorrentService _torrentService;
    private readonly ILocalizationService _localizationService;
    private readonly ITopLevelService _topLevelService;
    private readonly IDialogService _dialogService;
    private readonly IAddTorrentDialogService _addTorrentDialogService;
    private readonly IAppSettingsService _settingsService;
    private readonly ITorrentCategoryService _categoryService;
    private readonly Channel<TorrentAlertEventArgs> _alertChannel;
    private readonly CancellationTokenSource _loopCts = new();
    private readonly Task _alertTask;
    private readonly Task _statsTask;
    private CancellationTokenSource? _statusAutoClearCts;
    private bool _disposed;

    public DownloadsViewModel(
        ITorrentService torrentService,
        ITorrentSelectionService selectionService,
        ILocalizationService localizationService,
        ITopLevelService topLevelService,
        IDialogService dialogService,
        IAddTorrentDialogService addTorrentDialogService,
        IAppSettingsService settingsService,
        ITorrentCategoryService categoryService,
        DetailsViewModel detailsViewModel)
    {
        _torrentService = torrentService;
        _selectionService = selectionService;
        _localizationService = localizationService;
        _topLevelService = topLevelService;
        _dialogService = dialogService;
        _addTorrentDialogService = addTorrentDialogService;
        _settingsService = settingsService;
        _categoryService = categoryService;
        SelectedTorrentDetailViewModel = detailsViewModel;

        Torrents = new ObservableCollection<TorrentListItemViewModel>();
        Torrents.CollectionChanged += OnTorrentsCollectionChanged;

        AddTorrentCommand = new AsyncRelayCommand(AddTorrentAsync);
        AddMagnetCommand = new AsyncRelayCommand(AddMagnetAsync);
        ClearStatusCommand = new RelayCommand(ClearStatusMessage);
        CreateTorrentCommand = new AsyncRelayCommand(ShowCreateTorrentAsync);
        StartSelectedCommand = new AsyncRelayCommand(StartSelectedAsync, CanStartSelected);
        StopSelectedCommand = new AsyncRelayCommand(StopSelectedAsync, CanStopSelected);
        RemoveSelectedCommand = new AsyncRelayCommand(RemoveSelectedAsync, CanRemoveSelected);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => SelectedTorrent != null);
        CopyHashCommand = new AsyncRelayCommand(CopyHashAsync, () => SelectedTorrent != null);
        CopyMagnetCommand = new AsyncRelayCommand(CopyMagnetAsync, () => SelectedTorrent != null);
        ForceRecheckCommand = new AsyncRelayCommand(ForceRecheckSelectedAsync, CanForceRecheckSelected);
        ToggleDetailsPaneCommand = new RelayCommand(ToggleDetailsPane);
        SetFilterCommand = new RelayCommand<TorrentFilter>(filter => Filter = filter);
        SetCategoryFilterCommand = new RelayCommand<string?>(SetCategoryFilter);
        AssignCategoryCommand = new AsyncRelayCommand<string?>(AssignCategoryToSelectionAsync);
        ToggleTorrentCommand = new AsyncRelayCommand<TorrentListItemViewModel?>(ToggleTorrentAsync);
        OpenTorrentFolderCommand = new RelayCommand<TorrentListItemViewModel?>(OpenFolderFor);
        RemoveTorrentCommand = new AsyncRelayCommand<TorrentListItemViewModel?>(RemoveTorrentAsync);
        ToggleSessionPauseCommand = new AsyncRelayCommand(ToggleSessionPauseAsync);

        IsDetailsPaneVisible = _settingsService.Current.ShowDetailsPane;

        WeakReferenceMessenger.Default.Register<CategoriesChangedMessage>(this, (_, _) => RefreshCategories());
        WeakReferenceMessenger.Default.Register<TorrentAlertMessage>(this, (_, msg) => OnTorrentAlert(msg));
        WeakReferenceMessenger.Default.Register<ActivationRequestedMessage>(this, (_, msg) =>
        {
            _ = Dispatcher.UIThread.InvokeAsync(async () => await HandleActivationAsync(msg.Arguments));
        });

        LoadExistingTorrents();

        _alertChannel = Channel.CreateBounded<TorrentAlertEventArgs>(new BoundedChannelOptions(10000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        UpdateTorrentPresence();
        _alertTask = Task.Run(() => RunAlertLoopAsync(_loopCts.Token));
        _statsTask = Task.Run(() => RunStatsLoopAsync(_loopCts.Token));
    }

    public ObservableCollection<TorrentListItemViewModel> Torrents { get; }

    public DetailsViewModel SelectedTorrentDetailViewModel { get; }

    public TorrentListItemViewModel? SelectedTorrent
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _selectionService.SelectedTorrent = value?.Torrent;
                StartSelectedCommand.NotifyCanExecuteChanged();
                StopSelectedCommand.NotifyCanExecuteChanged();
                RemoveSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string MagnetLink
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string StatusMessage
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    } = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>
    /// Whether the details pane is open. Setting it does not persist the choice or wake the pane up;
    /// <see cref="ToggleDetailsPane"/> is what a user action goes through.
    /// </summary>
    public bool IsDetailsPaneVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool HasTorrents => Torrents.Count > 0;

    public bool HasNoTorrents => !HasTorrents;

    /// <summary>
    /// What the list is narrowed to. The collection the grid binds to is
    /// <see cref="VisibleTorrents"/>; <see cref="Torrents"/> stays the whole set, because the
    /// dashboard counts and the alert plumbing are about everything, not about what is on screen.
    /// </summary>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ApplyFilter();
            }
        }
    } = string.Empty;

    public TorrentFilter Filter
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ApplyFilter();
            }
        }
    } = TorrentFilter.All;

    public ObservableCollection<TorrentListItemViewModel> VisibleTorrents { get; } = new();

    /// <summary>
    /// Every row the user has selected, which is not always one.
    ///
    /// <para>
    /// <see cref="SelectedTorrent"/> stays the focused row, because the details pane and the copy
    /// commands are about a single torrent. Start, stop and remove act on all of these instead:
    /// stopping fifty torrents one at a time is not a thing to ask of anyone.
    /// </para>
    /// </summary>
    public IReadOnlyList<TorrentListItemViewModel> SelectedTorrents
    {
        get;
        private set
        {
            field = value;
            StartSelectedCommand.NotifyCanExecuteChanged();
            StopSelectedCommand.NotifyCanExecuteChanged();
            RemoveSelectedCommand.NotifyCanExecuteChanged();
        }
    } = [];

    /// <summary>
    /// Called by the view as the grid's selection changes. Falls back to the focused row so every
    /// caller that only ever sets <see cref="SelectedTorrent"/> keeps working.
    /// </summary>
    internal void SetSelectedTorrents(IEnumerable<TorrentListItemViewModel> selection)
    {
        SelectedTorrents = selection.ToList();
    }

    private IReadOnlyList<TorrentListItemViewModel> SelectionOrFocused()
    {
        if (SelectedTorrents.Count > 0)
        {
            return SelectedTorrents;
        }

        return SelectedTorrent == null ? [] : [SelectedTorrent];
    }

    /// <summary>
    /// True when there are torrents but the search or filter has hidden all of them - a different
    /// thing to say than "nothing here yet", and a different thing to do about it.
    /// </summary>
    public bool HasNoMatches => HasTorrents && VisibleTorrents.Count == 0;

    public IRelayCommand<TorrentFilter> SetFilterCommand { get; }

    /// <summary>Narrows the list to one category, or back to all of them when given nothing.</summary>
    public IRelayCommand<string?> SetCategoryFilterCommand { get; }

    /// <summary>
    /// Files everything selected under a category, or unfiles it when given nothing. Works on the
    /// whole selection, because filing a batch is the reason anyone opens this menu.
    /// </summary>
    public IAsyncRelayCommand<string?> AssignCategoryCommand { get; }

    /// <summary>The categories on offer, refreshed whenever they change.</summary>
    public ObservableCollection<string> Categories { get; } = new();

    /// <summary>Which category the list is narrowed to, or empty for all of them.</summary>
    public string CategoryFilter
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasCategoryFilter));
                OnPropertyChanged(nameof(IsAllCategories));
                OnPropertyChanged(nameof(IsAllCategories));
                ApplyFilter();
            }
        }
    } = string.Empty;

    public bool HasCategoryFilter => CategoryFilter.Length > 0;

    /// <summary>Whether the list is showing every category, which is the state of the "All" chip.</summary>
    public bool IsAllCategories
    {
        get => CategoryFilter.Length == 0;
        set
        {
            if (value)
            {
                SetCategoryFilter(null);
            }
        }
    }

    /// <summary>
    /// Whether there is anything to filter by. Nobody who has not defined a category should be shown
    /// a row of category chips.
    /// </summary>
    public bool HasCategories => Categories.Count > 0;

    private void SetCategoryFilter(string? category)
    {
        CategoryFilter = category ?? string.Empty;
    }

    private async Task AssignCategoryToSelectionAsync(string? category)
    {
        // A copy, because assigning saves and announces, and the announcement rebuilds the list.
        foreach (var item in SelectedTorrents.ToList())
        {
            await _categoryService.AssignAsync(item.Hash, category).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Re-reads the categories and what each row is filed under. Cheap, and simpler than working out
    /// which rows a change touched.
    /// </summary>
    private void RefreshCategories()
    {
        Categories.Clear();
        foreach (var category in _categoryService.Categories)
        {
            Categories.Add(category.Name);
        }

        OnPropertyChanged(nameof(HasCategories));

        foreach (var item in Torrents)
        {
            item.Category = _categoryService.GetCategory(item.Hash) ?? string.Empty;
        }

        // A category that has just been removed cannot go on filtering the list.
        if (CategoryFilter.Length > 0 && !Categories.Contains(CategoryFilter, StringComparer.OrdinalIgnoreCase))
        {
            CategoryFilter = string.Empty;
            return;
        }

        ApplyFilter();
    }

    // These are written to as well as read, so that checking a chip is what changes the filter.
    // They used to be read only, with the work done by a command bound to the button's click, and
    // that left the chips unusable by anything that selects a radio button without clicking it -
    // which is how assistive technology selects one. See ChooseWhenChecked.

    public bool IsFilterAll
    {
        get => Filter == TorrentFilter.All;
        set => ChooseWhenChecked(value, TorrentFilter.All);
    }

    public bool IsFilterDownloading
    {
        get => Filter == TorrentFilter.Downloading;
        set => ChooseWhenChecked(value, TorrentFilter.Downloading);
    }

    public bool IsFilterSeeding
    {
        get => Filter == TorrentFilter.Seeding;
        set => ChooseWhenChecked(value, TorrentFilter.Seeding);
    }

    public bool IsFilterCompleted
    {
        get => Filter == TorrentFilter.Completed;
        set => ChooseWhenChecked(value, TorrentFilter.Completed);
    }

    /// <summary>
    /// Applies the filter a chip stands for, when that chip is the one being checked.
    /// </summary>
    /// <remarks>
    /// Only when checked. Choosing one member of a radio group unchecks the rest, so every choice
    /// arrives here as one true and one false, in no guaranteed order. Acting on the false would
    /// mean acting on the chip being left behind.
    /// </remarks>
    private void ChooseWhenChecked(bool isChecked, TorrentFilter filter)
    {
        if (isChecked)
        {
            Filter = filter;
        }
    }

    private void ApplyFilter()
    {
        var search = SearchText?.Trim() ?? string.Empty;

        VisibleTorrents.Clear();
        foreach (var torrent in Torrents.Where(Matches))
        {
            VisibleTorrents.Add(torrent);
        }

        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterDownloading));
        OnPropertyChanged(nameof(IsFilterSeeding));
        OnPropertyChanged(nameof(IsFilterCompleted));
        OnPropertyChanged(nameof(HasCategoryFilter));

        bool Matches(TorrentListItemViewModel item)
        {
            if (search.Length > 0 && item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (CategoryFilter is { Length: > 0 } category &&
                !string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Filter switch
            {
                TorrentFilter.Downloading => !item.IsComplete && item.IsRunning,
                TorrentFilter.Seeding => item.IsComplete && item.IsRunning,
                TorrentFilter.Completed => item.IsComplete,
                _ => true
            };
        }
    }

    public bool IsBusy
    {
        get;
        set => SetProperty(ref field, value);
    }

    public long TotalDownloadSpeedBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public long TotalUploadSpeedBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int ActiveTorrents
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int TotalPeers
    {
        get;
        set => SetProperty(ref field, value);
    }

    public IAsyncRelayCommand AddTorrentCommand { get; }

    public IAsyncRelayCommand AddMagnetCommand { get; }

    public IRelayCommand ClearStatusCommand { get; }

    public IAsyncRelayCommand CreateTorrentCommand { get; }

    public IAsyncRelayCommand StartSelectedCommand { get; }

    public IAsyncRelayCommand StopSelectedCommand { get; }

    public IAsyncRelayCommand RemoveSelectedCommand { get; }

    public IRelayCommand OpenFolderCommand { get; }

    public IAsyncRelayCommand CopyHashCommand { get; }

    public IAsyncRelayCommand CopyMagnetCommand { get; }

    public IAsyncRelayCommand ForceRecheckCommand { get; }

    public IRelayCommand ToggleDetailsPaneCommand { get; }

    /// <summary>Row actions, so simple mode needs no selection before acting.</summary>
    public IAsyncRelayCommand<TorrentListItemViewModel?> ToggleTorrentCommand { get; }

    public IRelayCommand<TorrentListItemViewModel?> OpenTorrentFolderCommand { get; }

    public IAsyncRelayCommand<TorrentListItemViewModel?> RemoveTorrentCommand { get; }

    /// <summary>Stops everything that is running, or starts back exactly what it stopped.</summary>
    public IAsyncRelayCommand ToggleSessionPauseCommand { get; }

    /// <summary>
    /// Whether the session is paused. Drives the toolbar button's label and icon, so it is a
    /// property rather than a read straight through to the engine on each bind.
    /// </summary>
    public bool IsSessionPaused
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Opens or closes the details pane and remembers the choice. Opening refreshes the pane by
    /// hand: while it was closed it ignored the alerts it would normally have redrawn itself from,
    /// so without this it would come back holding whatever it last saw.
    /// </summary>
    private void ToggleDetailsPane()
    {
        IsDetailsPaneVisible = !IsDetailsPaneVisible;
        _settingsService.Current.ShowDetailsPane = IsDetailsPaneVisible;
        _ = _settingsService.SaveAsync(default);

        if (IsDetailsPaneVisible)
        {
            SelectedTorrentDetailViewModel.RefreshFromSelection();
        }
    }

    private async Task AddTorrentAsync()
    {
        var storageProvider = _topLevelService.GetStorageProvider();

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Resources.Downloads_FilePicker_Title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Resources.Downloads_FilePicker_Filter)
                {
                    Patterns = ["*.torrent"]
                }
            ]
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file != null)
        {
            await AddTorrentFileAsync(file.Path.LocalPath);
        }
    }

    public async Task AddTorrentFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var wasAdded = await _addTorrentDialogService.ShowTorrentFileAsync(path);
            if (wasAdded)
            {
                SetStatusMessage(Resources.Status_TorrentAdded, autoClear: true);
            }
        }
        catch (Exception ex)
        {
            SetStatusMessage(string.Format(Resources.Status_AddTorrentFailed, ex.Message));
        }
    }

    public async Task AddMagnetUriAsync(string magnet)
    {
        if (!TryNormalizeMagnetLink(magnet, out magnet, out var error))
        {
            SetStatusMessage(string.Format(Resources.Status_AddMagnetFailed, error));
            return;
        }

        try
        {
            var wasAdded = await _addTorrentDialogService.ShowMagnetAsync(magnet);
            if (wasAdded)
            {
                SetStatusMessage(Resources.Status_MagnetAdded, autoClear: true);
                MagnetLink = string.Empty;
            }
        }
        catch (Exception ex)
        {
            SetStatusMessage(string.Format(Resources.Status_AddMagnetFailed, ex.Message));
        }
    }

    private async Task HandleActivationAsync(IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                await AddMagnetUriAsync(argument);
                continue;
            }

            if (string.Equals(Path.GetExtension(argument), ".torrent", StringComparison.OrdinalIgnoreCase))
            {
                await AddTorrentFileAsync(argument);
            }
        }
    }

    // Enabled when any of the selection can be acted on, not when all of it can: selecting a mix
    // of running and stopped torrents and pressing Start should start the stopped ones.
    private bool CanStartSelected()
    {
        return SelectionOrFocused().Any(item => item.Torrent.State == TorrentState.Stopped);
    }

    private bool CanStopSelected()
    {
        return SelectionOrFocused().Any(item => item.Torrent.Started);
    }

    private bool CanRemoveSelected()
    {
        return SelectionOrFocused().Count > 0;
    }

    private void LoadExistingTorrents()
    {
        foreach (var torrent in _torrentService.GetTorrents())
        {
            AddOrUpdateTorrent(torrent);
        }
    }

    private async Task RunStatsLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var stats = _torrentService.GetStats();
                Dispatcher.UIThread.Post(() => ApplyStats(stats));
            }
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (ct.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested) { }
    }

    private void ApplyStats(EngineStats stats)
    {
        TotalDownloadSpeedBytesPerSecond = UpdateSmoothedSpeed(
            stats.DownloadSpeed,
            ref _smoothedTotalDownloadSpeedBytesPerSecond,
            ref _totalDownloadSpeedSamples);
        TotalUploadSpeedBytesPerSecond = UpdateSmoothedSpeed(
            stats.UploadSpeed,
            ref _smoothedTotalUploadSpeedBytesPerSecond,
            ref _totalUploadSpeedSamples);
        ActiveTorrents = stats.ActiveTorrents;
        TotalPeers = stats.TotalPeers;
        IsSessionPaused = _torrentService.IsSessionPaused;
    }


    private const int SpeedSmoothingWindow = 3;
    private double _smoothedTotalDownloadSpeedBytesPerSecond;
    private double _smoothedTotalUploadSpeedBytesPerSecond;
    private int _totalDownloadSpeedSamples;
    private int _totalUploadSpeedSamples;

    private static long UpdateSmoothedSpeed(long current, ref double smoothed, ref int samples)
    {
        if (samples == 0)
        {
            smoothed = current;
            samples = 1;
            return current;
        }

        if (current == 0 && smoothed > 0 && samples > 1)
        {
            smoothed *= 0.7;
            if (smoothed < 1)
            {
                smoothed = 0;
            }
            samples++;
            return (long)Math.Round(smoothed);
        }

        double alpha = 2.0 / (SpeedSmoothingWindow + 1);
        smoothed = (alpha * current) + ((1.0 - alpha) * smoothed);
        samples++;
        return (long)Math.Round(smoothed);
    }

    private async Task AddMagnetAsync()
    {
        var magnet = await TryGetMagnetFromClipboardAsync();

        if (!IsValidMagnetLink(magnet))
        {
            magnet = await PromptForMagnetLinkAsync();
        }

        if (string.IsNullOrWhiteSpace(magnet))
        {
            return;
        }

        if (!TryNormalizeMagnetLink(magnet, out magnet, out var error))
        {
            SetStatusMessage(string.Format(Resources.Status_AddMagnetFailed, error));
            return;
        }

        await AddMagnetUriAsync(magnet);
    }

    /// <summary>
    /// Stops every running torrent, or starts back the ones a previous pause stopped.
    /// </summary>
    /// <remarks>
    /// The engine keeps running either way - the DHT node, the listeners and the port mappings all
    /// stay up - so this is not a way of going offline, it is a way of stopping the transfers.
    /// </remarks>
    private async Task ToggleSessionPauseAsync()
    {
        try
        {
            if (_torrentService.IsSessionPaused)
            {
                await _torrentService.ResumeSessionAsync();
                SetStatusMessage(Resources.Status_SessionResumed, autoClear: true);
            }
            else
            {
                await _torrentService.PauseSessionAsync();
                SetStatusMessage(Resources.Status_SessionPaused, autoClear: true);
            }

            IsSessionPaused = _torrentService.IsSessionPaused;
        }
        catch (Exception ex)
        {
            SetStatusMessage(ex.Message);
        }
    }

    private void SetStatusMessage(string message, bool autoClear = false)
    {
        CancelStatusAutoClear();
        StatusMessage = message;

        if (!autoClear || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _statusAutoClearCts = cts;
        _ = ClearStatusMessageAfterDelayAsync(message, cts);
    }

    private async Task ClearStatusMessageAfterDelayAsync(string message, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(StatusAutoClearDelay, cts.Token).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (!cts.IsCancellationRequested && ReferenceEquals(_statusAutoClearCts, cts) && StatusMessage == message)
                {
                    ClearStatusMessage();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ClearStatusMessage()
    {
        CancelStatusAutoClear();
        StatusMessage = string.Empty;
    }

    private void CancelStatusAutoClear()
    {
        var cts = _statusAutoClearCts;
        if (cts == null)
        {
            return;
        }

        _statusAutoClearCts = null;
        cts.Cancel();
        cts.Dispose();
    }

    private async Task<string?> PromptForMagnetLinkAsync()
    {
        var typed = await _dialogService.PromptForTextAsync(new TextPrompt(
            Resources.Downloads_AddMagnet,
            Resources.Downloads_AddMagnet,
            InitialText: MagnetLink,
            Watermark: Resources.Downloads_MagnetWatermark));

        if (typed is null)
        {
            return null;
        }

        MagnetLink = typed;
        return MagnetLink;
    }

    private static bool IsValidMagnetLink(string? magnet)
    {
        return TryNormalizeMagnetLink(magnet, out _, out _);
    }

    private static bool TryNormalizeMagnetLink(string? magnet, out string normalized, out string? error)
    {
        normalized = magnet?.Trim() ?? string.Empty;
        if (!PeerSharp.Core.MagnetLink.TryParse(normalized, out var parsed, out error))
        {
            return false;
        }

        if (parsed is null)
        {
            return false;
        }

        // Parsing is not enough: a BEP 46 link parses without an info hash, and everything
        // downstream of here assumes there is one.
        if (!TorrentService.HasUsableInfoHash(parsed))
        {
            error = TorrentService.MagnetWithoutInfoHashMessage;
            return false;
        }

        return true;
    }

    private Task ShowCreateTorrentAsync()
    {
        return _dialogService.ShowAsync<CreateTorrentViewModel>();
    }

    private async Task StartSelectedAsync()
    {
        foreach (var item in SelectionOrFocused().Where(item => item.Torrent.State == TorrentState.Stopped))
        {
            await TorrentService.StartAsync(item.Torrent);
        }
    }

    private async Task StopSelectedAsync()
    {
        foreach (var item in SelectionOrFocused().Where(item => item.Torrent.Started))
        {
            await TorrentService.StopAsync(item.Torrent);
        }
    }

    /// <summary>
    /// Removes everything selected. One torrent gets the usual dialog naming it; several are
    /// confirmed once, together, rather than asking the same question over and over.
    /// </summary>
    private async Task RemoveSelectedAsync()
    {
        var selection = SelectionOrFocused();
        if (selection.Count <= 1)
        {
            await RemoveTorrentAsync(selection.Count > 0 ? selection[0] : null);
            return;
        }

        var removeAction = GetDefaultRemoveAction();
        if (_settingsService.Current.ShowRemoveTorrentOptions
            && !await ConfirmRemoveManyAsync(selection.Count))
        {
            return;
        }

        foreach (var item in selection)
        {
            await _torrentService.RemoveAsync(item.Torrent, ToRemoveOptions(removeAction));
        }
    }

    private Task<bool> ConfirmRemoveManyAsync(int count)
    {
        // Nowhere to ask means the window is not up, which happens only at startup and in tests.
        // Going ahead is what this did before the prompt moved behind the service, and changing it
        // in a refactor would be changing it by accident.
        if (!_dialogService.CanPrompt)
        {
            return Task.FromResult(true);
        }

        return _dialogService.ConfirmAsync(new ConfirmPrompt(
            Resources.Downloads_Remove_Confirm_Title,
            string.Format(Resources.Downloads_Remove_Confirm_Many, count),
            Resources.Downloads_Remove,
            Resources.Common_Cancel));
    }

    private async Task RemoveTorrentAsync(TorrentListItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        var torrent = item.Torrent;
        var torrentName = item.Name;
        var removeAction = GetDefaultRemoveAction();

        if (!_settingsService.Current.ShowRemoveTorrentOptions)
        {
            await _torrentService.RemoveAsync(torrent, ToRemoveOptions(removeAction));
            return;
        }

        if (!_dialogService.CanPrompt)
        {
            // Cannot ask which files to delete, so delete none of them: remove the torrent and
            // leave what it downloaded alone.
            await _torrentService.RemoveAsync(torrent, RemoveOptions.None);
            return;
        }

        var choice = await _dialogService.PromptForRemoveOptionsAsync(new RemoveTorrentPrompt(
            Resources.Downloads_Remove_Confirm_Title,
            string.Format(Resources.Downloads_Remove_Confirm_Message, torrentName),
            Resources.Downloads_Remove,
            Resources.Common_Cancel,
            removeAction,
            RemoveOptionLabels(),
            Resources.Downloads_Remove_RememberChoice));

        if (choice is null)
        {
            return;
        }

        if (choice.RememberChoice)
        {
            _settingsService.Current.ShowRemoveTorrentOptions = false;
            _settingsService.Current.DefaultRemoveTorrentAction = ToSettingsValue(choice.Action);
            await _settingsService.SaveAsync(default);
        }

        await _torrentService.RemoveAsync(torrent, ToRemoveOptions(choice.Action));
    }

    private void OpenFolder()
    {
        OpenFolderFor(SelectedTorrent);
    }

    private void OpenFolderFor(TorrentListItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        var path = item.Torrent.Files.DownloadPath;
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }
    }

    /// <summary>
    /// Starts a stopped torrent and stops a running one. Simple mode puts one button on each row
    /// rather than asking the user to select a row and then find the toolbar.
    /// </summary>
    private Task ToggleTorrentAsync(TorrentListItemViewModel? item)
    {
        if (item == null)
        {
            return Task.CompletedTask;
        }

        return item.Torrent.Started
            ? TorrentService.StopAsync(item.Torrent)
            : TorrentService.StartAsync(item.Torrent);
    }

    private Task CopyHashAsync()
    {
        var selected = SelectedTorrent;
        return selected == null
            ? Task.CompletedTask
            : CopyToClipboardAsync(selected.Torrent.Hash.ToString());
    }

    private Task CopyMagnetAsync()
    {
        var selected = SelectedTorrent;
        if (selected == null)
        {
            return Task.CompletedTask;
        }

        // ITorrent interface might not have MagnetLink property, but we can generate it from hash
        return CopyToClipboardAsync($"magnet:?xt=urn:btih:{selected.Torrent.Hash}");
    }

    /// <summary>
    /// Puts text on the clipboard, and says so when it does not get there.
    ///
    /// <para>
    /// Flushed, because setting alone does not finish the job. Avalonia hands Windows a data object
    /// that renders its contents only when something asks for them, so the text belonged to this
    /// process rather than to the clipboard: pasting elsewhere produced whatever was copied before,
    /// and closing Peerfluence would have taken it away entirely. <c>FlushAsync</c> is what makes
    /// the copy real, and it does nothing on the platforms that do not need it.
    /// </para>
    ///
    /// <para>
    /// Awaited rather than started and abandoned, too. Windows hands the clipboard to one process
    /// at a time, so losing that race is ordinary rather than exceptional, and an unobserved failure
    /// left the user believing they had copied something.
    /// </para>
    /// </summary>
    private async Task CopyToClipboardAsync(string text)
    {
        IClipboard clipboard;
        try
        {
            clipboard = _topLevelService.GetClipboard();
        }
        catch (InvalidOperationException)
        {
            SetStatusMessage(Resources.Downloads_ClipboardUnavailable, autoClear: true);
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            await clipboard.FlushAsync();
        }
        catch (Exception ex)
        {
            SetStatusMessage(string.Format(Resources.Downloads_CopyFailed, ex.Message), autoClear: true);
        }
    }

    private bool CanForceRecheckSelected()
    {
        return SelectedTorrent is { Torrent.State: TorrentState.Stopped };
    }

    private async Task ForceRecheckSelectedAsync()
    {
        var selected = SelectedTorrent;
        if (selected == null)
        {
            return;
        }

        await TorrentService.ForceRecheckAsync(selected.Torrent);
    }

    // IFeatureViewModel
    public string Title => Resources.Nav_Downloads;

    public string IconKind => "Download";

    public int Order => 0;

    private void OnTorrentAlert(TorrentAlertMessage msg)
    {
        var e = new TorrentAlertEventArgs(msg.Torrent, msg.Alert);

        // Lifecycle alerts must not go through the debouncing channel —
        // a later alert for the same hash can overwrite LastImportant
        // within the 200 ms batch window, silently dropping the event.
        if (e.Alert.Id is AlertId.TorrentAdded or AlertId.TorrentRemoved)
        {
            Dispatcher.UIThread.Post(() => HandleTorrentAlert(e));
            return;
        }

        _alertChannel.Writer.TryWrite(e);
    }

    private async Task RunAlertLoopAsync(CancellationToken ct)
    {
        var pendingAlerts = new Dictionary<InfoHash, PendingAlerts>();
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                while (_alertChannel.Reader.TryRead(out var alert))
                {
                    if (!pendingAlerts.TryGetValue(alert.Torrent.Hash, out var pending))
                    {
                        pending = new PendingAlerts();
                        pendingAlerts[alert.Torrent.Hash] = pending;
                    }

                    switch (alert.Alert.Id)
                    {
                        case AlertId.TransferStatsUpdated:
                            pending.LastStats = alert;
                            break;
                        case AlertId.ProgressChanged:
                            pending.LastProgress = alert;
                            break;
                        default:
                            pending.LastImportant = alert;
                            break;
                    }
                }

                if (pendingAlerts.Count == 0)
                {
                    continue;
                }

                var snapshot = pendingAlerts.Values.ToArray();
                pendingAlerts.Clear();
                Dispatcher.UIThread.Post(() => ApplyAlertBatch(snapshot));
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Dispose();
        }
    }

    private void ApplyAlertBatch(PendingAlerts[] batch)
    {
        foreach (var pending in batch)
        {
            if (pending.LastImportant != null)
            {
                HandleTorrentAlert(pending.LastImportant);
            }

            if (pending.LastProgress != null)
            {
                HandleProgressAlert(pending.LastProgress);
            }

            if (pending.LastStats != null)
            {
                HandleStatsAlert(pending.LastStats);
            }
        }

        StartSelectedCommand.NotifyCanExecuteChanged();
        StopSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        CopyHashCommand.NotifyCanExecuteChanged();
        CopyMagnetCommand.NotifyCanExecuteChanged();
        ForceRecheckCommand.NotifyCanExecuteChanged();
    }

    private void HandleTorrentAlert(TorrentAlertEventArgs e)
    {
        var torrent = e.Torrent;

        switch (e.Alert.Id)
        {
            case AlertId.TorrentAdded:
                AddOrUpdateTorrent(torrent);
                break;
            case AlertId.TorrentRemoved:
                RemoveTorrent(torrent.Hash);
                break;
            default:
                UpdateTorrent(torrent, e.Alert);
                break;
        }
    }

    private void AddOrUpdateTorrent(ITorrent torrent)
    {
        var key = GetTorrentKey(torrent);
        if (_torrentLookup.TryGetValue(key, out var existing))
        {
            existing.UpdateFrom(torrent);
            return;
        }

        var viewModel = new TorrentListItemViewModel(torrent)
        {
            Actions = this,
            Category = _categoryService.GetCategory(torrent.Hash) ?? string.Empty
        };
        _torrentLookup[key] = viewModel;
        Torrents.Add(viewModel);
    }

    private void UpdateTorrent(ITorrent torrent, Alert alert)
    {
        var key = GetTorrentKey(torrent);
        if (!_torrentLookup.TryGetValue(key, out var existing))
        {
            return;
        }

        existing.UpdateFrom(torrent);
        HandleStatsAlert(existing, alert);
    }

    private void HandleProgressAlert(TorrentAlertEventArgs e)
    {
        var key = GetTorrentKey(e.Torrent);
        if (!_torrentLookup.TryGetValue(key, out var existing))
        {
            return;
        }

        existing.UpdateProgress(e.Torrent);
    }

    private void HandleStatsAlert(TorrentAlertEventArgs e)
    {
        var key = GetTorrentKey(e.Torrent);
        if (!_torrentLookup.TryGetValue(key, out var existing))
        {
            return;
        }

        HandleStatsAlert(existing, e.Alert);
    }

    private static void HandleStatsAlert(TorrentListItemViewModel existing, Alert alert)
    {
        if (alert is TransferStatsAlert statsAlert)
        {
            existing.UpdateTransferStats(new TransferStats
            {
                ConnectedPeers = statsAlert.ConnectedPeers,
                DownloadSpeed = statsAlert.DownloadSpeed,
                UploadSpeed = statsAlert.UploadSpeed,
                Downloaded = statsAlert.Downloaded,
                Uploaded = statsAlert.Uploaded
            });
        }
    }

    private sealed class PendingAlerts
    {
        public TorrentAlertEventArgs? LastImportant { get; set; }
        public TorrentAlertEventArgs? LastProgress { get; set; }
        public TorrentAlertEventArgs? LastStats { get; set; }
    }

    private void RemoveTorrent(InfoHash hash)
    {
        // InfoHash can be V1 or V2. We need to find the entry that matches either.
        var entry = _torrentLookup.FirstOrDefault(x => TorrentIdentity.HasHash(x.Value.Torrent, hash));
        if (entry.Value == null)
        {
            return;
        }

        var key = entry.Key;
        var existing = entry.Value;

        existing.Detach();
        Torrents.Remove(existing);
        _torrentLookup.Remove(key);

        var selected = _selectionService.SelectedTorrent;
        if (SelectedTorrent == existing || (selected != null && TorrentIdentity.HasHash(selected, hash)))
        {
            SelectedTorrent = null;
            _selectionService.SelectedTorrent = null;
        }
    }

    private static string GetTorrentKey(ITorrent torrent)
    {
        return $"{torrent.Hash.ToHexStringUpper()}_{torrent.HashV2.ToHexStringUpper()}";
    }

    private RemoveTorrentAction GetDefaultRemoveAction()
    {
        return _settingsService.Current.DefaultRemoveTorrentAction switch
        {
            "DeleteFiles" => RemoveTorrentAction.DeleteFiles,
            "DeleteMetadata" => RemoveTorrentAction.DeleteMetadata,
            "DeleteAll" => RemoveTorrentAction.DeleteAll,
            _ => RemoveTorrentAction.RemoveOnly
        };
    }

    /// <summary>
    /// What each removal option is called, in the language the interface is showing.
    /// </summary>
    /// <remarks>
    /// Passed to the dialog rather than read by it. The dialog service builds controls and knows
    /// nothing about torrents; naming the options is this view model's business.
    /// </remarks>
    private static Dictionary<RemoveTorrentAction, string> RemoveOptionLabels()
    {
        return new Dictionary<RemoveTorrentAction, string>
        {
            [RemoveTorrentAction.RemoveOnly] = Resources.Downloads_Remove_Option_RemoveOnly,
            [RemoveTorrentAction.DeleteFiles] = Resources.Downloads_Remove_Option_DeleteFiles,
            [RemoveTorrentAction.DeleteMetadata] = Resources.Downloads_Remove_Option_DeleteMetadata,
            [RemoveTorrentAction.DeleteAll] = Resources.Downloads_Remove_Option_DeleteAll
        };
    }

    internal static RemoveOptions ToRemoveOptions(RemoveTorrentAction action)
    {
        return action switch
        {
            RemoveTorrentAction.DeleteFiles => RemoveOptions.DeleteFiles,
            RemoveTorrentAction.DeleteMetadata => RemoveOptions.DeleteTorrentFile,
            RemoveTorrentAction.DeleteAll => RemoveOptions.DeleteAll,
            _ => RemoveOptions.None
        };
    }

    private static string ToSettingsValue(RemoveTorrentAction action)
    {
        return action switch
        {
            RemoveTorrentAction.DeleteFiles => "DeleteFiles",
            RemoveTorrentAction.DeleteMetadata => "DeleteMetadata",
            RemoveTorrentAction.DeleteAll => "DeleteAll",
            _ => "RemoveOnly"
        };
    }

    private async Task<string?> TryGetMagnetFromClipboardAsync()
    {
        try
        {
            var clipboard = _topLevelService.GetClipboard();
            var text = await clipboard.TryGetTextAsync();
            return text?.Trim();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void OnTorrentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateTorrentPresence();
        ApplyFilter();
    }

    private void UpdateTorrentPresence()
    {
        OnPropertyChanged(nameof(HasTorrents));
        OnPropertyChanged(nameof(HasNoTorrents));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Torrents.CollectionChanged -= OnTorrentsCollectionChanged;
        CancelStatusAutoClear();
        _alertChannel.Writer.TryComplete();
        _loopCts.Cancel();
        _loopCts.Dispose();
    }

}
