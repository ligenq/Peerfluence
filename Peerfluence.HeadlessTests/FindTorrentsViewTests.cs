using Avalonia.Controls;
using Avalonia.LogicalTree;
using Peerfluence.Core.Services;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using Peerfluence.Views;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The screen really rendering. The XAML compiler proves the markup parses; only building the
/// control proves every binding target exists and every resource key resolves.
/// </summary>
public class FindTorrentsViewTests
{
    private static (FindTorrentsView View, FindTorrentsViewModel Vm) CreateView(
        bool configured,
        IAddTorrentDialogService? addTorrentDialogService = null)
    {
        var searchService = Substitute.For<ITorrentSearchService>();
        searchService.IsConfigured.Returns(configured);

        var vm = new FindTorrentsViewModel(
            searchService,
            Substitute.For<ITorrentService>(),
            addTorrentDialogService ?? Substitute.For<IAddTorrentDialogService>());

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
        Assert.False(view.FindControl<Border>("ResultsCard")!.IsVisible);
        Assert.True(view.FindControl<StackPanel>("EmptyState")!.IsVisible);
    }

    [AvaloniaFact]
    public void TheExplanation_StepsAside_OnceAnEndpointIsConnected()
    {
        var (view, _) = CreateView(configured: true);

        Assert.True(view.FindControl<Grid>("QueryBar")!.IsVisible);
        Assert.True(view.FindControl<Border>("ResultsCard")!.IsVisible);
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

    /// <summary>
    /// The empty state used to be a paragraph and nothing else: it named the settings and left the
    /// user to go and find them.
    /// </summary>
    [AvaloniaFact]
    public void TheExplanation_CarriesAWayToActOnIt()
    {
        var (view, vm) = CreateView(configured: false);

        var button = view.FindControl<Button>("SetUpIndexerButton")!;

        Assert.True(button.IsVisible);
        Assert.Same(vm.OpenSearchSettingsCommand, button.Command);
    }

    [AvaloniaFact]
    public void TheOfferToFixIt_AppearsOnlyWhenTheSettingsCouldFixIt()
    {
        var (view, _) = CreateView(configured: true);

        var button = view.FindControl<Button>("FixInSettingsButton")!;

        Assert.False(button.IsVisible);
    }

    private static TorrentSearchResultViewModel AResult()
    {
        return new TorrentSearchResultViewModel(new TorrentSearchResult(
            "ubuntu-24.04.iso", SizeBytes: 1024, Seeders: 10, Peers: 2, IndexerName: "Example",
            PublishedAt: null, Link: "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"));
    }

    /// <summary>
    /// Double-click and the context menu both add, and neither can be seen. The button can, and it
    /// is there only once there is something selected to add.
    /// </summary>
    [AvaloniaFact]
    public void TheResults_CarryAnAddButton_ThatFollowsTheSelection()
    {
        var (view, vm) = CreateView(configured: true);

        var button = view.FindControl<Button>("AddSelectedButton")!;

        Assert.True(button.IsVisible);
        Assert.Same(vm.AddCommand, button.Command);
        Assert.False(button.IsEffectivelyEnabled);

        vm.SelectedResult = AResult();

        Assert.True(button.IsEffectivelyEnabled);
    }

    /// <summary>
    /// The grid takes Enter for itself, to move down a row, so this has to get to it first.
    /// </summary>
    [AvaloniaFact]
    public async Task PressingEnterOnAResult_AddsIt()
    {
        var addTorrentDialogService = Substitute.For<IAddTorrentDialogService>();
        var (view, vm) = CreateView(configured: true, addTorrentDialogService);
        var result = AResult();
        vm.Results.Add(result);
        vm.SelectedResult = result;
        var grid = view.FindControl<DataGrid>("ResultsGrid")!;

        var enter = new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
            Source = grid,
        };
        grid.RaiseEvent(enter);

        Assert.True(enter.Handled);
        await addTorrentDialogService.Received(1).ShowMagnetAsync(result.Link);
    }

    [AvaloniaFact]
    public void PressingEnterInTheQueryBox_Searches()
    {
        var (view, _) = CreateView(configured: true);

        var queryBox = view.GetLogicalDescendants().OfType<TextBox>().Single();

        Assert.Single(queryBox.KeyBindings);
    }
}
