namespace Peerfluence.Core.Services;

/// <summary>
/// Shows a passing message to whoever is looking.
/// </summary>
/// <remarks>
/// It used to keep the notifications it had shown, in a collection with a matching Dismiss. Nothing
/// ever read the collection and nothing ever called Dismiss, and the only code that touched either
/// was the tests written to satisfy the rule that every public member has one. A notification centre
/// would be a fine thing to build; keeping the half of one that was never finished was not.
/// </remarks>
public interface INotificationService
{
    void Publish(NotificationItem notification, TimeSpan? autoDismiss = null);
}
