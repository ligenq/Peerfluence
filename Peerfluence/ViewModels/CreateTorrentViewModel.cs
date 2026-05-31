using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Peerfluence.ViewModels;

public partial class CreateTorrentViewModel : ViewModelBase
{
    private readonly ITopLevelService _topLevelService;
    private readonly ILogger<CreateTorrentViewModel> _logger;

    public CreateTorrentViewModel(ITopLevelService topLevelService, ILogger<CreateTorrentViewModel> logger)
    {
        _topLevelService = topLevelService;
        _logger = logger;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _trackers = string.Empty;

    [ObservableProperty]
    private bool _isPrivate;

    [ObservableProperty]
    private string _webSeeds = string.Empty;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private int _selectedPieceSizeIndex = 2; // Default to 1MB (index 2 in list)

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isCreating;

    public List<string> PieceSizes { get; } = new()
    {
        "256 KiB",
        "512 KiB",
        "1 MiB",
        "2 MiB",
        "4 MiB",
        "8 MiB",
        "16 MiB"
    };

    private readonly uint[] _pieceSizeValues =
    {
        256 * 1024,
        512 * 1024,
        1024 * 1024,
        2 * 1024 * 1024,
        4 * 1024 * 1024,
        8 * 1024 * 1024,
        16 * 1024 * 1024
    };

    [RelayCommand]
    private async Task BrowseSource()
    {
        var storage = _topLevelService.GetStorageProvider();
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Properties.Resources.CreateTorrent_SelectSourceFolder,
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            SourcePath = folders[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task BrowseSourceFile()
    {
        var storage = _topLevelService.GetStorageProvider();
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Properties.Resources.CreateTorrent_SelectSourceFile,
            AllowMultiple = false
        });

        if (files.Count > 0)
        {
            SourcePath = files[0].Path.LocalPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task Create()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            ErrorMessage = Properties.Resources.CreateTorrent_ErrorNoSource;
            return;
        }

        if (!File.Exists(SourcePath) && !Directory.Exists(SourcePath))
        {
            ErrorMessage = Properties.Resources.CreateTorrent_ErrorSourceNotFound;
            return;
        }

        var storage = _topLevelService.GetStorageProvider();
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Properties.Resources.CreateTorrent_SaveTorrentFile,
            DefaultExtension = "torrent",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(Properties.Resources.CreateTorrent_TorrentFileType)
                {
                    Patterns = new[] { "*.torrent" }
                }
            }
        });

        if (file == null) return;

        IsCreating = true;
        try
        {
            var builder = new TorrentFileBuilder();

            if (File.Exists(SourcePath))
            {
                builder.AddFileFromPath(SourcePath);
            }
            else if (Directory.Exists(SourcePath))
            {
                var dirInfo = new DirectoryInfo(SourcePath);
                var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                if (files.Length == 0)
                {
                    ErrorMessage = Properties.Resources.CreateTorrent_ErrorNoFiles;
                    return;
                }

                foreach (var f in files)
                {
                    var relPath = Path.GetRelativePath(dirInfo.FullName, f.FullName);
                    var torrentPath = Path.Combine(dirInfo.Name, relPath);
                    builder.AddFileFromPath(f.FullName, torrentPath);
                }
            }

            var trackers = Trackers
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tracker => tracker.Trim())
                .Where(tracker => tracker.Length > 0)
                .ToArray();
            if (trackers.Length > 0)
            {
                builder.AddTrackerTier(trackers);
            }

            var webSeeds = WebSeeds
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(url => url.Trim())
                .Where(url => url.Length > 0)
                .ToArray();
            foreach (var url in webSeeds)
            {
                builder.AddWebSeed(url);
            }

            var pieceIndex = SelectedPieceSizeIndex;
            if (pieceIndex < 0 || pieceIndex >= _pieceSizeValues.Length)
            {
                pieceIndex = 2;
            }

            builder.WithPieceLength(_pieceSizeValues[pieceIndex]);
            builder.WithPrivate(IsPrivate);

            var torrent = await builder.BuildAsync();

            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(torrent.RawData);

            ErrorMessage = string.Empty;
            OnRequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create torrent");
            ErrorMessage = Properties.Resources.CreateTorrent_ErrorFailed;
        }
        finally
        {
            IsCreating = false;
        }
    }

    private bool CanCreate()
    {
        return !IsCreating && !string.IsNullOrWhiteSpace(SourcePath);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnSourcePathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ErrorMessage = string.Empty;
        }
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    public event Action? OnRequestClose;
}
