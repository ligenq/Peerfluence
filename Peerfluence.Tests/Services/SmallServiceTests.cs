using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.Services;

/// <summary>
/// The list of notifications the window shows.
/// </summary>
public sealed class NotificationServiceTests
{
    [Fact]
    public void PublishingNothing_IsRejectedRatherThanStored()
    {
        var sut = new NotificationService(Substitute.For<SukiUI.Toasts.ISukiToastManager>());

        Assert.Throws<ArgumentNullException>(() => sut.Publish(null!));
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
