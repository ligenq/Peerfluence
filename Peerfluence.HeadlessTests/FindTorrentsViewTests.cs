using Avalonia.Controls;
using Avalonia.LogicalTree;
using NSubstitute;
using Peerfluence.Core.Services;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using Peerfluence.Views;
using SukiUI.Controls;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The screen really rendering. The XAML compiler proves the markup parses; only building the
/// control proves every binding target exists and every resource key resolves.
/// </summary>
public class FindTorrentsViewTests
{
    private static (FindTorrentsView View, FindTorrentsViewModel Vm) CreateView(bool configured)
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.IsConfigured.Returns(configured);

        var vm = new FindTorrentsViewModel(
            searchService,
            Substitute.For<ITorrentService>(),
            Substitute.For<IAddTorrentDialogService>());

        var view = new FindTorrentsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.ApplyTemplate();
        window.Presenter!.ApplyTemplate();

        return (view, vm);
    }

    [AvaloniaFact]
    public void View_CanBeCreated()
    {
        var (view, _) = CreateView(configured: true);
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public void SettingTheDataContext_ReadsWhetherAnEndpointIsConfigured()
    {
        var (_, vm) = CreateView(configured: true);

        Assert.True(vm.IsConfigured);
    }

    /// <summary>
    /// An empty grid with a search box above it looks like a search that found nothing. Until an
    /// endpoint is connected there is nothing to search, so the screen says that instead.
    /// </summary>
    [AvaloniaFact]
    public void TheQueryBarAndTheResults_AreReplacedByAnExplanation_UntilAnEndpointIsConnected()
    {
        var (view, _) = CreateView(configured: false);

        Assert.False(view.FindControl<Grid>("QueryBar")!.IsVisible);
        Assert.False(view.FindControl<GlassCard>("ResultsCard")!.IsVisible);
        Assert.True(view.FindControl<StackPanel>("EmptyState")!.IsVisible);
    }

    [AvaloniaFact]
    public void TheExplanation_StepsAside_OnceAnEndpointIsConnected()
    {
        var (view, _) = CreateView(configured: true);

        Assert.True(view.FindControl<Grid>("QueryBar")!.IsVisible);
        Assert.True(view.FindControl<GlassCard>("ResultsCard")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("EmptyState")!.IsVisible);
    }

    [AvaloniaFact]
    public void TheResultsGrid_ShowsTheColumnsWorthSkimming()
    {
        var (view, _) = CreateView(configured: true);

        var grid = view.GetLogicalDescendants().OfType<DataGrid>().Single();

        // Name, Size, Seeds, Peers, Indexer, Age.
        Assert.Equal(6, grid.Columns.Count);
        Assert.All(grid.Columns, column => Assert.NotNull(column.Header));
    }

    [AvaloniaFact]
    public void PressingEnterInTheQueryBox_Searches()
    {
        var (view, _) = CreateView(configured: true);

        var queryBox = view.GetLogicalDescendants().OfType<TextBox>().Single();

        Assert.Single(queryBox.KeyBindings);
    }
}
