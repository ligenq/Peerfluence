using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.Core.Config;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.Theme;
using Peerfluence.Views;

namespace Peerfluence.HeadlessTests;

public class CreateTorrentWindowTests
{
    [AvaloniaTheory]
    [InlineData("Midnight")]
    [InlineData("MidnightLight")]
    public void WindowFitsItsContentAndGrowsWhenAnErrorAppears(string variant)
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        using var service = new ThemeService();
        service.Apply(new ThemeSettings { ThemeVariant = variant });
        var model = TestHelpers.CreateCreateTorrentViewModel();
        var window = new CreateTorrentWindow { DataContext = model };
        window.Show();
        try
        {
            Layout(window);
            var scroll = window.FindControl<ScrollViewer>("FormScrollViewer")!;
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1);
            AssertActionsFit(window);

            double initialHeight = window.Bounds.Height;

            model.ErrorMessage = "An error that needs room below the form.";
            Layout(window);
            Assert.True(window.Bounds.Height > initialHeight,
                $"The window stayed at {initialHeight}px instead of making room for the error.");
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1);
            AssertActionsFit(window);

            model.ErrorMessage = string.Empty;
            Layout(window);
            Assert.InRange(window.Bounds.Height, initialHeight - 1, initialHeight + 1);
        }
        finally
        {
            window.Close();
            theme.UseVariant(previous);
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(500)]
    [InlineData(650)]
    public void LimitedHeightScrollsTheFormWhileKeepingActionsVisible(int availableHeight)
    {
        var model = TestHelpers.CreateCreateTorrentViewModel();
        var window = new CreateTorrentWindow { DataContext = model, MaxHeight = availableHeight };
        window.Show();
        try
        {
            Layout(window);
            Assert.True(window.Bounds.Height <= availableHeight);
            var scroll = window.FindControl<ScrollViewer>("FormScrollViewer")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.False(scroll.AllowAutoHide);
            var bar = scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                .Single(part => part.Name == "PART_VerticalScrollBar" && ReferenceEquals(part.TemplatedParent, scroll));
            double barLeft = bar.TranslatePoint(default, scroll)!.Value.X;
            var cards = Assert.IsType<StackPanel>(scroll.Content).Children.OfType<Border>()
                .Where(card => card.Classes.Contains("panel")).ToArray();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
            {
                double right = card.TranslatePoint(new Point(card.Bounds.Width, 0), scroll)!.Value.X;
                Assert.True(barLeft - right >= 8 - 0.5, "The form must leave its cards clear of the scrollbar.");
            }
            AssertActionsFit(window);

            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Layout(window);
            var comment = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetAutomationId(box) == "CommentTextBox");
            var commentBottom = comment.TranslatePoint(new Point(0, comment.Bounds.Height), scroll)!.Value.Y;
            Assert.InRange(commentBottom, 0, scroll.Viewport.Height + 1);
            AssertActionsFit(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void AssertActionsFit(Window window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<Button>()
                     .Where(button => button.IsDefault || button.IsCancel))
        {
            var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value.Y;
            Assert.True(button.Bounds.Height > 0);
            Assert.InRange(bottom, 0, window.ClientSize.Height);
        }
    }

    [AvaloniaFact]
    public void View_CanBeCreated()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        var view = new CreateTorrentWindow { DataContext = vm };
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public void PieceSizes_Has7Options()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.Equal(7, vm.PieceSizes.Count);
    }

    [AvaloniaFact]
    public void ErrorMessage_HiddenByDefault()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [AvaloniaFact]
    public void ErrorMessage_HasError_WhenSet()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        vm.ErrorMessage = "Something went wrong";
        Assert.True(vm.HasError);
    }

    [AvaloniaFact]
    public void IsCreating_FalseByDefault()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.False(vm.IsCreating);
    }

    [AvaloniaFact]
    public void CreateCommand_CannotExecute_WhenSourceEmpty()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.Equal(string.Empty, vm.SourcePath);
        Assert.False(vm.CreateCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CreateCommand_CanExecute_WhenSourceSet()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        vm.SourcePath = @"C:\some\path";
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CreateCommand_CannotExecute_WhenCreating()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        vm.SourcePath = @"C:\some\path";
        vm.IsCreating = true;
        Assert.False(vm.CreateCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void DefaultPieceSizeIndex_Is2()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.Equal(2, vm.SelectedPieceSizeIndex);
    }

    [AvaloniaFact]
    public void IsPrivate_FalseByDefault()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.False(vm.IsPrivate);
    }

    [AvaloniaFact]
    public void SourcePath_TextBox_IsReadOnly()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        var view = new CreateTorrentWindow { DataContext = vm };

        // Apply template to instantiate controls
        view.ApplyTemplate();

        var textBoxes = view.GetLogicalDescendants().OfType<TextBox>().Where(tb => tb.IsReadOnly).ToList();
        Assert.NotEmpty(textBoxes);
    }

    [AvaloniaFact]
    public void MultilineTextBoxes_Exist()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        var view = new CreateTorrentWindow { DataContext = vm };
        view.ApplyTemplate();

        var multiline = view.GetLogicalDescendants().OfType<TextBox>().Where(tb => tb.AcceptsReturn).ToList();
        // Trackers and WebSeeds
        Assert.True(multiline.Count >= 2, $"Expected at least 2 multiline TextBoxes, found {multiline.Count}");
    }

    [AvaloniaFact]
    public void CreateButton_TracksCommandCanExecute()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        var view = new CreateTorrentWindow { DataContext = vm };
        view.ApplyTemplate();

        var createButton = view.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => ReferenceEquals(button.Command, vm.CreateCommand));

        Assert.False(createButton.IsEffectivelyEnabled);

        vm.SourcePath = @"C:\source";
        Assert.True(createButton.IsEffectivelyEnabled);

        vm.IsCreating = true;
        Assert.False(createButton.IsEffectivelyEnabled);
    }
}
