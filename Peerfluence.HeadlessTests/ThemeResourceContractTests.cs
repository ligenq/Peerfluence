using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Theme;

namespace Peerfluence.HeadlessTests;

public sealed class ThemeResourceContractTests
{
    [AvaloniaFact]
    public void ScrollbarResources_ResolveWithTheirRequiredTypesInEveryVariant()
    {
        string[] colors = ["ScrollBarThumbBackgroundColor", "ScrollBarPanningThumbBackgroundColor"];
        string[] brushes =
        [
            "ScrollBarBackground", "ScrollBarBackgroundPointerOver", "ScrollBarBorderBrush",
            "ScrollBarTrackFill", "ScrollBarTrackFillPointerOver", "ScrollBarTrackStroke", "ScrollBarTrackStrokePointerOver",
            "ScrollBarPanningThumbBackground", "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed", "ScrollBarThumbFillDisabled",
            "ScrollBarButtonBackground", "ScrollBarButtonBackgroundPointerOver", "ScrollBarButtonBackgroundPressed", "ScrollBarButtonBackgroundDisabled",
            "ScrollBarButtonBorderBrush", "ScrollBarButtonBorderBrushPointerOver", "ScrollBarButtonBorderBrushPressed", "ScrollBarButtonBorderBrushDisabled",
            "ScrollBarButtonArrowForeground", "ScrollBarButtonArrowForegroundPointerOver", "ScrollBarButtonArrowForegroundPressed", "ScrollBarButtonArrowForegroundDisabled",
        ];
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        var window = new Window();
        try
        {
            window.Show();
            foreach (var variant in PeerfluenceTheme.Variants)
            {
                app.RequestedThemeVariant = variant;
                theme.UseVariant(variant);
                foreach (string key in colors)
                {
                    Assert.True(window.FindResource(variant, key) is Color,
                        $"{variant}: '{key}' must resolve as a Color, not a brush or an unset value.");
                }

                foreach (string key in brushes)
                {
                    Assert.True(window.FindResource(variant, key) is IBrush,
                        $"{variant}: '{key}' must resolve as a brush, not a Color or an unset value.");
                }
            }
        }
        finally
        {
            window.Close();
            theme.UseVariant(previous);
            app.RequestedThemeVariant = previous;
        }
    }
}
