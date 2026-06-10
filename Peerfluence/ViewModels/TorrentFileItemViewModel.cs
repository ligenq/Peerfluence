using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Peerfluence.ViewModels;

public sealed class TorrentFileItemViewModel : ObservableObject
{
    private bool _lastServerSelected;
    private Priority _lastServerPriority;

    public TorrentFileItemViewModel(TorrentFileInfo info, FileSelection selection, bool isStreamable = false)
    {
        Index = info.Index;
        Path = info.Path;
        SizeBytes = info.Size;
        DownloadedBytes = info.DownloadedBytes;
        Progress = info.Progress;
        IsSelected = selection.Selected;
        Priority = selection.Priority;
        IsStreamable = isStreamable;

        _lastServerSelected = selection.Selected;
        _lastServerPriority = selection.Priority;
    }

    public void UpdateFrom(TorrentFileInfo info, FileSelection selection, bool isStreamable)
    {
        DownloadedBytes = info.DownloadedBytes;
        Progress = info.Progress;
        IsStreamable = isStreamable;

        // Sync Selected state:
        // 1. If we are already in sync with server, update both.
        // 2. If UI != Server, but UI == last known server, then server changed. Update.
        // 3. Otherwise user changed it, keep UI value.
        if (selection.Selected == IsSelected)
        {
            _lastServerSelected = selection.Selected;
        }
        else if (IsSelected == _lastServerSelected)
        {
            IsSelected = selection.Selected;
            _lastServerSelected = selection.Selected;
        }
        else
        {
            _lastServerSelected = selection.Selected;
        }

        // Sync Priority:
        if (selection.Priority == Priority)
        {
            _lastServerPriority = selection.Priority;
        }
        else if (Priority == _lastServerPriority)
        {
            Priority = selection.Priority;
            _lastServerPriority = selection.Priority;
        }
        else
        {
            _lastServerPriority = selection.Priority;
        }
    }

    public int Index { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public long DownloadedBytes
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public float Progress
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsStreamable
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public static IReadOnlyList<EnumDisplayOption<Priority>> PriorityOptions => ViewModels.PriorityOptions.Localized;

    public bool IsSelected
    {
        get;
        set => SetProperty(ref field, value);
    }

    public Priority Priority
    {
        get;
        set => SetProperty(ref field, value);
    }
}
