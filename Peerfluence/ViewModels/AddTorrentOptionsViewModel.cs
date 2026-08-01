using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Peerfluence.Properties;
using PeerSharp.Config;

namespace Peerfluence.ViewModels;

public partial class AddTorrentOptionsViewModel : ViewModelBase
{
    private readonly ITorrentService _torrentService;
    private readonly ITopLevelService _topLevelService;
    private readonly IAppSettingsService _settingsService;
    private readonly string _source;
    private readonly bool _isMagnet;
    private CancellationTokenSource? _metadataPreviewCts;
    private Task? _metadataPreviewTask;
    private TorrentFile? _previewTorrentFile;

    private AddTorrentOptionsViewModel(
        ITorrentService torrentService,
        ITopLevelService topLevelService,
        IAppSettingsService settingsService,
        string source,
        bool isMagnet)
    {
        _torrentService = torrentService;
        _topLevelService = topLevelService;
        _settingsService = settingsService;
        _source = source;
        _isMagnet = isMagnet;

        AddCommand = new AsyncRelayCommand(AddAsync, CanAdd);
        CancelCommand = new RelayCommand(RequestClose);
        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(IsMetadataPending));
        };
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private bool _startImmediately = true;

    [ObservableProperty]
    private string _additionalTrackers = string.Empty;

    [ObservableProperty]
    private int _downloadLimitKiBPerSecond;

    [ObservableProperty]
    private int _uploadLimitKiBPerSecond;

    [ObservableProperty]
    private int _queuePriority;

    [ObservableProperty]
    private float _ratioLimit;

    [ObservableProperty]
    private int _seedTimeLimitMinutes;

    [ObservableProperty]
    private bool _skipThisStepNextTime;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isFetchingMetadata;

    [ObservableProperty]
    private string _metadataStatusText = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _sourceLabel = string.Empty;

    [ObservableProperty]
    private string _hash = string.Empty;

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExistingTrackers))]
    private string _existingTrackers = string.Empty;

    [ObservableProperty]
    private long _totalSizeBytes;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private int _pieceCount;

    [ObservableProperty]
    private long _pieceSizeBytes;

    [ObservableProperty]
    private bool _isPrivate;

    public event Action? OnRequestClose;

    public IAsyncRelayCommand AddCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public ObservableCollection<AddTorrentFileOptionViewModel> Files { get; } = new();

    public string Title => _isMagnet ? Resources.Downloads_AddMagnet : Resources.Downloads_AddTorrent;

    public bool HasFiles => Files.Count > 0;

    public bool IsMagnet => _isMagnet;

    public bool IsMetadataPending => IsMagnet && !HasFiles;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasExistingTrackers => !string.IsNullOrWhiteSpace(ExistingTrackers);

    public bool WasAdded { get; private set; }

    public static async Task<AddTorrentOptionsViewModel> CreateForTorrentFileAsync(
        string torrentPath,
        ITorrentService torrentService,
        ITopLevelService topLevelService,
        IAppSettingsService settingsService)
    {
        var torrentFile = await TorrentFile.LoadAsync(torrentPath);
        var model = new AddTorrentOptionsViewModel(torrentService, topLevelService, settingsService, torrentPath, isMagnet: false)
        {
            // Kept so the add reuses the file we already parsed rather than reading it a second time.
            _previewTorrentFile = torrentFile,
            Name = torrentFile.Name,
            SourceLabel = torrentPath,
            Hash = torrentFile.InfoHash.IsEmpty ? torrentFile.InfoHashV2.ToString() : torrentFile.InfoHash.ToString(),
            VersionLabel = GetVersionLabel(torrentFile.IsV1, torrentFile.IsV2, torrentFile.IsHybrid),
            TotalSizeBytes = torrentFile.TotalSize,
            FileCount = torrentFile.FileCount,
            PieceCount = torrentFile.PieceCount,
            PieceSizeBytes = torrentFile.PieceSize,
            IsPrivate = torrentFile.IsPrivate,
            ExistingTrackers = string.Join(Environment.NewLine, torrentFile.Trackers),
            DownloadPath = Path.Combine(GetDefaultDownloadPath(settingsService), torrentFile.Name)
        };

        foreach (var file in torrentFile.GetFiles())
        {
            model.Files.Add(new AddTorrentFileOptionViewModel(file.Index, file.Path, file.Size));
        }

        return model;
    }

    public static AddTorrentOptionsViewModel CreateForMagnet(
        string magnetUri,
        ITorrentService torrentService,
        ITopLevelService topLevelService,
        IAppSettingsService settingsService)
    {
        var magnet = MagnetLink.Parse(magnetUri);
        return new AddTorrentOptionsViewModel(torrentService, topLevelService, settingsService, magnetUri, isMagnet: true)
        {
            Name = string.IsNullOrWhiteSpace(magnet.DisplayName) ? Resources.AddTorrent_MagnetFallbackName : magnet.DisplayName!,
            SourceLabel = magnetUri,
            Hash = magnet.InfoHash.IsEmpty ? magnet.InfoHashV2.ToString() : magnet.InfoHash.ToString(),
            VersionLabel = GetVersionLabel(magnet.IsV1, magnet.IsV2, magnet.IsHybrid),
            ExistingTrackers = string.Join(Environment.NewLine, magnet.Trackers),
            DownloadPath = GetDefaultDownloadPath(settingsService)
        };
    }

    public void StartMetadataPreview(IMagnetMetadataPreviewService previewService, TimeSpan timeout)
    {
        if (!_isMagnet)
        {
            return;
        }

        _metadataPreviewCts?.Cancel();
        _metadataPreviewCts = new CancellationTokenSource();
        IsFetchingMetadata = true;
        MetadataStatusText = Resources.Status_FetchingMetadata;
        _metadataPreviewTask = FetchMetadataPreviewAsync(previewService, timeout, _metadataPreviewCts);
    }

    internal void ApplyMetadataPreview(MagnetMetadataPreview preview)
    {
        _previewTorrentFile = preview.TorrentFile;
        Name = preview.Name;
        Hash = preview.Hash;
        VersionLabel = preview.VersionLabel;
        TotalSizeBytes = preview.TotalSizeBytes;
        FileCount = preview.FileCount;
        PieceCount = preview.PieceCount;
        PieceSizeBytes = preview.PieceSizeBytes;
        IsPrivate = preview.IsPrivate;
        ExistingTrackers = string.Join(Environment.NewLine, preview.Trackers);

        var defaultDownloadPath = GetDefaultDownloadPath(_settingsService);
        if (string.Equals(DownloadPath, defaultDownloadPath, StringComparison.OrdinalIgnoreCase))
        {
            DownloadPath = Path.Combine(defaultDownloadPath, preview.Name);
        }

        Files.Clear();
        foreach (var file in preview.Files)
        {
            Files.Add(new AddTorrentFileOptionViewModel(file.Index, file.Path, file.SizeBytes));
        }

        OnPropertyChanged(nameof(IsMetadataPending));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    private async Task BrowseDownloadPathAsync()
    {
        var folders = await _topLevelService.GetStorageProvider().OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Resources.Settings_DownloadFolderPicker_Title,
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder != null)
        {
            DownloadPath = folder.Path.LocalPath;
        }
    }

    [RelayCommand]
    private Task BrowseDownloadPath()
    {
        return BrowseDownloadPathAsync();
    }

    private bool CanAdd()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(DownloadPath);
    }

    private async Task AddAsync()
    {
        ErrorMessage = string.Empty;

        try
        {
            IsBusy = true;

            // The preview fetches metadata by adding this very hash to the engine and removing it
            // again. Cancelling only starts that unwind, so adding before it finishes would hit the
            // engine's duplicate guard and fail an add the user did nothing wrong to make.
            await CancelMetadataPreviewAsync();

            var options = BuildOptions();
            if (_previewTorrentFile != null)
            {
                // Both a .torrent file and a magnet whose preview resolved end up here; adding the
                // parsed file spares the magnet a second metadata download.
                await _torrentService.AddTorrentAsync(_previewTorrentFile, options);
            }
            else
            {
                await _torrentService.AddMagnetAsync(_source, options);
            }

            if (SkipThisStepNextTime)
            {
                _settingsService.Current.ShowAddTorrentOptions = false;
                await _settingsService.SaveAsync(default);
            }

            WasAdded = true;
            RequestClose();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal AddTorrentOptions BuildOptions()
    {
        var options = new AddTorrentOptions
        {
            DownloadPath = DownloadPath.Trim(),
            StartImmediately = StartImmediately,
            QueuePriority = QueuePriority,
            DownloadLimitBytesPerSecond = ToBytesPerSecond(DownloadLimitKiBPerSecond),
            UploadLimitBytesPerSecond = ToBytesPerSecond(UploadLimitKiBPerSecond),
            RatioLimit = RatioLimit > 0 ? RatioLimit : null,
            SeedTimeLimit = SeedTimeLimitMinutes > 0 ? TimeSpan.FromMinutes(SeedTimeLimitMinutes) : null
        };

        var trackers = ParseTrackers(AdditionalTrackers);
        if (trackers.Count > 0)
        {
            options.AdditionalTrackers = trackers;
        }

        if (Files.Count > 0)
        {
            options.FileSelections = Files
                .OrderBy(file => file.Index)
                .Select(file => new FileSelection(file.IsSelected, file.IsSelected ? file.Priority : Priority.DoNotDownload))
                .ToList();
        }

        return options;
    }

    private void RequestClose()
    {
        CancelMetadataPreview();
        OnRequestClose?.Invoke();
    }

    private async Task FetchMetadataPreviewAsync(
        IMagnetMetadataPreviewService previewService,
        TimeSpan timeout,
        CancellationTokenSource cancellationTokenSource)
    {
        var cancellationToken = cancellationTokenSource.Token;

        try
        {
            var preview = await previewService.FetchAsync(_source, timeout, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await RunOnUiThreadAsync(() =>
            {
                if (preview == null)
                {
                    MetadataStatusText = Resources.AddTorrent_MetadataPreviewUnavailable;
                    return;
                }

                MetadataStatusText = string.Empty;
                ApplyMetadataPreview(preview);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Metadata preview is opportunistic; the user can still add the magnet. Say so rather
            // than leaving "fetching metadata" on screen for a fetch that has already given up -
            // an engine that already holds this hash lands here every time.
            await RunOnUiThreadAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    MetadataStatusText = Resources.AddTorrent_MetadataPreviewUnavailable;
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                if (ReferenceEquals(_metadataPreviewCts, cancellationTokenSource))
                {
                    _metadataPreviewCts = null;
                    _metadataPreviewTask = null;
                    IsFetchingMetadata = false;
                }

                cancellationTokenSource.Dispose();
            }).ConfigureAwait(false);
        }
    }

    private void CancelMetadataPreview()
    {
        _metadataPreviewCts?.Cancel();
        _metadataPreviewCts = null;
        IsFetchingMetadata = false;
        MetadataStatusText = string.Empty;
    }

    /// <summary>
    /// Cancels an in-flight preview and waits for it to finish releasing the engine resources it
    /// took. <see cref="FetchMetadataPreviewAsync"/> handles its own failures, so the returned task
    /// completes rather than faults.
    /// </summary>
    private Task CancelMetadataPreviewAsync()
    {
        var pending = _metadataPreviewTask;
        CancelMetadataPreview();
        return pending ?? Task.CompletedTask;
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private static IReadOnlyList<string> ParseTrackers(string value)
    {
        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int? ToBytesPerSecond(int kibPerSecond)
    {
        return kibPerSecond > 0 ? checked(kibPerSecond * 1024) : null;
    }

    private static string GetDefaultDownloadPath(IAppSettingsService settingsService)
    {
        return string.IsNullOrWhiteSpace(settingsService.Current.Storage.DownloadPath)
            ? settingsService.CreateDefaultSettings().Storage.DownloadPath
            : settingsService.Current.Storage.DownloadPath;
    }

    private static string GetVersionLabel(bool isV1, bool isV2, bool isHybrid)
    {
        if (isHybrid)
        {
            return "V1 + V2";
        }

        if (isV2)
        {
            return "V2";
        }

        return isV1 ? "V1" : Resources.Common_Unknown;
    }
}
