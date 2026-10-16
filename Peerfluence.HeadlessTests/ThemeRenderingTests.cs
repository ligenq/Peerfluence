using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Theme;

namespace Peerfluence.HeadlessTests;

public sealed class ThemeRenderingTests
{
    private sealed record TorrentRow(string Name);

    [AvaloniaFact]
    public void NarrowTextCells_ShowEllipsisAndKeepTheFullValueWhenResizedOrRebound()
    {
        const string fullValue = "A long torrent filename that must remain complete.mkv";
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
            ItemsSource = new[] { new TorrentRow(fullValue) },
        };
        var column = new DataGridTextColumn
        {
            Binding = new Binding(nameof(TorrentRow.Name)),
            Width = new DataGridLength(100),
        };
        grid.Columns.Add(column);
        var window = new Window { Content = grid, Width = 800, Height = 250 };
        window.Show();
        try
        {
            Layout(window);
            var text = grid.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CellTextBlock");
            Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
            Assert.Equal(fullValue, text.Text);
            Assert.Equal(fullValue, ToolTip.GetTip(text));
            object? copiedValue = null;
            grid.CopyingRowClipboardContent += (_, args) => copiedValue = Assert.Single(args.ClipboardRowContent).Content;
            ClickCell(window, VisibleCells(grid).First());
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
            Layout(window);
            Assert.Equal(fullValue, copiedValue);

            column.Width = new DataGridLength(700);
            Layout(window);
            Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
            Assert.Equal(fullValue, text.Text);
            // The tooltip goes away with the ellipsis. Left attached it would pop up to repeat
            // text the cell is now showing in full.
            Assert.Null(ToolTip.GetTip(text));

            column.Width = new DataGridLength(100);
            Layout(window);
            Assert.Equal(fullValue, ToolTip.GetTip(text));

            grid.ItemsSource = new[] { new TorrentRow("Replacement value") };
            Layout(window);
            text = grid.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CellTextBlock");
            Assert.Equal("Replacement value", ToolTip.GetTip(text));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// A cell with room for its text carries no tooltip at all.
    /// </summary>
    /// <remarks>
    /// The theme trims cell text, and the obvious way to make the hidden part reachable is a
    /// ToolTip.Tip setter in the same style. That attaches one to every cell, so the short ones -
    /// a percentage, a speed, a peer count - pop up to repeat what is already on screen. The
    /// tooltip has to follow the ellipsis, and the only way to prove it does is to check a cell
    /// that never gets one.
    /// </remarks>
    [AvaloniaFact]
    public void AWideTextCell_CarriesNoTooltipAtAll()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = new[] { new TorrentRow("12.3%") },
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Binding = new Binding(nameof(TorrentRow.Name)),
            Width = new DataGridLength(400),
        });
        var window = new Window { Content = grid, Width = 800, Height = 250 };
        window.Show();
        try
        {
            Layout(window);
            var text = grid.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CellTextBlock");
            Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
            Assert.Equal("12.3%", text.Text);
            Assert.Null(ToolTip.GetTip(text));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(DataGridSelectionMode.Single)]
    [InlineData(DataGridSelectionMode.Extended)]
    public void ClickingAndNavigatingCells_SelectsWholeRowsWithoutCellOutlines(DataGridSelectionMode selectionMode)
    {
        var items = new[] { new TorrentRow("First"), new TorrentRow("Second"), new TorrentRow("Third") };
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = selectionMode,
            ItemsSource = items,
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(TorrentRow.Name)) { Mode = BindingMode.OneWay },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Details",
            CellTemplate = new FuncDataTemplate<TorrentRow>((item, _) => new TextBlock { Text = item!.Name }),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        var window = new Window { Content = grid, Width = 600, Height = 400 };
        window.Show();

        try
        {
            Layout(window);
            var rows = grid.GetVisualDescendants().OfType<DataGridRow>().OrderBy(row => row.Bounds.Y).ToArray();
            Assert.Equal(3, rows.Length);
            var firstRowCells = VisibleCells(rows[0]).ToArray();
            Assert.Equal(2, firstRowCells.Length);
            foreach (var cell in firstRowCells)
            {
                ClickCell(window, cell);
                Assert.True(grid.IsFocused);
                Assert.Contains(":focus", cell.Classes);
                Assert.Same(items[0], grid.SelectedItem);
                Assert.Single(grid.SelectedItems);
                AssertRowHighlight(window, rows[0]);
                AssertNoCellOutlines(grid);
            }

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Layout(window);
            Assert.Same(items[1], grid.SelectedItem);
            Assert.Single(grid.SelectedItems);
            AssertRowHighlight(window, rows[1]);
            AssertNoCellOutlines(grid);

            if (selectionMode == DataGridSelectionMode.Single)
            {
                ClickCell(window, firstRowCells[0], RawInputModifiers.Control);
                Assert.Single(grid.SelectedItems);
                Assert.Same(items[0], grid.SelectedItem);
                ClickCell(window, VisibleCells(rows[2]).Last(), RawInputModifiers.Shift);
                Assert.Single(grid.SelectedItems);
                Assert.Same(items[2], grid.SelectedItem);
                AssertRowHighlight(window, rows[2]);
                AssertNoCellOutlines(grid);
                return;
            }

            ClickCell(window, firstRowCells[0], RawInputModifiers.Control);
            Assert.Equal(2, grid.SelectedItems.Count);
            Assert.Contains(items[0], grid.SelectedItems.Cast<TorrentRow>());
            Assert.Contains(items[1], grid.SelectedItems.Cast<TorrentRow>());
            AssertRowHighlight(window, rows[0]);
            AssertRowHighlight(window, rows[1]);
            AssertNoCellOutlines(grid);

            ClickCell(window, firstRowCells[0]);
            ClickCell(window, VisibleCells(rows[2]).Last(), RawInputModifiers.Shift);
            Assert.Equal(items, grid.SelectedItems.Cast<TorrentRow>().OrderBy(item => Array.IndexOf(items, item)));
            Assert.All(rows, row => AssertRowHighlight(window, row));
            AssertNoCellOutlines(grid);
        }
        finally
        {
            window.Close();
        }
    }

    private static void ClickCell(Window window, DataGridCell cell, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left, modifiers);
        window.MouseUp(point.Value, MouseButton.Left, modifiers);
        Layout(window);
    }

    private static void AssertNoCellOutlines(DataGrid grid)
    {
        foreach (var cell in VisibleCells(grid))
        {
            Assert.False(cell.GetVisualDescendants().OfType<Grid>().Single(part => part.Name == "FocusVisual").IsVisible);
            Assert.False(cell.GetVisualDescendants().OfType<Rectangle>().Single(part => part.Name == "CurrencyVisual").IsVisible);
        }
    }

    private static void AssertRowHighlight(Window window, DataGridRow row)
    {
        var background = row.GetVisualDescendants().OfType<Rectangle>().First(part => part.Name == "BackgroundRectangle");
        Assert.Equal(Colour(window, "PfAccentSubtle"), Assert.IsType<SolidColorBrush>(background.Fill).Color);
        Assert.True(background.Bounds.Width >= VisibleCells(row).Sum(cell => cell.Bounds.Width));
    }

    private static IEnumerable<DataGridCell> VisibleCells(Visual source) =>
        source.GetVisualDescendants().OfType<DataGridCell>()
            .Where(cell => cell.IsEffectivelyVisible && cell.Bounds.Width > 0);

    [AvaloniaFact]
    public void APopulatedGrid_RendersItsHeaderAndTorrentRow()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = new[] { new TorrentRow("Test torrent") },
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(TorrentRow.Name)) { Mode = BindingMode.OneWay },
        });
        var window = new Window { Content = grid, Width = 600, Height = 400 };
        window.Show();

        try
        {
            Layout(window);
            Assert.NotNull(grid.Template);
            Assert.Contains(grid.GetVisualDescendants().OfType<DataGridColumnHeader>(),
                header => Equals(header.Content, "Name"));
            var row = Assert.Single(grid.GetVisualDescendants().OfType<DataGridRow>());
            Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Test torrent");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ChangingAccentAndVariant_RepaintsExistingFluentControls()
    {
        var application = Application.Current!;
        var previousVariant = application.RequestedThemeVariant;
        var theme = application.Styles.OfType<PeerfluenceTheme>().Single();
        var field = new TextBox { Text = "Focused field" };
        var toggle = new ToggleSwitch { IsChecked = true };
        var window = new Window
        {
            Content = new StackPanel { Children = { field, toggle } },
            Width = 600,
            Height = 400,
        };
        window.Show();

        try
        {
            Layout(window);
            field.Focus();
            var fill = toggle.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "SwitchKnobBounds");

            foreach (string accent in PeerfluenceTheme.Accents)
            {
                Assert.True(theme.UseAccent(accent));
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    application.RequestedThemeVariant = variant;
                    Layout(window);
                    Assert.True(theme.Resources.TryGetResource($"{accent}AccentColor", variant,
                        out object? expected));
                    var colour = Assert.IsType<Color>(expected);
                    Assert.Equal(colour, Assert.IsType<SolidColorBrush>(fill.Background).Color);
                    Assert.Contains(field.GetVisualDescendants().OfType<Border>(),
                        border => border.BorderBrush is SolidColorBrush brush && brush.Color == colour);
                    Assert.True(window.TryFindResource("SystemAccentColor", variant, out object? systemColour));
                    Assert.Equal(colour, Assert.IsType<Color>(systemColour));
                }
            }
        }
        finally
        {
            window.Close();
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            application.RequestedThemeVariant = previousVariant;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Labels_InheritTheForegroundOfTheirActionOrSelection(bool dark)
    {
        var application = Application.Current!;
        var previousVariant = application.RequestedThemeVariant;
        application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var buttonLabel = new TextBlock { Text = "Add torrent" };
        var button = new Button { Content = new StackPanel { Children = { buttonLabel } } };
        button.Classes.Add("accent");
        var chipLabel = new TextBlock { Text = "Simple" };
        var chip = new RadioButton { Content = chipLabel, IsChecked = true };
        chip.Classes.Add("chip");
        var navigationLabel = new TextBlock { Text = "Downloads" };
        var navigation = new ListBox { ItemsSource = new[] { navigationLabel }, SelectedIndex = 0 };
        navigation.Classes.Add("nav");
        var ordinaryLabel = new TextBlock { Text = "Ordinary text" };
        var window = new Window
        {
            Content = new StackPanel { Children = { button, chip, navigation, ordinaryLabel } },
            Width = 600,
            Height = 400,
        };
        window.Show();

        try
        {
            Layout(window);
            Assert.Equal(Colour(window, "PfTextOnAccent"), Assert.IsType<SolidColorBrush>(buttonLabel.Foreground).Color);
            Assert.Equal(Colour(window, "PfTextOnAccent"), Assert.IsType<SolidColorBrush>(chipLabel.Foreground).Color);
            Assert.Equal(Colour(window, "PfAccent"), Assert.IsType<SolidColorBrush>(navigationLabel.Foreground).Color);
            Assert.Equal(Colour(window, "PfText"), Assert.IsType<SolidColorBrush>(ordinaryLabel.Foreground).Color);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = previousVariant;
        }
    }

    [AvaloniaFact]
    public void CheckBoxes_KeepTheirAccentAndGlyphAcrossPointerStatesAndThemeChanges()
    {
        var application = Application.Current!;
        var previousVariant = application.RequestedThemeVariant;
        var theme = application.Styles.OfType<PeerfluenceTheme>().Single();
        var check = new CheckBox { Content = "Check", IsThreeState = true, IsChecked = true };
        var window = new Window { Content = new StackPanel { Children = { check } }, Width = 400, Height = 200 };
        window.Show();
        try
        {
            Layout(window);
            var fill = check.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "NormalRectangle");
            foreach (string accent in PeerfluenceTheme.Accents)
            {
                theme.UseAccent(accent);
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    application.RequestedThemeVariant = variant;
                    foreach (bool? value in new bool?[] { true, null })
                    {
                        window.MouseMove(new Point(390, 190));
                        check.IsChecked = value;
                        Layout(window);
                        var glyph = check.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                            .Single(part => part.Name == "CheckGlyph");
                        Assert.Equal(Colour(window, "PfAccent"), Assert.IsType<SolidColorBrush>(fill.Background).Color);
                        Assert.Equal(Colour(window, "PfTextOnAccent"), Assert.IsType<SolidColorBrush>(glyph.Fill).Color);
                        var point = check.TranslatePoint(new Point(10, 10), window)!.Value;
                        window.MouseMove(point);
                        Layout(window);
                        Assert.Equal(Colour(window, "PfAccentHover"), Assert.IsType<SolidColorBrush>(fill.Background).Color);
                        Assert.Equal(Colour(window, "PfTextOnAccent"), Assert.IsType<SolidColorBrush>(glyph.Fill).Color);
                        window.MouseDown(point, MouseButton.Left);
                        Layout(window);
                        Assert.Equal(Colour(window, "PfAccentPressed"), Assert.IsType<SolidColorBrush>(fill.Background).Color);
                        Assert.Equal(Colour(window, "PfTextOnAccent"), Assert.IsType<SolidColorBrush>(glyph.Fill).Color);
                        window.MouseUp(point, MouseButton.Left);
                    }
                }
            }
        }
        finally
        {
            window.Close();
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            application.RequestedThemeVariant = previousVariant;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledPrimaryAction_IsVisiblyDisabledAndDoesNotActivate(bool dark)
    {
        var button = new Button { Content = new TextBlock { Text = "Create" }, Classes = { "accent" } };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        var window = new Window
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new StackPanel { Children = { button } },
            Width = 400,
            Height = 200,
        };
        window.Show();
        try
        {
            Layout(window);
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Equal(Colour(window, "PfAccent"), Assert.IsType<SolidColorBrush>(presenter.Background).Color);
            button.IsEnabled = false;
            Layout(window);
            var point = button.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Layout(window);
            Assert.Equal(0, clicks);
            Assert.Equal(Colour(window, "PfSurfaceAlt"), Assert.IsType<SolidColorBrush>(presenter.Background).Color);
            Assert.Equal(Colour(window, "PfTextTertiary"), Assert.IsType<SolidColorBrush>(((TextBlock)button.Content!).Foreground).Color);
            button.IsEnabled = true;
            window.MouseMove(new Point(390, 190));
            Layout(window);
            Assert.Equal(Colour(window, "PfAccent"), Assert.IsType<SolidColorBrush>(presenter.Background).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NumericField_MatchesTextFieldHeightAndStillEditsAndSpins()
    {
        var number = new NumericUpDown { Value = 10, Minimum = 0, Maximum = 100 };
        var text = new TextBox { Text = "Field" };
        var window = new Window { Content = new StackPanel { Children = { number, text } }, Width = 400, Height = 200 };
        window.Show();
        try
        {
            Layout(window);
            Assert.Equal(text.Bounds.Height, number.Bounds.Height);
            var input = number.GetVisualDescendants().OfType<TextBox>().Single();
            input.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.Equal(11, number.Value);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal(10, number.Value);
            input.SelectAll();
            window.KeyTextInput("42");
            text.Focus();
            Layout(window);
            Assert.Equal(42, number.Value);
        }
        finally { window.Close(); }
    }

    private static Color Colour(Window window, string key) =>
        Assert.IsType<SolidColorBrush>(window.FindResource(window.ActualThemeVariant, key)).Color;

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
