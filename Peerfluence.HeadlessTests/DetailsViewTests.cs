using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>
    /// The two buttons under the file grid stay apart at a width that cannot hold both.
    /// </summary>
    /// <remarks>
    /// They used to share one grid cell, held apart only by opposing HorizontalAlignment, which
    /// overlaps rather than running out of room: squeeze the pane, or use a language whose labels
    /// are longer, and "Stop streaming" is drawn over "Apply file selection". A column each means
    /// the labels trim instead.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1200)]
    [InlineData(420)]
    [InlineData(240)]
    public void FileActionButtons_NeverOverlapHoweverNarrowThePaneIs(int width)
    {
        var vm = TestHelpers.CreateDetailsViewModel();
        var view = new DetailsView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = 800 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var stop = Find(view, "StopStreamingButton");
            var apply = Find(view, "ApplyFileSelectionButton");
            Assert.NotSame(stop, apply);

            // Both are laid out even while Stop is collapsed, so compare the slots they occupy.
            var stopRight = stop.TranslatePoint(new Point(stop.Bounds.Width, 0), view);
            var applyLeft = apply.TranslatePoint(default, view);
            Assert.NotNull(stopRight);
            Assert.NotNull(applyLeft);
            Assert.True(stopRight.Value.X <= applyLeft.Value.X + 0.5,
                $"at {width}px the buttons overlap: stop ends at {stopRight.Value.X}, apply starts at {applyLeft.Value.X}");
        }
        finally
        {
            window.Close();
            ((IDisposable)vm).Dispose();
        }
    }

    private static Control Find(DetailsView view, string automationId) =>
        view.GetVisualDescendants().OfType<Control>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    [AvaloniaFact]
    public void TabControl_HasExpectedTabs()
    {
        var (view, _) = CreateView();

        var tabControl = view.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault();
        Assert.NotNull(tabControl);
        // Files, Trackers, Peers, Web seeds, Pieces, Settings
        Assert.Equal(6, tabControl.Items.Count);
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
    public void PeersTab_OffersAnAddressBoxWiredToTheAddCommand()
    {
        var (view, vm) = CreateView();

        var tabControl = view.GetLogicalDescendants().OfType<TabControl>().First();
        var peersTab = tabControl.Items.OfType<TabItem>().ElementAt(2);
        var addressBox = peersTab.GetLogicalDescendants().OfType<TextBox>().First();
        var addButton = peersTab.GetLogicalDescendants().OfType<Button>().First();

        addressBox.Text = "192.168.1.10:51413";

        Assert.Equal("192.168.1.10:51413", vm.NewPeerAddresses);
        Assert.Same(vm.AddPeersCommand, addButton.Command);
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
