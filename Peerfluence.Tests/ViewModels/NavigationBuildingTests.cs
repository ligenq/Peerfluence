using Material.Icons;
using Peerfluence.Core;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

/// <summary>
/// Turning the registered features into the navigation.
/// </summary>
/// <remarks>
/// This used to be a loop inside the main view model's constructor, which meant the ordering and
/// the fallback icon could only be reached by building a window's worth of view model. As a function
/// of its argument it can simply be called.
/// </remarks>
public sealed class NavigationBuildingTests
{
    private sealed class Feature(string title, string iconKind, int order)
        : ViewModelBase, IFeatureViewModel
    {
        public string Title { get; } = title;

        public string IconKind { get; } = iconKind;

        public int Order { get; } = order;
    }

    /// <summary>A feature that is not a view model, which is a registration mistake.</summary>
    private sealed class NotAViewModel : IFeatureViewModel
    {
        public string Title => "Nowhere";

        public string IconKind => "Magnify";

        public int Order => 0;
    }

    [Fact]
    public void FeaturesAppearInTheOrderTheyAskedFor()
    {
        var items = MainWindowViewModel.BuildNavigation(
        [
            new Feature("Settings", "Cog", 100),
            new Feature("Downloads", "Download", 0),
            new Feature("Find", "Magnify", 50),
        ]);

        Assert.Equal(["Downloads", "Find", "Settings"], items.Select(item => item.Title));
    }

    [Fact]
    public void AnIconNameThatIsRecognised_IsUsed()
    {
        var items = MainWindowViewModel.BuildNavigation([new Feature("Downloads", "Download", 0)]);

        Assert.Equal(MaterialIconKind.Download, items[0].Icon);
    }

    [Fact]
    public void AnIconNameNobodyRecognises_FallsBackRatherThanThrowing()
    {
        // The name is a string on a view model. Getting it wrong should cost a wrong picture, not a
        // window that will not open. Nothing reached this branch before.
        var items = MainWindowViewModel.BuildNavigation([new Feature("Downloads", "NotAnIconAtAll", 0)]);

        Assert.Equal(MaterialIconKind.CircleOutline, items[0].Icon);
    }

    [Fact]
    public void AFeatureThatIsNotAViewModel_SaysWhichOneAndWhy()
    {
        // It used to be an unchecked cast, so this arrived as an InvalidCastException naming neither
        // the feature nor what was expected of it - at startup, before any window appeared.
        var error = Assert.Throws<InvalidOperationException>(
            () => MainWindowViewModel.BuildNavigation([new NotAViewModel()]));

        Assert.Contains(nameof(NotAViewModel), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ViewModelBase), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFeaturesAtAll_IsAnEmptyNavigationRatherThanAFailure()
    {
        Assert.Empty(MainWindowViewModel.BuildNavigation([]));
    }

    [Fact]
    public void EachItemKeepsTheViewModelItWasBuiltFrom()
    {
        // The navigation is what the window shows a page from, so the item has to carry it.
        var feature = new Feature("Downloads", "Download", 0);

        var items = MainWindowViewModel.BuildNavigation([feature]);

        Assert.Same(feature, items[0].ViewModel);
    }
}
