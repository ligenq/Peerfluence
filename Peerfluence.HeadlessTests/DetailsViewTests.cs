using Avalonia.Controls;
using Avalonia.LogicalTree;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.ViewModels;
using Peerfluence.Views;

namespace Peerfluence.HeadlessTests;

public class DetailsViewTests
{
    private static (DetailsView View, DetailsViewModel Vm) CreateView()
    {
        var vm = TestHelpers.CreateDetailsViewModel();
        var view = new DetailsView { DataContext = vm };

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
    public void TabControl_HasExpectedTabs()
    {
        var (view, _) = CreateView();

        var tabControl = view.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault();
        Assert.NotNull(tabControl);
        // Files, Trackers, Peers, Pieces, Settings
        Assert.Equal(5, tabControl.Items.Count);
    }

    [AvaloniaFact]
    public void ProgressBar_Exists()
    {
        var (view, _) = CreateView();

        var progressBars = view.GetLogicalDescendants().OfType<ProgressBar>().ToList();
        Assert.NotEmpty(progressBars);
    }

    [AvaloniaFact]
    public void RecheckProgressPanel_HiddenByDefault()
    {
        var (_, vm) = CreateView();
        Assert.False(vm.IsRechecking);
    }

    [AvaloniaFact]
    public void FilesTab_HasDataGrid()
    {
        var (view, _) = CreateView();

        var dataGrids = view.GetLogicalDescendants().OfType<DataGrid>().ToList();
        Assert.NotEmpty(dataGrids);
    }

    [AvaloniaFact]
    public void Header_ContainsNameTextBlock()
    {
        var (view, _) = CreateView();

        var textBlocks = view.GetLogicalDescendants().OfType<TextBlock>().ToList();
        // Should have text blocks for name, state, size, downloaded, peers, hash, path
        Assert.True(textBlocks.Count > 5, $"Expected more than 5 TextBlocks in header area, found {textBlocks.Count}");
    }

    [AvaloniaFact]
    public void NumericUpDowns_ExistInSettingsTab()
    {
        var (view, _) = CreateView();

        var nuds = view.GetLogicalDescendants().OfType<NumericUpDown>().ToList();
        // Download limit, Upload limit, Disk read, Disk write, Queue priority
        Assert.True(nuds.Count >= 5, $"Expected at least 5 NumericUpDowns, found {nuds.Count}");
    }
}
