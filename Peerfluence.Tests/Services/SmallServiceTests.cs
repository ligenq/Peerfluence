using System.Diagnostics.Metrics;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using PeerSharp.Diagnostics;

namespace Peerfluence.Tests.Services;

/// <summary>
/// The list of notifications the window shows.
/// </summary>
public sealed class NotificationServiceTests
{
    private static NotificationItem Item(string title = "title") =>
        new(title, "message", NotificationType.Info, "Information");

    [Fact]
    public void APublishedNotification_JoinsTheList()
    {
        var sut = new NotificationService();

        var notification = Item();
        sut.Publish(notification);

        Assert.Same(notification, Assert.Single(sut.Notifications));
    }

    [Fact]
    public void ADismissedNotification_LeavesTheList()
    {
        var sut = new NotificationService();
        var notification = Item();
        sut.Publish(notification);

        sut.Dismiss(notification);

        Assert.Empty(sut.Notifications);
    }

    [Fact]
    public void DismissingSomethingThatWasNeverThere_ChangesNothing()
    {
        var sut = new NotificationService();
        sut.Publish(Item("kept"));

        sut.Dismiss(Item("never published"));

        Assert.Single(sut.Notifications);
    }

    [Fact]
    public void PublishingNothing_IsRejectedRatherThanStored()
    {
        var sut = new NotificationService();

        Assert.Throws<ArgumentNullException>(() => sut.Publish(null!));
    }
}

/// <summary>
/// Reading the engine's metrics, which is how lifetime byte totals are obtained.
/// </summary>
/// <remarks>
/// Driven through a real <see cref="Meter"/> named the same as PeerSharp's, because that is the
/// whole contract: this subscribes by name and by instrument name, and a rename at either end would
/// leave it silently reporting zeroes.
/// </remarks>
public sealed class EngineMetricsReaderTests
{
    [Fact]
    public void WithNothingPublishing_EverythingReadsZero()
    {
        using var sut = new EngineMetricsReader();

        var snapshot = sut.Read();

        Assert.Equal(0, snapshot.LifetimeDownloadedBytes);
        Assert.Equal(0, snapshot.LifetimeUploadedBytes);
    }

    [Fact]
    public void TheInstrumentsPeerSharpPublishes_AreTheOnesRead()
    {
        using var sut = new EngineMetricsReader();

        using var meter = new Meter(PeerSharpMetrics.MeterName);
        meter.CreateObservableGauge(PeerSharpMetrics.DownloadedInstrument, () => 4096L);
        meter.CreateObservableGauge(PeerSharpMetrics.UploadedInstrument, () => 2048L);
        meter.CreateObservableGauge(PeerSharpMetrics.ConnectedPeersInstrument, () => 7L);

        var snapshot = sut.Read();

        Assert.Equal(4096, snapshot.LifetimeDownloadedBytes);
        Assert.Equal(2048, snapshot.LifetimeUploadedBytes);
        Assert.Equal(7, snapshot.ConnectedPeers);
    }

    [Fact]
    public void AnotherMetersNumbers_AreNotMistakenForTheEngines()
    {
        using var sut = new EngineMetricsReader();

        using var stranger = new Meter("SomethingElse");
        stranger.CreateObservableGauge(PeerSharpMetrics.DownloadedInstrument, () => 999L);

        Assert.Equal(0, sut.Read().LifetimeDownloadedBytes);
    }

    [Fact]
    public void ReadingTwice_ReportsTheCurrentValueRatherThanTheSum()
    {
        // The values are cleared before each poll. Without that, an observable gauge read twice
        // would appear to double.
        using var sut = new EngineMetricsReader();

        using var meter = new Meter(PeerSharpMetrics.MeterName);
        meter.CreateObservableGauge(PeerSharpMetrics.DownloadedInstrument, () => 100L);

        sut.Read();

        Assert.Equal(100, sut.Read().LifetimeDownloadedBytes);
    }

    [Fact]
    public void ReadingAfterDisposal_AnswersZeroRatherThanThrowing()
    {
        // The MCP resource can be asked for stats while the application is shutting down.
        var sut = new EngineMetricsReader();
        sut.Dispose();

        Assert.Equal(default, sut.Read());
    }

    [Fact]
    public void DisposingTwice_IsSafe()
    {
        var sut = new EngineMetricsReader();

        sut.Dispose();
        sut.Dispose();
    }
}

/// <summary>
/// The application-wide message bus.
/// </summary>
[Collection("Messenger")]
public sealed class AppMessengerTests
{
    private sealed class Ping
    {
        public string Text { get; init; } = string.Empty;
    }

    [Fact]
    public void APublishedMessage_ReachesWhoeverRegisteredForIt()
    {
        var sut = new AppMessenger();
        Ping? received = null;

        WeakReferenceMessenger.Default.Register<Ping>(
            this,
            (_, message) => received = message);

        try
        {
            sut.Publish(new Ping { Text = "hello" });
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<Ping>(this);
        }

        Assert.Equal("hello", received?.Text);
    }
}

/// <summary>
/// The current language, and the strings read through it.
/// </summary>
/// <remarks>
/// Shares a collection with the display-name tests because the culture these set is process-wide:
/// run in parallel, this switching to Swedish decides what those read.
/// </remarks>
[Collection("Localization")]
public sealed class LocalizationServiceTests
{
    [Fact]
    public void TheIndexer_ReadsTheSameStringTheStaticLookupDoes()
    {
        // The indexer is what every binding in the XAML goes through, so it has to agree with the
        // lookup used everywhere else.
        var sut = new LocalizationService();

        Assert.Equal(LocalizationService.GetString("App_Title"), sut["App_Title"]);
    }

    [Fact]
    public void AKeyThatWasNeverWritten_ComesBackAsItself()
    {
        // Rather than empty: a screen showing "Settings_ThingIForgot" says what is missing, where a
        // blank label says only that something is wrong.
        var sut = new LocalizationService();

        Assert.Equal("NoSuchKey_AtAll", sut["NoSuchKey_AtAll"]);
    }

    [Fact]
    public void ApplyingALanguage_MakesItTheCurrentOne()
    {
        var sut = new LocalizationService();
        var original = sut.CurrentLanguage;

        try
        {
            sut.Apply("sv-SE");
            Assert.Equal("sv-SE", sut.CurrentLanguage);
        }
        finally
        {
            sut.Apply(original);
        }
    }
}

/// <summary>
/// One entry in the side menu.
/// </summary>
public sealed class NavigationItemTests
{
    [Fact]
    public void ANavigationItem_KeepsWhatItWasBuiltWith()
    {
        var page = new AboutViewModel(NullLogger<AboutViewModel>.Instance);

        var item = new NavigationItem("Downloads", Material.Icons.MaterialIconKind.Download, page);

        Assert.Equal("Downloads", item.Title);
        Assert.Equal(Material.Icons.MaterialIconKind.Download, item.Icon);
        Assert.Same(page, item.ViewModel);
    }

    [Fact]
    public void RenamingAnItem_TellsTheMenuToRedraw()
    {
        // The titles are localized, so they change under the menu when the language does.
        var item = new NavigationItem(
            "Downloads",
            Material.Icons.MaterialIconKind.Download,
            new AboutViewModel(NullLogger<AboutViewModel>.Instance));

        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.Title = "Nedladdningar";

        Assert.Contains(nameof(NavigationItem.Title), changed);
    }
}
