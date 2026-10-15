using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Theme;

namespace Peerfluence.HeadlessTests;

public sealed class ControlStateTests
{
    [AvaloniaTheory]
    [InlineData("standard", false)]
    [InlineData("accent", false)]
    [InlineData("ghost", false)]
    [InlineData("danger", false)]
    [InlineData("toolbar", false)]
    [InlineData("toolbar", true)]
    [InlineData("chip", false)]
    [InlineData("chip", true)]
    [InlineData("nav", false)]
    [InlineData("nav", true)]
    public void Actions_KeepTheirPaletteAndFocusCueAcrossAllStates(string kind, bool selected)
    {
        Button button = kind switch
        {
            "toolbar" => new ToggleButton { IsChecked = selected },
            "chip" => new RadioButton { IsChecked = selected },
            _ => new Button(),
        };
        var label = new TextBlock { Text = "Action" };
        button.Content = label;
        if (kind != "standard") button.Classes.Add(kind);
        if (kind == "nav" && selected) button.Classes.Add("selected");
        var app = Application.Current!;
        var previousVariant = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        var focusAnchor = new Button { Content = "Next action" };
        var window = new Window { Content = new StackPanel { Children = { button, focusAnchor } }, Width = 400, Height = 200 };
        window.Show();
        try
        {
            foreach (string accent in PeerfluenceTheme.Accents)
            {
                theme.UseAccent(accent);
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    app.RequestedThemeVariant = variant;
                    Layout(window);
                    var point = button.TranslatePoint(new Point(10, 10), window)!.Value;
                    var outside = new Point(390, 190);
                    window.MouseMove(outside);
                    AssertState("normal");
                    window.MouseMove(point);
                    AssertState("hover");
                    window.MouseDown(point, MouseButton.Left);
                    AssertState("pressed");
                    window.MouseMove(outside);
                    window.MouseUp(outside, MouseButton.Left);

                    focusAnchor.Focus();
                    window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                    window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                    Assert.True(button.IsFocused);
                    Layout(window);
                    Assert.Contains(":focus-visible", button.Classes);
                    var adorners = AdornerLayer.GetAdornerLayer(button);
                    Assert.NotNull(adorners);
                    Assert.NotEmpty(adorners.GetVisualChildren());

                    button.IsEnabled = false;
                    window.MouseMove(point);
                    window.MouseDown(point, MouseButton.Left);
                    AssertState("disabled");
                    window.MouseUp(point, MouseButton.Left);
                    if (button is ToggleButton toggle) Assert.Equal(selected, toggle.IsChecked);
                    button.IsEnabled = true;
                }
            }
        }
        finally
        {
            window.Close();
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previousVariant;
        }

        void AssertState(string state)
        {
            Layout(window);
            string? background;
            string foreground;
            if (state == "disabled")
            {
                background = kind is "ghost" or "nav" || (kind == "toolbar" && !selected) ? null : "PfSurfaceAlt";
                foreground = "PfTextTertiary";
            }
            else if (kind == "accent" || (kind == "chip" && selected))
            {
                background = state == "pressed" ? "PfAccentPressed" : state == "hover" ? "PfAccentHover" : "PfAccent";
                foreground = "PfTextOnAccent";
            }
            else if (kind is "toolbar" or "nav" && selected)
            {
                background = kind == "toolbar" && state != "normal" ? "PfAccentMuted" : "PfAccentSubtle";
                foreground = "PfAccent";
            }
            else
            {
                background = state == "pressed" ? "PfSurfaceActive" : state == "hover" ? "PfSurfaceHover" : kind is "ghost" or "toolbar" or "nav" ? null : "PfSurface";
                foreground = kind == "danger" ? "PfDanger" : state == "normal" && kind is "toolbar" or "chip" or "nav" ? "PfTextSecondary" : "PfText";
            }

            var fill = kind == "chip"
                ? button.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "PART_Chip").Background
                : button.GetVisualDescendants().OfType<ContentPresenter>().First().Background;
            Assert.Equal(background is null ? Colors.Transparent : Colour(window, background), Assert.IsAssignableFrom<ISolidColorBrush>(fill).Color);
            Assert.Equal(Colour(window, foreground), Assert.IsType<SolidColorBrush>(label.Foreground).Color);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckedRadioAndSwitch_FollowTheAccentAndUseAContrastingGlyph(bool useSwitch)
    {
        ToggleButton control = useSwitch ? new ToggleSwitch { IsChecked = true } : new RadioButton { IsChecked = true };
        var app = Application.Current!;
        var previousVariant = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        var window = new Window { Content = new StackPanel { Children = { control } }, Width = 400, Height = 200 };
        window.Show();
        try
        {
            foreach (string accent in PeerfluenceTheme.Accents)
            {
                theme.UseAccent(accent);
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    app.RequestedThemeVariant = variant;
                    Layout(window);
                    var point = control.TranslatePoint(new Point(10, 10), window)!.Value;
                    window.MouseMove(new Point(390, 190));
                    Check("PfAccent");
                    window.MouseMove(point);
                    Check("PfAccentHover");
                    window.MouseDown(point, MouseButton.Left);
                    Check("PfAccentPressed");
                    window.MouseMove(new Point(390, 190));
                    window.MouseUp(new Point(390, 190), MouseButton.Left);
                    control.IsEnabled = false;
                    Layout(window);
                    Assert.NotEqual(Colour(window, "PfAccent"), Fill());
                    control.IsEnabled = true;
                }
            }
        }
        finally
        {
            window.Close();
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previousVariant;
        }

        Color Fill() => Assert.IsType<SolidColorBrush>(useSwitch
            ? control.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "SwitchKnobBounds").Background
            : control.GetVisualDescendants().OfType<Ellipse>().Single(part => part.Name == "CheckOuterEllipse").Fill).Color;
        void Check(string key)
        {
            Layout(window);
            Assert.Equal(Colour(window, key), Fill());
            var glyph = control.GetVisualDescendants().OfType<Ellipse>().Single(part => part.Name == (useSwitch ? "SwitchKnobOn" : "CheckGlyph"));
            Assert.Equal(Colour(window, "PfTextOnAccent"), Assert.IsType<SolidColorBrush>(glyph.Fill).Color);
        }
    }

    [AvaloniaFact]
    public void OpenDropdown_KeepsItsSelectedPaletteThroughPointerAndDisabledStates()
    {
        var selected = new ComboBoxItem { Content = "Selected" };
        var combo = new ComboBox { Items = { selected, new ComboBoxItem { Content = "Other" } }, SelectedIndex = 0, Width = 240 };
        var app = Application.Current!;
        var previousVariant = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        var window = new Window { Content = new StackPanel { Children = { combo } }, Width = 400, Height = 300 };
        window.Show();
        try
        {
            foreach (string accent in PeerfluenceTheme.Accents)
            {
                theme.UseAccent(accent);
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    app.RequestedThemeVariant = variant;
                    selected.IsEnabled = true;
                    combo.IsDropDownOpen = true;
                    Layout(window);
                    var popup = TopLevel.GetTopLevel(selected)!;
                    popup.MouseMove(new Point(-100, -100));
                    Layout(window);
                    var presenter = selected.GetVisualDescendants().OfType<ContentPresenter>().First();
                    Assert.Equal(Colour(window, "PfAccentSubtle"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                    var point = selected.TranslatePoint(new Point(10, selected.Bounds.Height / 2), popup)!.Value;
                    popup.MouseMove(point);
                    Layout(window);
                    Assert.Equal(Colour(window, "PfAccentMuted"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                    popup.MouseDown(point, MouseButton.Left);
                    Layout(window);
                    Assert.Equal(Colour(window, "PfAccentMuted"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                    selected.IsEnabled = false;
                    popup.MouseUp(point, MouseButton.Left);
                    Layout(window);
                    Assert.Equal(Colour(window, "PfSurfaceAlt"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                    Assert.Equal(Colour(window, "PfTextTertiary"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
                    combo.IsDropDownOpen = false;
                }
            }
        }
        finally
        {
            window.Close();
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previousVariant;
        }
    }

    private static Color Colour(Window window, string key) => Assert.IsType<SolidColorBrush>(window.FindResource(key)).Color;
    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
