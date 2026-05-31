namespace Peerfluence.Core;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public record NotificationItem(string Title, string Message, NotificationType Type, string? IconKind = null, Action? OnClick = null);
