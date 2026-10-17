using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.Theme;
using Peerfluence.ViewModels;
using Peerfluence.Views;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The add-torrent dialog, loaded for real.
/// </summary>
/// <remarks>
/// Constructing the window parses its XAML and resolves every <c>StaticResource</c> in it. Nothing
/// did that before, which is how six references to a spacing key that was never defined survived in
/// here - the compiler does not resolve resource keys, so the first thing that notices is whoever
/// opens the dialog.
/// </remarks>
public class AddTorrentOptionsWindowTests
{
    [AvaloniaTheory]
    [InlineData("Midnight", 760)]
    [InlineData("Midnight", 860)]
    [InlineData("MidnightLight", 760)]
    [InlineData("MidnightLight", 860)]
    public void OuterScrollbarLeavesEveryCardFullyVisible(string variant, int width)
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        using var service = new ThemeService();
        service.Apply(new ThemeSettings { ThemeVariant = variant });
        var window = new AddTorrentOptionsWindow { DataContext = CreateLoadedMagnet(), Width = width, Height = 640 };
        try
        {
            window.Show();
            CheckCards();
            window.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
            CheckCards();
        }
        finally
        {
            window.Close();
            theme.UseVariant(previous);
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previous;
        }

        void CheckCards()
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var outer = window.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(viewer => viewer.Content is StackPanel stack && stack.Children.OfType<Expander>().Any());
            Assert.True(outer.Extent.Height > outer.Viewport.Height, "The outer scrollbar must be needed.");
            var bar = outer.GetVisualDescendants().OfType<ScrollBar>()
                .Single(scroll => scroll.Name == "PART_VerticalScrollBar" && ReferenceEquals(scroll.TemplatedParent, outer));
            Assert.True(bar.IsVisible);
            double barLeft = bar.TranslatePoint(default, outer)!.Value.X;
            var cards = Assert.IsType<StackPanel>(outer.Content).GetVisualDescendants().OfType<Border>()
                .Where(card => card.IsEffectivelyVisible && card.Classes.Contains("panel")).ToArray();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
            {
                double cardRight = card.TranslatePoint(new Point(card.Bounds.Width, 0), outer)!.Value.X;
                Assert.True(barLeft - cardRight >= 8 - 0.5,
                    $"A card reaches {cardRight:F1}px, but the scrollbar starts at {barLeft:F1}px; "
                    + "the full right edge and an 8px gap must remain visible.");
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("Midnight", 760)]
    [InlineData("Midnight", 860)]
    [InlineData("MidnightLight", 760)]
    [InlineData("MidnightLight", 860)]
    public void LoadedMetadataKeepsAdvancedControlsInsideTheirBounds(string variant, int width)
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        using var service = new ThemeService();
        service.Apply(new ThemeSettings { ThemeVariant = variant });
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings { Storage = { DownloadPath = "C:\\Downloads" } });
        var model = AddTorrentOptionsViewModel.CreateForMagnet(
            "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
            Substitute.For<ITorrentService>(), Substitute.For<ITopLevelService>(), settings,
            Substitute.For<ITorrentCategoryService>());
        model.ApplyMetadataPreview(new MagnetMetadataPreview(
            "Example torrent", "0123456789abcdef0123456789abcdef01234567", "v1", 1024, 1, 1, 16384,
            false, [new MagnetMetadataPreviewFile(0, "example.txt", 1024)],
            Enumerable.Range(0, 15).Select(index => $"https://tracker{index}.example.com/announce").ToArray()));
        var window = new AddTorrentOptionsWindow { DataContext = model, Width = width };
        try
        {
            window.Show();
            var expander = window.GetVisualDescendants().OfType<Expander>().Single();
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var header = expander.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "ExpanderHeader");
            var chevron = header.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "ExpandCollapseChevronBorder");
            double chevronRight = chevron.TranslatePoint(new Point(chevron.Bounds.Width, 0), header)!.Value.X;
            Assert.True(header.Bounds.Width - chevronRight >= 12,
                $"The expander chevron has only {header.Bounds.Width - chevronRight:F1}px to the right edge.");

            var trackers = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetAutomationId(box) == "ExistingTrackersTextBox");
            var viewer = trackers.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(viewer.Extent.Height > viewer.Viewport.Height, "Trackers must exercise the overflow path.");
            var bar = viewer.GetVisualDescendants().OfType<ScrollBar>()
                .Single(scroll => scroll.Name == "PART_VerticalScrollBar");
            Assert.True(bar.IsVisible);
            Assert.False(bar.AllowAutoHide);
            double right = bar.TranslatePoint(new Point(bar.Bounds.Width, 0), trackers)!.Value.X;
            Assert.InRange(right, 0, trackers.Bounds.Width - 1);
            double trackersRight = trackers.TranslatePoint(new Point(trackers.Bounds.Width, 0), expander)!.Value.X;
            Assert.InRange(trackersRight, 0, expander.Bounds.Width - expander.Padding.Right);
            var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.Equal(Assert.IsType<Color>(window.FindResource(window.ActualThemeVariant, "PfTextTertiaryColor")),
                Assert.IsAssignableFrom<ISolidColorBrush>(thumb.Background).Color);

            foreach (string key in new[] { "ScrollBarPanningThumbBackgroundColor", "ScrollBarThumbBackgroundColor" })
            {
                Assert.Equal(Assert.IsType<Color>(window.FindResource(window.ActualThemeVariant, "PfTextTertiaryColor")),
                    Assert.IsType<Color>(window.FindResource(window.ActualThemeVariant, key)));
            }
        }
        finally
        {
            window.Close();
            theme.UseVariant(previous);
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaFact]
    public void TheWindow_LoadsWithEveryResourceItReferences()
    {
        var window = new AddTorrentOptionsWindow();

        Assert.NotNull(window);
    }

    private static AddTorrentOptionsViewModel CreateLoadedMagnet()
    {
        const string name = "A sample torrent with a long descriptive release name [2026] [Extended edition] [1080p]";
        const string hash = "0123456789abcdef0123456789abcdef01234567";
        var trackers = Enumerable.Range(0, 15).Select(index => $"https://tracker{index}.example.com/announce").ToArray();
        string magnet = $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(name)}&"
            + string.Join('&', trackers.Select(tracker => $"tr={Uri.EscapeDataString(tracker)}"));
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings { Storage = { DownloadPath = "C:\\Downloads" } });
        var model = AddTorrentOptionsViewModel.CreateForMagnet(magnet,
            Substitute.For<ITorrentService>(), Substitute.For<ITopLevelService>(), settings,
            Substitute.For<ITorrentCategoryService>());
        model.ApplyMetadataPreview(new MagnetMetadataPreview(name, hash, "v1", 1024, 1, 1, 16384, false,
            [new MagnetMetadataPreviewFile(0, $"{name}.mkv", 1024)], trackers));
        return model;
    }
}
