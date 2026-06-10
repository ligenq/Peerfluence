using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Peerfluence.ViewModels;

public sealed class TrackerStatusItemViewModel : ObservableObject
{
    private TrackerStatusType _status;

    public TrackerStatusItemViewModel(TrackerStatus status)
    {
        Url = status.Url;
        UpdateFrom(status);
    }

    public void UpdateFrom(TrackerStatus status)
    {
        _status = status.Status;
        State = PriorityOptions.GetTrackerStatusDisplayName(_status);
        LastAnnounce = status.LastAnnounce;
        NextAnnounce = status.NextAnnounce;
        SeedCount = status.SeedCount;
        LeechCount = status.LeechCount;
        LastError = status.LastError ?? string.Empty;
    }

    public void RefreshLocalizedText()
    {
        State = PriorityOptions.GetTrackerStatusDisplayName(_status);
    }

    public string Url { get; }

    public string State
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public DateTimeOffset LastAnnounce
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public DateTimeOffset NextAnnounce
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public uint SeedCount
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public uint LeechCount
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string LastError
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;
}
