using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Headless;
using Peerfluence.HeadlessTests.XUnit;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// Whether a size column ever asks its converter to go backwards.
/// </summary>
public class ByteSizeConverterBindingTests
{
    private sealed class Row
    {
        public long TotalSizeBytes { get; set; } = 1_500_000_000;
    }

    /// <summary>Counts the calls the real converter would have thrown on.</summary>
    private sealed class Spy : IValueConverter
    {
        public int BackwardCalls { get; private set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Peerfluence.Converters.ByteSizeConverter.Instance.Convert(value, targetType, parameter, culture);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            BackwardCalls++;
            return null;
        }
    }

    private static int BackwardCallsFor(BindingMode mode)
    {
        var spy = new Spy();
        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            ItemsSource = new List<Row> { new() },
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Binding = new Binding(nameof(Row.TotalSizeBytes)) { Converter = spy, Mode = mode },
        });

        var window = new Window { Content = grid, Width = 600, Height = 400 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return spy.BackwardCalls;
    }

    [AvaloniaFact]
    public void ADefaultColumnBinding_AsksTheConverterToGoBackwards()
    {
        // The reproduction. DataGridTextColumn binds two ways by default, and the grid asks for the
        // way back while merely showing a row - on a grid that is IsReadOnly, with nobody editing
        // anything. A converter that throws there throws while a download is on screen.
        Assert.NotEqual(0, BackwardCallsFor(BindingMode.Default));
    }

    [AvaloniaFact]
    public void AOneWayColumnBinding_DoesNot()
    {
        Assert.Equal(0, BackwardCallsFor(BindingMode.OneWay));
    }
}
