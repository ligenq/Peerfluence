using Peerfluence.Theme.Controls;
using Avalonia.Threading;
using Peerfluence.Core;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// Showing a notification from wherever the news happened to arrive.
/// </summary>
/// <remarks>
/// Here rather than in the unit tests because the marshalling is the thing worth testing, and a
/// dispatcher that never runs cannot show that anything was marshalled to it.
/// </remarks>
public class NotificationServiceTests
{
    private static NotificationItem Item() =>
        new("Torrent finished", "Ubuntu ISO", NotificationType.Success, "Information");

    [AvaloniaFact]
    public void ANotificationFromABackgroundThread_StillReachesTheToast()
    {
        // How the application actually publishes most of them: a torrent finishing and a completion
        // action failing are both reported from a hosted service, on whatever thread the engine's
        // alert arrived on. Those callers have no reason to know that saying so touches the
        // interface, which is why the service marshals rather than asking them to.
        var manager = new ToastManager();
        var sut = new NotificationService(manager);

        Task.Run(() => sut.Publish(Item())).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(manager.Toasts);
    }

    [AvaloniaFact]
    public void ANotificationFromTheUiThread_ArrivesJustTheSame()
    {
        var manager = new ToastManager();
        var sut = new NotificationService(manager);

        sut.Publish(Item());
        Dispatcher.UIThread.RunJobs();

        Assert.Single(manager.Toasts);
    }

    [AvaloniaFact]
    public void PublishingNothing_IsRefusedWhereTheCallerCanSeeIt()
    {
        // On the calling thread rather than inside the posted work, where nobody would catch it.
        var sut = new NotificationService(Substitute.For<IToastManager>());

        Assert.Throws<ArgumentNullException>(() => sut.Publish(null!));
    }
}
