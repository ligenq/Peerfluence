using CommunityToolkit.Mvvm.Messaging;
using Avalonia;
using Peerfluence.Theme.Controls;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;
using PeerSharp.Interfaces;
using PeerSharp.Core;
using PeerSharp.Config;
using Peerfluence.Core.Services;
using Peerfluence.Core.Messaging;
using Peerfluence.Services;

namespace Peerfluence.HeadlessTests;

public class DownloadsViewTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(7)]
    public void NarrowTemplateCells_TrimTextWithinTheCellAndExposeTheFullValue(int columnIndex)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("A very long torrent name that cannot fit in a narrow column.mkv");
        torrent.Hash.Returns(InfoHash.CreateRandom());
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        var item = new TorrentListItemViewModel(torrent)
        {
            Progress = 0.1234f,
            DownloadSpeedBytesPerSecond = long.MaxValue,
            UploadSpeedBytesPerSecond = long.MaxValue,
        };
        var view = new DownloadsView();
        var sourceGrid = view.FindControl<DataGrid>("TorrentDataGrid")!;
        var template = Assert.IsType<DataGridTemplateColumn>(sourceGrid.Columns[columnIndex]).CellTemplate;
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { item }, IsReadOnly = true };
        int columnWidth = columnIndex == 0 ? 100 : 60;
        grid.Columns.Add(new DataGridTemplateColumn { CellTemplate = template, Width = new DataGridLength(columnWidth) });
        var window = new Window { Content = grid, Width = 400, Height = 200 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var text = grid.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("gridCellText"));
            Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
            Assert.False(string.IsNullOrEmpty(text.Text));
            Assert.Equal(text.Text, ToolTip.GetTip(text));
            Assert.True(text.Bounds.Width < columnWidth);
        }
        finally
        {
            window.Close();
            item.Detach();
        }
    }

    /// <summary>
    /// The progress column is narrower than the width Fluent gives every progress bar at least, so
    /// the bar ran on past the cell and was cut off there: the part that showed filled up at about
    /// two thirds, and a download looked done while the figure under it said 67%.
    /// </summary>
    [AvaloniaFact]
    public void TheProgressBar_FitsItsCell_SoItFillsAsFarAsTheFigureUnderItSays()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("ubuntu-24.04.iso");
        torrent.Hash.Returns(InfoHash.CreateRandom());
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        var item = new TorrentListItemViewModel(torrent) { Progress = 0.5f };
        var source = new DownloadsView().FindControl<DataGrid>("TorrentDataGrid")!;
        var progressColumn = Assert.IsType<DataGridTemplateColumn>(source.Columns[1]);
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { item }, IsReadOnly = true };
        grid.Columns.Add(new DataGridTemplateColumn { CellTemplate = progressColumn.CellTemplate, Width = progressColumn.Width });
        var window = new Window { Content = grid, Width = 600, Height = 200 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var bar = grid.GetVisualDescendants().OfType<ProgressBar>().Single();
            var cell = bar.FindAncestorOfType<DataGridCell>()!;
            var figure = bar.Parent!.GetLogicalChildren().OfType<TextBlock>().Single();

            Assert.StartsWith("50", figure.Text, StringComparison.Ordinal);
            var barRight = bar.TranslatePoint(new Point(bar.Bounds.Width, 0), cell)!.Value.X;
            Assert.True(barRight <= cell.Bounds.Width, $"The bar ends at {barRight}, past the cell's {cell.Bounds.Width}.");
            var indicator = bar.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Indicator");
            Assert.Equal(bar.Bounds.Width / 2, indicator.Bounds.Width, tolerance: 1.0);
        }
        finally
        {
            window.Close();
            item.Detach();
        }
    }

    /// <summary>
    /// A real view model over the torrents given, for what only the filtering can show.
    /// </summary>
    private static DownloadsViewModel CreateFilteringViewModel(IReadOnlyList<string> categories, params string[] names)
    {
        var torrentService = Substitute.For<ITorrentService>();
        var torrents = names.Select(name =>
        {
            var torrent = Substitute.For<ITorrent>();
            torrent.Name.Returns(name);
            torrent.Hash.Returns(InfoHash.CreateRandom());
            torrent.HashV2.Returns(InfoHash.EmptyV2);
            torrent.State.Returns(TorrentState.Stopped);
            return torrent;
        }).ToArray();
        torrentService.GetTorrents().Returns(torrents);
        torrentService.GetStats().Returns(new EngineStats());

        var categoryService = Substitute.For<ITorrentCategoryService>();
        categoryService.Categories.Returns(categories.Select(name => new Peerfluence.Core.Config.TorrentCategory(name)).ToList());

        return new DownloadsViewModel(
            torrentService,
            new TorrentSelectionService(Substitute.For<IAppMessenger>()),
            new LocalizationService(),
            Substitute.For<ITopLevelService>(),
            Substitute.For<IDialogService>(),
            Substitute.For<IAddTorrentDialogService>(),
            new AppSettingsService(new AppPaths(), Substitute.For<IAppSettingsStore>(), new System.IO.Abstractions.FileSystem()),
            categoryService,
            TestHelpers.CreateDetailsViewModel());
    }

    /// <summary>
    /// The message used to share a cell with the table and sit underneath it. The table's panel is
    /// opaque, so a list filtered down to nothing showed an empty table and no word about why.
    /// </summary>
    [AvaloniaFact]
    public void FilteringEverythingAway_IsSaidInsideTheTable_WithAWayBack()
    {
        var vm = CreateFilteringViewModel([], "ubuntu-24.04.iso");
        var view = new DownloadsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();

        try
        {
            vm.SearchText = "nothing matches this";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var card = view.FindControl<Border>("TorrentTableCard")!;
            var state = view.FindControl<StackPanel>("NoMatchesState")!;
            Assert.True(card.IsEffectivelyVisible);
            Assert.True(state.IsEffectivelyVisible);
            Assert.Contains(state, card.GetVisualDescendants());
            Assert.True(state.Bounds.Height > 0);

            var clear = Assert.Single(state.GetVisualDescendants().OfType<Button>());
            Assert.Same(vm.ClearFiltersCommand, clear.Command);

            clear.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(state.IsEffectivelyVisible);
            Assert.Single(vm.VisibleTorrents);
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    }

    /// <summary>
    /// The chips used to sit in an Auto column, so every category added took its width from the
    /// search box until there was none of it left.
    /// </summary>
    [AvaloniaFact]
    public void ManyCategories_Wrap_RatherThanSqueezingTheSearchBoxAway()
    {
        var categories = Enumerable.Range(1, 14).Select(index => $"A category called number {index}").ToList();
        var vm = CreateFilteringViewModel(categories, "ubuntu-24.04.iso");
        var view = new DownloadsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();

        try
        {
            // The list reads the categories when told they changed, which is what loading them does.
            WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var search = view.GetVisualDescendants().OfType<TextBox>()
                .Single(box => Avalonia.Automation.AutomationProperties.GetAutomationId(box) == "SearchTextTextBox");
            Assert.True(search.Bounds.Width >= 200, $"The search box is {search.Bounds.Width} wide");

            var allChip = view.GetVisualDescendants().OfType<RadioButton>()
                .Single(chip => Avalonia.Automation.AutomationProperties.GetAutomationId(chip) == "FilterAllRadioButton");
            var firstCategory = view.GetVisualDescendants().OfType<RadioButton>()
                .First(chip => Equals(chip.Content, categories[0]));
            var allTop = allChip.TranslatePoint(new Avalonia.Point(0, 0), view)!.Value.Y;
            var firstTop = firstCategory.TranslatePoint(new Avalonia.Point(0, 0), view)!.Value.Y;
            Assert.True(allTop > firstTop, "The chips did not wrap onto a second line");
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    }

    private static (DownloadsView View, DownloadsViewModel Vm) CreateView()
    {
        var vm = TestHelpers.CreateDownloadsViewModel();
        var view = new DownloadsView { DataContext = vm };

        // Attach to window to trigger template application without rendering
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.ApplyTemplate();
        window.Presenter!.ApplyTemplate();

        return (view, vm);
    }

    [AvaloniaFact]
    public void View_CanBeCreated()
    {
        var (view, _) = CreateView();
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public void DataGrid_BindsToTorrentsCollection()
    {
        var (view, vm) = CreateView();

        var dataGrid = view.GetLogicalDescendants().OfType<DataGrid>().FirstOrDefault();
        Assert.NotNull(dataGrid);
        // The grid shows what search and the filter left, not the whole set.
        Assert.Same(vm.VisibleTorrents, dataGrid.ItemsSource);
    }

    [AvaloniaFact]
    public void DataGrid_HasExpectedColumns()
    {
        var (view, _) = CreateView();

        var dataGrid = view.GetLogicalDescendants().OfType<DataGrid>().FirstOrDefault();
        Assert.NotNull(dataGrid);
        // Name, Progress, Size, Category, State, ETA, Down, Up, Peers
        Assert.Equal(9, dataGrid.Columns.Count);
    }

    [AvaloniaFact]
    public void MagnetLink_TextBox_IsNotShownInToolbar()
    {
        var (view, _) = CreateView();

        // The search box is the only text box on this screen; the inline magnet field is long gone.
        var textBoxes = view.GetLogicalDescendants().OfType<TextBox>().ToList();
        var textBox = Assert.Single(textBoxes);
        Assert.Equal(Peerfluence.Properties.Resources.Downloads_Search, textBox.PlaceholderText);
    }

    [AvaloniaFact]
    public void StatusInfoBar_IsClosable()
    {
        var (view, _) = CreateView();

        var infoBar = view.GetLogicalDescendants().OfType<InfoBar>().FirstOrDefault();
        Assert.NotNull(infoBar);
        Assert.True(infoBar.IsClosable);
    }

    [AvaloniaFact]
    public async Task StatusInfoBar_HidingClearsStatusMessage()
    {
        var (view, vm) = CreateView();
        var infoBar = view.FindControl<InfoBar>("DownloadsStatusInfoBar");
        Assert.NotNull(infoBar);

        vm.StatusMessage = "Done";
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
        Assert.True(vm.HasStatusMessage);

        infoBar.IsVisible = false;

        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [AvaloniaFact]
    public void EmptyState_IsVisibleWhenNoTorrentsExist()
    {
        var (view, vm) = CreateView();

        Assert.False(vm.HasTorrents);
        Assert.True(vm.HasNoTorrents);
        Assert.True(view.FindControl<Control>("EmptyStateCard")!.IsVisible);
        Assert.False(view.FindControl<Control>("TorrentTableCard")!.IsVisible);
    }

    [AvaloniaFact]
    public void DetailPane_HiddenWhenNoTorrentSelected()
    {
        var (view, vm) = CreateView();

        Assert.Null(vm.SelectedTorrent);

        // The GridSplitter has IsVisible bound to NullToBoolConverter on SelectedTorrent
        var splitter = view.GetLogicalDescendants().OfType<GridSplitter>().FirstOrDefault();
        if (splitter != null)
        {
            Assert.False(splitter.IsVisible);
        }
    }

    [AvaloniaFact]
    public void DetailPane_GridSplitter_HiddenWhenNoSelection()
    {
        var (view, vm) = CreateView();
        Assert.Null(vm.SelectedTorrent);

        var splitter = view.GetLogicalDescendants().OfType<GridSplitter>().FirstOrDefault();
        if (splitter != null)
        {
            Assert.False(splitter.IsVisible);
        }
    }

    [AvaloniaFact]
    public void DetailPane_StaysClosedWhenATorrentIsSelected()
    {
        // Selecting a torrent no longer opens the pane; the toggle is what decides.
        var (view, vm) = CreateView();

        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Test Torrent");
        torrent.Hash.Returns(new PeerSharp.Core.InfoHash(new byte[20]));
        vm.SelectedTorrent = new TorrentListItemViewModel(torrent);

        Assert.False(vm.IsDetailsPaneVisible);
        var splitter = view.GetLogicalDescendants().OfType<GridSplitter>().FirstOrDefault();
        Assert.False(splitter?.IsVisible ?? false);
    }

    [AvaloniaFact]
    public void DetailPane_ClosedByDefault_LeavesItsRowsWithNoHeight()
    {
        // The bug this guards: a hidden control in a star-sized row still reserves its share, so the
        // list stopped halfway down the window with nothing underneath it.
        var (view, vm) = CreateView();

        Assert.False(vm.IsDetailsPaneVisible);

        var rows = ContentGrid(view).RowDefinitions;
        Assert.Equal(0, rows[5].Height.Value);
        Assert.Equal(0, rows[6].Height.Value);
    }

    [AvaloniaFact]
    public void TogglingTheDetailPane_GivesItsRowsHeightBackAndRemembersTheChoice()
    {
        var settingsService = new AppSettingsService(new AppPaths(), Substitute.For<IAppSettingsStore>(), new System.IO.Abstractions.FileSystem());
        var vm = TestHelpers.CreateDownloadsViewModel(
            settingsService: settingsService,
            detailsViewModel: TestHelpers.CreateDetailsViewModel());
        var view = new DownloadsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.ApplyTemplate();
        window.Presenter!.ApplyTemplate();

        vm.ToggleDetailsPaneCommand.Execute(null);

        Assert.True(vm.IsDetailsPaneVisible);
        Assert.True(settingsService.Current.ShowDetailsPane);
        var rows = ContentGrid(view).RowDefinitions;
        Assert.True(rows[6].Height.IsStar);
        Assert.True(rows[5].Height.Value > 0);

        vm.ToggleDetailsPaneCommand.Execute(null);

        Assert.False(vm.IsDetailsPaneVisible);
        Assert.False(settingsService.Current.ShowDetailsPane);
        Assert.Equal(0, ContentGrid(view).RowDefinitions[6].Height.Value);
    }

    private static Grid ContentGrid(DownloadsView view)
    {
        return view.GetLogicalDescendants().OfType<Grid>().First(grid => grid.Name == "ContentGrid");
    }

    [AvaloniaFact]
    public void BusyArea_DefaultDataContext()
    {
        var (view, vm) = CreateView();
        Assert.False(vm.IsBusy);
    }

    [AvaloniaFact]
    public void BusyArea_IsBusyProperty()
    {
        var (_, vm) = CreateView();

        vm.IsBusy = true;
        Assert.True(vm.IsBusy);

        vm.IsBusy = false;
        Assert.False(vm.IsBusy);
    }

    [AvaloniaFact]
    public async Task ActionButtons_EnableWhenTorrentSelected()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Selected Torrent");
        torrent.Hash.Returns(new InfoHash(new byte[20]));
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        torrent.State.Returns(TorrentState.Stopped);
        torrent.Started.Returns(false);
        torrent.HasMetadata.Returns(true);

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(Array.Empty<ITorrent>());
        torrentService.GetStats().Returns(new EngineStats());

        var vm = CreateRuntimeViewModel(torrentService);
        var view = new DownloadsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.ApplyTemplate();
        window.Presenter!.ApplyTemplate();

        var startButton = view.GetLogicalDescendants().OfType<Button>()
            .First(button => ReferenceEquals(button.Command, vm.StartSelectedCommand));
        var stopButton = view.GetLogicalDescendants().OfType<Button>()
            .First(button => ReferenceEquals(button.Command, vm.StopSelectedCommand));
        var removeButton = view.GetLogicalDescendants().OfType<Button>()
            .First(button => ReferenceEquals(button.Command, vm.RemoveSelectedCommand));

        Assert.False(startButton.IsEffectivelyEnabled);
        Assert.False(stopButton.IsEffectivelyEnabled);
        Assert.False(removeButton.IsEffectivelyEnabled);

        vm.SelectedTorrent = new TorrentListItemViewModel(torrent);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.True(startButton.IsEffectivelyEnabled);
        Assert.False(stopButton.IsEffectivelyEnabled);
        Assert.True(removeButton.IsEffectivelyEnabled);

        StopLoops(vm);
    }

    [AvaloniaFact]
    public void DataGridSelectionChanged_SynchronizesSelectedTorrent()
    {
        var torrentA = Substitute.For<ITorrent>();
        torrentA.Name.Returns("Torrent A");
        torrentA.Hash.Returns(new InfoHash(new byte[20]));
        torrentA.HashV2.Returns(InfoHash.EmptyV2);

        var torrentB = Substitute.For<ITorrent>();
        torrentB.Name.Returns("Torrent B");
        torrentB.Hash.Returns(InfoHash.CreateRandom());
        torrentB.HashV2.Returns(InfoHash.EmptyV2);

        var vm = TestHelpers.CreateDownloadsViewModel();
        var itemA = new TorrentListItemViewModel(torrentA);
        var itemB = new TorrentListItemViewModel(torrentB);
        vm.Torrents.Add(itemA);
        vm.Torrents.Add(itemB);

        var view = new DownloadsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.ApplyTemplate();
        window.Presenter!.ApplyTemplate();

        var dataGrid = view.FindControl<DataGrid>("TorrentDataGrid");
        Assert.NotNull(dataGrid);

        dataGrid.SelectedItem = itemB;

        Assert.Same(itemB, vm.SelectedTorrent);
    }

    [AvaloniaFact]
    public void TryGetTorrentFromEventSource_ResolvesTorrentFromRow()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Torrent");
        torrent.Hash.Returns(InfoHash.CreateRandom());
        torrent.HashV2.Returns(InfoHash.EmptyV2);

        var item = new TorrentListItemViewModel(torrent);
        var row = new DataGridRow { DataContext = item };

        var resolved = DownloadsView.TryGetTorrentFromEventSource(row);

        Assert.Same(item, resolved);
    }

    [AvaloniaFact]
    public void EmptyState_HidesWhenExistingTorrentsAreLoaded()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns("Loaded Torrent");
        torrent.Hash.Returns(new InfoHash(new byte[20]));
        torrent.HashV2.Returns(InfoHash.EmptyV2);
        torrent.State.Returns(TorrentState.Stopped);
        torrent.HasMetadata.Returns(true);

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);
        torrentService.GetStats().Returns(new EngineStats());

        var vm = CreateRuntimeViewModel(torrentService);
        var view = new DownloadsView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.ApplyTemplate();
        window.Presenter!.ApplyTemplate();

        Assert.True(vm.HasTorrents);
        Assert.False(vm.HasNoTorrents);
        Assert.False(view.FindControl<Control>("EmptyStateCard")!.IsVisible);
        Assert.True(view.FindControl<Control>("TorrentTableCard")!.IsVisible);

        StopLoops(vm);
    }

    private static DownloadsViewModel CreateRuntimeViewModel(ITorrentService torrentService)
    {
        var selectionService = new TorrentSelectionService(Substitute.For<IAppMessenger>());
        var localizationService = new LocalizationService();
        var topLevelService = Substitute.For<ITopLevelService>();
        var dialogService = Substitute.For<IDialogService>();
        var addTorrentDialogService = Substitute.For<IAddTorrentDialogService>();
        var settingsService = new AppSettingsService(new AppPaths(), Substitute.For<IAppSettingsStore>(), new System.IO.Abstractions.FileSystem());
        var detailsViewModel = TestHelpers.CreateDetailsViewModel();

        return new DownloadsViewModel(
            torrentService,
            selectionService,
            localizationService,
            topLevelService,
            dialogService,
            addTorrentDialogService,
            settingsService,
            Substitute.For<ITorrentCategoryService>(),
            detailsViewModel);
    }

    private static void StopLoops(DownloadsViewModel vm)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var cts = (CancellationTokenSource)typeof(DownloadsViewModel)
            .GetField("_loopCts", flags)!
            .GetValue(vm)!;
        cts.Cancel();
    }
}
