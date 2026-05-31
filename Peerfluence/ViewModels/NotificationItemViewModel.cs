using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace Peerfluence.ViewModels;

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class NotificationItemViewModel : ObservableObject
{
    public NotificationItemViewModel(string title, string message, NotificationKind kind, MaterialIconKind icon)
    {
        Title = title;
        Message = message;
        Kind = kind;
        Icon = icon;
        Timestamp = DateTimeOffset.Now;
    }

    public string Title { get; }

    public string Message { get; }

    public NotificationKind Kind { get; }

    public MaterialIconKind Icon { get; }

    public DateTimeOffset Timestamp { get; }

    public bool IsSuccess => Kind == NotificationKind.Success;

    public bool IsWarning => Kind == NotificationKind.Warning;

    public bool IsError => Kind == NotificationKind.Error;
}
