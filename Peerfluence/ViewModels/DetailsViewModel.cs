using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Messaging;
using PeerSharp.Interfaces;
using PeerSharp.Streaming;

namespace Peerfluence.ViewModels;

[SingletonService]
public sealed class DetailsViewModel : ViewModelBase
{
    private readonly ITorrentSelectionService _selectionService;
    private readonly ITorrentService _torrentService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly ITopLevelService _topLevelService;
    private readonly IAppSettingsService _settingsService;

    private CancellationTokenSource? _streamingCts;
    private HttpStreamServer? _streamServer;

    private readonly Channel<ITorrent> _refreshChannel;
    private readonly CancellationTokenSource _loopCts = new();
    private readonly Task _refreshTask;

    // Last known values from server to avoid overwriting user edits
    private int _lastServerDownloadLimit;
    private int _lastServerUploadLimit;
    private int _lastServerDiskReadLimit;
    private int _lastServerDiskWriteLimit;
    private DownloadStrategy _lastServerDownloadStrategy;
    private string _lastServerRatioLimit = string.Empty;
    private string _lastServerSeedTimeLimitMinutes = string.Empty;
    private int _lastServerQueuePriority;

    internal Action<Action> UIDispatcher { get; set; } = action => Dispatcher.UIThread.Post(action);

    public DetailsViewModel(
        ITorrentSelectionService selectionService,
        ITorrentService torrentService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        ITopLevelService topLevelService,
        IAppSettingsService settingsService)
    {
        _selectionService = selectionService;
        _torrentService = torrentService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _topLevelService = topLevelService;
        _settingsService = settingsService;

        Files = new ObservableCollection<TorrentFileItemViewModel>();
        Trackers = new ObservableCollection<TrackerStatusItemViewModel>();
        Peers = new ObservableCollection<PeerInfoItemViewModel>();

        ApplyTorrentSettingsCommand = new AsyncRelayCommand(ApplyTorrentSettingsAsync, () => _selectionService.SelectedTorrent != null);
        ApplyFileSelectionCommand = new AsyncRelayCommand(ApplyFileSelectionAsync, () => _selectionService.SelectedTorrent != null);
        ForceRecheckCommand = new AsyncRelayCommand(ForceRecheckAsync, CanForceRecheck);
        SaveResumeDataCommand = new AsyncRelayCommand(SaveResumeDataAsync, () => _selectionService.SelectedTorrent != null);
        ChangeDownloadPathCommand = new AsyncRelayCommand(ChangeDownloadPathAsync, CanChangeDownloadPath);
        StreamFileCommand = new AsyncRelayCommand<TorrentFileItemViewModel?>(StreamFileAsync, CanStreamFile);

        // Tracker management
        AddTrackerCommand = new RelayCommand(AddTracker, () => _selectionService.SelectedTorrent != null && !string.IsNullOrWhiteSpace(NewTrackerUrl));
        RemoveTrackerCommand = new RelayCommand<TrackerStatusItemViewModel?>(RemoveTracker, _ => _selectionService.SelectedTorrent != null);
        AnnounceCommand = new AsyncRelayCommand(AnnounceAsync, () => _selectionService.SelectedTorrent != null);

        WeakReferenceMessenger.Default.Register<TorrentSelectionChangedMessage>(this, (_, msg) => OnSelectionChanged(msg));
        WeakReferenceMessenger.Default.Register<TorrentAlertMessage>(this, (_, msg) => OnTorrentAlert(msg));
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (_, _) => OnLanguageChanged());
        UpdateEmptyStateText();

        _refreshChannel = Channel.CreateBounded<ITorrent>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _refreshTask = Task.Run(() => RunRefreshLoopAsync(_loopCts.Token));
    }

    public ObservableCollection<TorrentFileItemViewModel> Files { get; }

    public ObservableCollection<TrackerStatusItemViewModel> Trackers { get; }

    public ObservableCollection<PeerInfoItemViewModel> Peers { get; }

    public IReadOnlyList<EnumDisplayOption<DownloadStrategy>> DownloadStrategies => PriorityOptions.DownloadStrategies;

    public IReadOnlyList<EnumDisplayOption<Priority>> PriorityChoices => PriorityOptions.Localized;

    public string Name
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string InfoHash
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string State
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public float Progress
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string DownloadPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public long TotalSizeBytes
    {
        get;
        set => SetProperty(ref field, value);
    }

    public long DownloadedBytes
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int ConnectedPeers
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int DiskReadLimitBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int DiskWriteLimitBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int DownloadLimitBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int UploadLimitBytesPerSecond
    {
        get;
        set => SetProperty(ref field, value);
    }

    public DownloadStrategy SelectedDownloadStrategy
    {
        get;
        set => SetProperty(ref field, value);
    }

    // Auto-stop rules
    public string RatioLimit
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string SeedTimeLimitMinutes
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    // Queue priority
    public int QueuePriority
    {
        get;
        set => SetProperty(ref field, value);
    }

    // Force recheck
    public bool IsRechecking
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ForceRecheckCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public float RecheckProgress
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string RecheckStatus
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    // Streaming
    public bool HasStreamableFiles
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsStreaming
    {
        get;
        set => SetProperty(ref field, value);
    }

    // Tracker management
    public string NewTrackerUrl
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AddTrackerCommand.NotifyCanExecuteChanged();
            }
        }
    } = string.Empty;

    // Piece map
    public byte[]? PieceBitfield
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int[]? PieceAvailability
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int PieceCount
    {
        get;
        set => SetProperty(ref field, value);
    }

    public IAsyncRelayCommand ApplyTorrentSettingsCommand { get; }

    public IAsyncRelayCommand ApplyFileSelectionCommand { get; }

    public IAsyncRelayCommand ForceRecheckCommand { get; }

    public IAsyncRelayCommand<TorrentFileItemViewModel?> StreamFileCommand { get; }

    public IAsyncRelayCommand SaveResumeDataCommand { get; }

    public IAsyncRelayCommand ChangeDownloadPathCommand { get; }

    public IRelayCommand AddTrackerCommand { get; }

    public IRelayCommand<TrackerStatusItemViewModel?> RemoveTrackerCommand { get; }

    public IAsyncRelayCommand AnnounceCommand { get; }

    private void OnSelectionChanged(TorrentSelectionChangedMessage msg)
    {
        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
        _streamingCts = null;
        _streamServer?.Dispose();
        _streamServer = null;

        TriggerRefresh();
        ApplyTorrentSettingsCommand.NotifyCanExecuteChanged();
        ApplyFileSelectionCommand.NotifyCanExecuteChanged();
        ForceRecheckCommand.NotifyCanExecuteChanged();
        SaveResumeDataCommand.NotifyCanExecuteChanged();
        ChangeDownloadPathCommand.NotifyCanExecuteChanged();
        AddTrackerCommand.NotifyCanExecuteChanged();
        AnnounceCommand.NotifyCanExecuteChanged();
    }

    private void OnTorrentAlert(TorrentAlertMessage msg)
    {
        if (MatchesSelectedTorrent(msg.Torrent))
        {
            _refreshChannel.Writer.TryWrite(msg.Torrent);
        }
    }

    internal void RefreshFromSelection() => TriggerRefresh();

    private void TriggerRefresh()
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent != null)
        {
            _refreshChannel.Writer.TryWrite(torrent);
        }
        else
        {
            UIDispatcher(Clear);
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _refreshChannel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_refreshChannel.Reader.TryRead(out var torrent))
                {
                    // Debounce: wait a bit before refreshing
                    await Task.Delay(200, ct).ConfigureAwait(false);

                    // Consume any newer requests that arrived during delay
                    while (_refreshChannel.Reader.TryRead(out var newer))
                    {
                        torrent = newer;
                    }

                    await RefreshFromTorrentAsync(torrent, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshFromTorrentAsync(ITorrent torrent, CancellationToken ct)
    {
        // 1. Capture basic info (fast)
        var name = torrent.Name;
        var infoHash = torrent.Hash.ToString();
        var state = torrent.State.ToDisplayString();
        var progress = torrent.Progress;
        var downloadPath = torrent.Files.DownloadPath;
        var totalSizeBytes = torrent.TotalSize;
        var downloadedBytes = (long)torrent.FinishedBytes;
        var connectedPeersCount = torrent.Peers.ConnectedCount;

        var diskReadLimit = torrent.DiskReadLimitBytesPerSecond;
        var diskWriteLimit = torrent.DiskWriteLimitBytesPerSecond;
        var downloadLimit = torrent.DownloadLimitBytesPerSecond;
        var uploadLimit = torrent.UploadLimitBytesPerSecond;
        var downloadStrategy = torrent.DownloadStrategy;
        var ratioLimit = torrent.RatioLimit?.ToString("F1") ?? string.Empty;
        var seedTimeLimitMinutes = torrent.SeedTimeLimit.HasValue
            ? ((int)torrent.SeedTimeLimit.Value.TotalMinutes).ToString()
            : string.Empty;
        var queuePriority = torrent.QueuePriority;

        var hasStreamableFiles = torrent.HasStreamableFiles;
        var pieceCount = torrent.PieceCount;

        // 2. Perform expensive calls in background
        var pieceBitfield = await Task.Run(() => torrent.GetPieceBitfield(), ct).ConfigureAwait(false);
        var pieceAvailability = await Task.Run(() => torrent.Peers.GetPieceAvailability(), ct).ConfigureAwait(false);

        var fileInfos = await Task.Run(() => torrent.GetAllFileInfo(), ct).ConfigureAwait(false);
        var fileSelections = await Task.Run(() => torrent.GetAllFileSelections(), ct).ConfigureAwait(false);
        var streamableIndices = torrent.StreamableFileIndices;

        var trackers = await Task.Run(() => torrent.Trackers.GetTrackers().ToList(), ct).ConfigureAwait(false);

        List<PeerInfo>? connectedPeers = null;
        if (torrent.State is not (TorrentState.Stopped or TorrentState.Stopping))
        {
            connectedPeers = await Task.Run(() => torrent.Peers.GetConnectedPeers().ToList(), ct).ConfigureAwait(false);
        }

        if (ct.IsCancellationRequested) return;

        // 3. Update UI
        UIDispatcher(() =>
        {
            if (ct.IsCancellationRequested) return;
            if (!MatchesSelectedTorrent(torrent)) return;

            Name = name;
            InfoHash = infoHash;
            State = state;
            Progress = progress;
            DownloadPath = downloadPath;
            TotalSizeBytes = totalSizeBytes;
            DownloadedBytes = downloadedBytes;
            ConnectedPeers = connectedPeersCount;

            // Sync settings: only update if current UI matches last known server value (user hasn't edited)
            if (DownloadLimitBytesPerSecond == _lastServerDownloadLimit) DownloadLimitBytesPerSecond = downloadLimit;
            _lastServerDownloadLimit = downloadLimit;

            if (UploadLimitBytesPerSecond == _lastServerUploadLimit) UploadLimitBytesPerSecond = uploadLimit;
            _lastServerUploadLimit = uploadLimit;

            if (DiskReadLimitBytesPerSecond == _lastServerDiskReadLimit) DiskReadLimitBytesPerSecond = diskReadLimit;
            _lastServerDiskReadLimit = diskReadLimit;

            if (DiskWriteLimitBytesPerSecond == _lastServerDiskWriteLimit) DiskWriteLimitBytesPerSecond = diskWriteLimit;
            _lastServerDiskWriteLimit = diskWriteLimit;

            if (SelectedDownloadStrategy == _lastServerDownloadStrategy) SelectedDownloadStrategy = downloadStrategy;
            _lastServerDownloadStrategy = downloadStrategy;

            if (RatioLimit == _lastServerRatioLimit) RatioLimit = ratioLimit;
            _lastServerRatioLimit = ratioLimit;

            if (SeedTimeLimitMinutes == _lastServerSeedTimeLimitMinutes) SeedTimeLimitMinutes = seedTimeLimitMinutes;
            _lastServerSeedTimeLimitMinutes = seedTimeLimitMinutes;

            if (QueuePriority == _lastServerQueuePriority) QueuePriority = queuePriority;
            _lastServerQueuePriority = queuePriority;

            HasStreamableFiles = hasStreamableFiles;
            PieceCount = pieceCount;
            PieceBitfield = pieceBitfield;
            PieceAvailability = pieceAvailability;

            UpdateFiles(fileInfos, fileSelections, streamableIndices);
            UpdateTrackers(trackers);

            if (torrent.State is TorrentState.Stopped or TorrentState.Stopping)
            {
                ConnectedPeers = 0;
                Peers.Clear();
            }
            else if (connectedPeers != null)
            {
                UpdatePeers(connectedPeers);
            }
        });
    }

    private void UpdateFiles(IReadOnlyList<TorrentFileInfo> fileInfos, IReadOnlyList<FileSelection> selections, IReadOnlyList<int> streamableIndices)
    {
        // If count mismatch, just clear and rebuild
        if (Files.Count != fileInfos.Count)
        {
            Files.Clear();
            foreach (var file in fileInfos)
            {
                var selection = file.Index < selections.Count
                    ? selections[file.Index]
                    : new FileSelection();
                var isStreamable = streamableIndices.Contains(file.Index);
                Files.Add(new TorrentFileItemViewModel(file, selection, isStreamable));
            }
            return;
        }

        // Update existing VMs
        for (int i = 0; i < fileInfos.Count; i++)
        {
            var file = fileInfos[i];
            var selection = file.Index < selections.Count
                ? selections[file.Index]
                : new FileSelection();
            var isStreamable = streamableIndices.Contains(file.Index);
            Files[i].UpdateFrom(file, selection, isStreamable);
        }
    }

    private void UpdateTrackers(IEnumerable<TrackerStatus> trackers)
    {
        var trackerList = trackers.ToList();

        // Match by URL
        var currentTrackers = Trackers.ToDictionary(t => t.Url);
        var toRemove = Trackers.Where(t => !trackerList.Any(nt => nt.Url == t.Url)).ToList();

        foreach (var tracker in toRemove)
        {
            Trackers.Remove(tracker);
        }

        foreach (var status in trackerList)
        {
            if (currentTrackers.TryGetValue(status.Url, out var existing))
            {
                existing.UpdateFrom(status);
            }
            else
            {
                Trackers.Add(new TrackerStatusItemViewModel(status));
            }
        }
    }

    private void UpdatePeers(IEnumerable<PeerInfo> peers)
    {
        var peerList = peers.ToList();
        var currentPeers = Peers.ToDictionary(p => p.EndPoint);
        var toRemove = Peers.Where(p => !peerList.Any(np => np.EndPoint.ToString() == p.EndPoint)).ToList();

        foreach (var peer in toRemove)
        {
            Peers.Remove(peer);
        }

        foreach (var peer in peerList)
        {
            var endPointStr = peer.EndPoint.ToString();
            if (currentPeers.TryGetValue(endPointStr, out var existing))
            {
                existing.UpdateFrom(peer);
            }
            else
            {
                Peers.Add(new PeerInfoItemViewModel(peer));
            }
        }
    }

    private void Clear()
    {
        Name = Properties.Resources.Details_SelectTorrent;
        InfoHash = string.Empty;
        State = string.Empty;
        Progress = 0f;
        DownloadPath = string.Empty;
        TotalSizeBytes = 0;
        DownloadedBytes = 0;
        ConnectedPeers = 0;
        DiskReadLimitBytesPerSecond = 0;
        DiskWriteLimitBytesPerSecond = 0;
        DownloadLimitBytesPerSecond = 0;
        UploadLimitBytesPerSecond = 0;
        SelectedDownloadStrategy = DownloadStrategy.RarestFirst;
        RatioLimit = string.Empty;
        SeedTimeLimitMinutes = string.Empty;
        QueuePriority = 0;
        HasStreamableFiles = false;
        IsRechecking = false;
        RecheckProgress = 0;
        RecheckStatus = string.Empty;
        NewTrackerUrl = string.Empty;
        PieceBitfield = null;
        PieceAvailability = null;
        PieceCount = 0;

        Files.Clear();
        Trackers.Clear();
        Peers.Clear();

        // Reset last server state
        _lastServerDownloadLimit = 0;
        _lastServerUploadLimit = 0;
        _lastServerDiskReadLimit = 0;
        _lastServerDiskWriteLimit = 0;
        _lastServerDownloadStrategy = DownloadStrategy.RarestFirst;
        _lastServerRatioLimit = string.Empty;
        _lastServerSeedTimeLimitMinutes = string.Empty;
        _lastServerQueuePriority = 0;
    }

    private void UpdateEmptyStateText()
    {
        if (_selectionService.SelectedTorrent == null)
        {
            Name = Properties.Resources.Details_SelectTorrent;
        }
    }

    private void OnLanguageChanged()
    {
        UpdateEmptyStateText();
        OnPropertyChanged(nameof(DownloadStrategies));
        OnPropertyChanged(nameof(PriorityChoices));
        foreach (var tracker in Trackers)
        {
            tracker.RefreshLocalizedText();
        }
    }

    private Task ApplyTorrentSettingsAsync()
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null)
        {
            return Task.CompletedTask;
        }

        torrent.DownloadLimitBytesPerSecond = DownloadLimitBytesPerSecond;
        torrent.UploadLimitBytesPerSecond = UploadLimitBytesPerSecond;
        torrent.DiskReadLimitBytesPerSecond = DiskReadLimitBytesPerSecond;
        torrent.DiskWriteLimitBytesPerSecond = DiskWriteLimitBytesPerSecond;
        torrent.DownloadStrategy = SelectedDownloadStrategy;

        // Auto-stop rules
        if (float.TryParse(RatioLimit, out var ratio) && ratio > 0)
        {
            torrent.RatioLimit = ratio;
        }
        else
        {
            torrent.RatioLimit = null;
        }

        if (int.TryParse(SeedTimeLimitMinutes, out var minutes) && minutes > 0)
        {
            torrent.SeedTimeLimit = TimeSpan.FromMinutes(minutes);
        }
        else
        {
            torrent.SeedTimeLimit = null;
        }

        // Queue priority
        torrent.QueuePriority = QueuePriority;

        return Task.CompletedTask;
    }

    private async Task ApplyFileSelectionAsync()
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null)
        {
            return;
        }

        var snapshot = Files.ToList();
        foreach (var file in snapshot)
        {
            var selection = new FileSelection(file.IsSelected, file.Priority);
            await torrent.SetFileSelectionAsync(file.Index, selection).ConfigureAwait(false);
        }
    }

    // Force recheck
    private bool CanForceRecheck()
    {
        var torrent = _selectionService.SelectedTorrent;
        return torrent != null && !IsRechecking && torrent.State == TorrentState.Stopped;
    }

    private async Task ForceRecheckAsync()
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null)
        {
            return;
        }

        IsRechecking = true;
        RecheckProgress = 0;
        RecheckStatus = Properties.Resources.Details_Recheck_Verifying;

        try
        {
            var progress = new Progress<PieceCheckProgress>(p =>
            {
                UIDispatcher(() =>
                {
                    RecheckProgress = p.Progress;
                    RecheckStatus = string.Format(Properties.Resources.Details_Recheck_Progress, p.CheckedPieces, p.TotalPieces, p.ValidPieces);
                });
            });

            var validPieces = await TorrentService.ForceRecheckAsync(torrent, progress).ConfigureAwait(false);

            UIDispatcher(() =>
            {
                RecheckStatus = string.Format(Properties.Resources.Details_Recheck_Complete, validPieces);
                IsRechecking = false;
                if (_selectionService.SelectedTorrent?.Hash == torrent.Hash)
                {
                    TriggerRefresh();
                }
            });
        }
        catch (Exception ex)
        {
            UIDispatcher(() =>
            {
                RecheckStatus = string.Format(Properties.Resources.Details_Recheck_Failed, ex.Message);
                IsRechecking = false;
            });
        }
    }

    // Streaming
    private bool CanStreamFile(TorrentFileItemViewModel? file)
    {
        return file is { IsStreamable: true } && _selectionService.SelectedTorrent != null;
    }

    private async Task StreamFileAsync(TorrentFileItemViewModel? file)
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null || file == null)
        {
            return;
        }

        try
        {
            torrent.DownloadStrategy = DownloadStrategy.Streaming;
            UIDispatcher(() => SelectedDownloadStrategy = DownloadStrategy.Streaming);

            _streamingCts?.Cancel();
            _streamingCts?.Dispose();
            _streamingCts = new CancellationTokenSource();

            _streamServer?.Dispose();
            var streamServer = new HttpStreamServer(torrent, file.Index);
            _streamServer = streamServer;
            streamServer.Start();

            var ct = _streamingCts.Token;
            IsStreaming = true;

            // Wait for a small amount of data to be available at the start of the file
            // to ensure the media player can identify the format immediately.
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var stream = await torrent.OpenStreamAsync(file.Index, ct).ConfigureAwait(false);
                    var buffer = new byte[256 * 1024];
                    await stream.ReadAsync(buffer, ct).ConfigureAwait(false);

                    UIDispatcher(() => IsStreaming = false);

                    var mediaPlayerPath = _settingsService.Current.MediaPlayerPath;
                    if (!string.IsNullOrWhiteSpace(mediaPlayerPath))
                    {
                        Process.Start(new ProcessStartInfo(mediaPlayerPath, $"\"{streamServer.Url}\"") { UseShellExecute = false });
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo(streamServer.Url) { UseShellExecute = true });
                    }
                }
                catch (OperationCanceledException)
                {
                    UIDispatcher(() => IsStreaming = false);
                }
                catch (Exception ex)
                {
                    UIDispatcher(() =>
                    {
                        IsStreaming = false;
                        _notificationService.Publish(
                            new NotificationItem(
                                Properties.Resources.Details_StreamFailed,
                                $"{file.Path}: {ex.Message}",
                                NotificationType.Error,
                                Material.Icons.MaterialIconKind.AlertCircleOutline.ToString()),
                            TimeSpan.FromSeconds(8));
                    });
                }
            }, ct);
        }
        catch (Exception ex)
        {
            _notificationService.Publish(
                new NotificationItem(
                    Properties.Resources.Details_StreamFailed,
                    $"{file.Path}: {ex.Message}",
                    NotificationType.Error,
                    Material.Icons.MaterialIconKind.AlertCircleOutline.ToString()),
                TimeSpan.FromSeconds(8));
        }
    }

    // Tracker management
    private void AddTracker()
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null || string.IsNullOrWhiteSpace(NewTrackerUrl))
        {
            return;
        }

        torrent.Trackers.AddTracker(NewTrackerUrl.Trim());
        NewTrackerUrl = string.Empty;
        TriggerRefresh();
    }

    private void RemoveTracker(TrackerStatusItemViewModel? tracker)
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null || tracker == null)
        {
            return;
        }

        torrent.Trackers.RemoveTracker(tracker.Url);
        TriggerRefresh();
    }

    private async Task AnnounceAsync()
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null)
        {
            return;
        }

        await torrent.Trackers.AnnounceAsync().ConfigureAwait(false);
        TriggerRefresh();
    }

    // Resume data
    private async Task SaveResumeDataAsync()
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null)
        {
            return;
        }

        try
        {
            var storage = _topLevelService.GetStorageProvider();
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Properties.Resources.Details_SaveResumeData_Title,
                DefaultExtension = "resume",
                SuggestedFileName = $"{torrent.Name}.resume",
                FileTypeChoices =
                [
                    new FilePickerFileType(Properties.Resources.Details_SaveResumeData_FileType)
                    {
                        Patterns = ["*.resume"]
                    }
                ]
            });

            if (file == null) return;

            var resumeData = torrent.GetResumeData();
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(resumeData.Data);

            _notificationService.Publish(
                new NotificationItem(
                    Properties.Resources.Details_SaveResumeData,
                    Properties.Resources.Details_SaveResumeData_Success,
                    NotificationType.Success,
                    Material.Icons.MaterialIconKind.ContentSaveCheckOutline.ToString()),
                TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _notificationService.Publish(
                new NotificationItem(
                    Properties.Resources.Details_SaveResumeData,
                    string.Format(Properties.Resources.Details_SaveResumeData_Failed, ex.Message),
                    NotificationType.Error,
                    Material.Icons.MaterialIconKind.AlertCircleOutline.ToString()),
                TimeSpan.FromSeconds(8));
        }
    }

    // Download path change
    private bool CanChangeDownloadPath()
    {
        var torrent = _selectionService.SelectedTorrent;
        return torrent != null && torrent.State == TorrentState.Stopped;
    }

    private async Task ChangeDownloadPathAsync()
    {
        var torrent = _selectionService.SelectedTorrent;
        if (torrent == null)
        {
            return;
        }

        try
        {
            var storage = _topLevelService.GetStorageProvider();
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Properties.Resources.Details_ChangeDownloadPath_Title,
                AllowMultiple = false
            });

            if (folders.Count == 0) return;

            var newPath = folders[0].Path.LocalPath;
            await torrent.SetDownloadPathAsync(newPath);

            UIDispatcher(() =>
            {
                DownloadPath = newPath;
                _notificationService.Publish(
                    new NotificationItem(
                        Properties.Resources.Details_ChangeDownloadPath,
                        Properties.Resources.Details_ChangeDownloadPath_Success,
                        NotificationType.Success,
                        Material.Icons.MaterialIconKind.FolderMoveOutline.ToString()),
                    TimeSpan.FromSeconds(5));
            });
        }
        catch (Exception ex)
        {
            _notificationService.Publish(
                new NotificationItem(
                    Properties.Resources.Details_ChangeDownloadPath,
                    string.Format(Properties.Resources.Details_ChangeDownloadPath_Failed, ex.Message),
                    NotificationType.Error,
                    Material.Icons.MaterialIconKind.AlertCircleOutline.ToString()),
                TimeSpan.FromSeconds(8));
        }
    }

    private bool MatchesSelectedTorrent(ITorrent torrent)
    {
        var selected = _selectionService.SelectedTorrent;
        return selected != null && MatchesTorrent(selected, torrent);
    }

    private static bool MatchesTorrent(ITorrent left, ITorrent right)
    {
        return left.Hash == right.Hash
            || left.Hash == right.HashV2
            || left.HashV2 == right.Hash
            || left.HashV2 == right.HashV2;
    }
}
