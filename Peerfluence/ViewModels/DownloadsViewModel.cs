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
public sealed class DownloadsViewModel : ViewModelBase, IFeatureViewModel, IDisposable
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
        DetailsViewModel detailsViewModel)
    {
        _torrentService = torrentService;
        _selectionService = selectionService;
        _localizationService = localizationService;
        _topLevelService = topLevelService;
        _dialogService = dialogService;
        _addTorrentDialogService = addTorrentDialogService;
        _settingsService = settingsService;
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
        CopyHashCommand = new RelayCommand(CopyHash, () => SelectedTorrent != null);
        CopyMagnetCommand = new RelayCommand(CopyMagnet, () => SelectedTorrent != null);
        ForceRecheckCommand = new AsyncRelayCommand(ForceRecheckSelectedAsync, CanForceRecheckSelected);

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

    public bool HasTorrents => Torrents.Count > 0;

    public bool HasNoTorrents => !HasTorrents;

    public bool IsBusy
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int TotalDownloadSpeedBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int TotalUploadSpeedBytesPerSecond
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

    public IRelayCommand CopyHashCommand { get; }

    public IRelayCommand CopyMagnetCommand { get; }

    public IAsyncRelayCommand ForceRecheckCommand { get; }

    public ISukiDialogManager? SukiDialogManager { get; set; }

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

        var file = files.FirstOrDefault();
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

    private bool CanStartSelected()
    {
        return SelectedTorrent is { Torrent.State: TorrentState.Stopped };
    }

    private bool CanStopSelected()
    {
        return SelectedTorrent is { Torrent.Started: true };
    }

    private bool CanRemoveSelected()
    {
        return SelectedTorrent is not null;
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
    }


    private const int SpeedSmoothingWindow = 3;
    private double _smoothedTotalDownloadSpeedBytesPerSecond;
    private double _smoothedTotalUploadSpeedBytesPerSecond;
    private int _totalDownloadSpeedSamples;
    private int _totalUploadSpeedSamples;

    private static int UpdateSmoothedSpeed(int current, ref double smoothed, ref int samples)
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
            return (int)Math.Round(smoothed);
        }

        double alpha = 2.0 / (SpeedSmoothingWindow + 1);
        smoothed = (alpha * current) + ((1.0 - alpha) * smoothed);
        samples++;
        return (int)Math.Round(smoothed);
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
        if (SukiDialogManager == null)
        {
            return null;
        }

        var textBox = new Avalonia.Controls.TextBox
        {
            Width = 420,
            MinWidth = 320,
            Text = MagnetLink,
            PlaceholderText = Resources.Downloads_MagnetWatermark
        };

        var result = new TaskCompletionSource<bool>();
        await SukiDialogManager
            .CreateDialog()
            .WithTitle(Resources.Downloads_AddMagnet)
            .WithContent(textBox)
            .Dismiss().ByClickingBackground()
            .OnDismissed(_ => result.TrySetResult(false))
            .WithActionButton(
                Resources.Common_Cancel,
                _ => result.TrySetResult(false),
                true)
            .WithActionButton(
                Resources.Downloads_AddMagnet,
                _ => result.TrySetResult(true),
                true,
                "Flat")
            .TryShowAsync();

        if (!result.Task.IsCompletedSuccessfully || !result.Task.Result)
        {
            return null;
        }

        MagnetLink = textBox.Text?.Trim() ?? string.Empty;
        return MagnetLink;
    }

    private static bool IsValidMagnetLink(string? magnet)
    {
        return TryNormalizeMagnetLink(magnet, out _, out _);
    }

    private static bool TryNormalizeMagnetLink(string? magnet, out string normalized, out string? error)
    {
        normalized = magnet?.Trim() ?? string.Empty;
        return PeerSharp.Core.MagnetLink.TryParse(normalized, out _, out error);
    }

    private Task ShowCreateTorrentAsync()
    {
        return _dialogService.ShowAsync<CreateTorrentViewModel>();
    }

    private async Task StartSelectedAsync()
    {
        var selected = SelectedTorrent;
        if (selected == null)
        {
            return;
        }

        await TorrentService.StartAsync(selected.Torrent);
    }

    private async Task StopSelectedAsync()
    {
        var selected = SelectedTorrent;
        if (selected == null)
        {
            return;
        }

        await TorrentService.StopAsync(selected.Torrent);
    }

    private async Task RemoveSelectedAsync()
    {
        if (SelectedTorrent == null)
        {
            return;
        }

        var torrent = SelectedTorrent.Torrent;
        var torrentName = SelectedTorrent.Name;
        var removeAction = GetDefaultRemoveAction();

        if (!_settingsService.Current.ShowRemoveTorrentOptions)
        {
            await _torrentService.RemoveAsync(torrent, ToRemoveOptions(removeAction));
            return;
        }

        if (SukiDialogManager != null)
        {
            var removeOnly = new Avalonia.Controls.RadioButton
            {
                Content = Resources.Downloads_Remove_Option_RemoveOnly,
                GroupName = "RemoveTorrentAction",
                IsChecked = removeAction == RemoveTorrentAction.RemoveOnly
            };
            var deleteFiles = new Avalonia.Controls.RadioButton
            {
                Content = Resources.Downloads_Remove_Option_DeleteFiles,
                GroupName = "RemoveTorrentAction",
                IsChecked = removeAction == RemoveTorrentAction.DeleteFiles
            };
            var deleteMetadata = new Avalonia.Controls.RadioButton
            {
                Content = Resources.Downloads_Remove_Option_DeleteMetadata,
                GroupName = "RemoveTorrentAction",
                IsChecked = removeAction == RemoveTorrentAction.DeleteMetadata
            };
            var deleteAll = new Avalonia.Controls.RadioButton
            {
                Content = Resources.Downloads_Remove_Option_DeleteAll,
                GroupName = "RemoveTorrentAction",
                IsChecked = removeAction == RemoveTorrentAction.DeleteAll
            };
            var rememberChoice = new Avalonia.Controls.CheckBox
            {
                Content = Resources.Downloads_Remove_RememberChoice,
                Margin = new Avalonia.Thickness(0, 10, 0, 0)
            };

            var content = new Avalonia.Controls.StackPanel
            {
                Children =
                {
                    new Avalonia.Controls.TextBlock
                    {
                        Text = string.Format(Resources.Downloads_Remove_Confirm_Message, torrentName),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new Avalonia.Controls.StackPanel
                    {
                        Margin = new Avalonia.Thickness(0, 10, 0, 0),
                        Children =
                        {
                            removeOnly,
                            deleteFiles,
                            deleteMetadata,
                            deleteAll
                        }
                    },
                    rememberChoice
                }
            };

            var result = new TaskCompletionSource<bool>();
            await SukiDialogManager
                .CreateDialog()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Warning)
                .WithTitle(Resources.Downloads_Remove_Confirm_Title)
                .WithContent(content)
                .Dismiss().ByClickingBackground()
                .OnDismissed(_ => result.TrySetResult(false))
                .WithActionButton(Resources.Common_Cancel, _ => result.TrySetResult(false), true)
                .WithActionButton(Resources.Downloads_Remove, _ => result.TrySetResult(true), true, "Flat")
                .TryShowAsync();

            if (!result.Task.IsCompletedSuccessfully || !result.Task.Result)
            {
                return;
            }

            removeAction = GetSelectedRemoveAction(removeOnly, deleteFiles, deleteMetadata, deleteAll);
            if (rememberChoice.IsChecked == true)
            {
                _settingsService.Current.ShowRemoveTorrentOptions = false;
                _settingsService.Current.DefaultRemoveTorrentAction = ToSettingsValue(removeAction);
                await _settingsService.SaveAsync(default);
            }

            await _torrentService.RemoveAsync(torrent, ToRemoveOptions(removeAction));
            return;
        }

        await _torrentService.RemoveAsync(torrent, ToRemoveOptions(removeAction));
    }

    private void OpenFolder()
    {
        var selected = SelectedTorrent;
        if (selected == null)
        {
            return;
        }

        var path = selected.Torrent.Files.DownloadPath;
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

    private void CopyHash()
    {
        var selected = SelectedTorrent;
        if (selected == null)
        {
            return;
        }

        var topLevel = _topLevelService.GetTopLevel();
        var clipboard = topLevel?.Clipboard;
        clipboard?.SetTextAsync(selected.Torrent.Hash.ToString());
    }

    private void CopyMagnet()
    {
        var selected = SelectedTorrent;
        if (selected == null)
        {
            return;
        }

        // ITorrent interface might not have MagnetLink property, but we can generate it from hash
        var magnet = $"magnet:?xt=urn:btih:{selected.Torrent.Hash}";
        var topLevel = _topLevelService.GetTopLevel();
        var clipboard = topLevel?.Clipboard;
        clipboard?.SetTextAsync(magnet);
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

        var viewModel = new TorrentListItemViewModel(torrent);
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
        var entry = _torrentLookup.FirstOrDefault(x => x.Value.Torrent.Hash == hash || x.Value.Torrent.HashV2 == hash);
        if (entry.Value == null)
        {
            return;
        }

        var key = entry.Key;
        var existing = entry.Value;

        existing.Detach();
        Torrents.Remove(existing);
        _torrentLookup.Remove(key);

        if (SelectedTorrent == existing || _selectionService.SelectedTorrent?.Hash == hash || _selectionService.SelectedTorrent?.HashV2 == hash)
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

    private static RemoveTorrentAction GetSelectedRemoveAction(
        Avalonia.Controls.RadioButton removeOnly,
        Avalonia.Controls.RadioButton deleteFiles,
        Avalonia.Controls.RadioButton deleteMetadata,
        Avalonia.Controls.RadioButton deleteAll)
    {
        if (deleteAll.IsChecked == true)
        {
            return RemoveTorrentAction.DeleteAll;
        }

        if (deleteMetadata.IsChecked == true)
        {
            return RemoveTorrentAction.DeleteMetadata;
        }

        return deleteFiles.IsChecked == true ? RemoveTorrentAction.DeleteFiles : RemoveTorrentAction.RemoveOnly;
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

    internal enum RemoveTorrentAction
    {
        RemoveOnly,
        DeleteFiles,
        DeleteMetadata,
        DeleteAll
    }
}
