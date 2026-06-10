using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Peerfluence.ViewModels;

public sealed class AddTorrentFileOptionViewModel : ObservableObject
{
    public AddTorrentFileOptionViewModel(int index, string path, long sizeBytes)
    {
        Index = index;
        Path = path;
        SizeBytes = sizeBytes;
    }

    public int Index { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public static IReadOnlyList<EnumDisplayOption<Priority>> PriorityOptions => ViewModels.PriorityOptions.Localized;

    public bool IsSelected
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !value)
            {
                Priority = Priority.DoNotDownload;
            }
            else if (value && Priority == Priority.DoNotDownload)
            {
                Priority = Priority.Normal;
            }
        }
    } = true;

    public Priority Priority
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value != Priority.DoNotDownload && !IsSelected)
            {
                IsSelected = true;
            }
        }
    } = Priority.Normal;
}
