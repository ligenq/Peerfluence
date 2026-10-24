using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
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

    /// <summary>
    /// What the size columns actually rely on: the converter is never asked to go backwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be a pair, because the other half reproduced the reason: under the previous
    /// control theme a <see cref="DataGridTextColumn"/> with a default-mode binding asked for the
    /// way back while merely showing a row, on a grid that is IsReadOnly with nobody editing
    /// anything, and a converter that throws there threw while a download was on screen.
    /// </para>
    /// <para>
    /// That no longer reproduces: with Avalonia's Fluent grid template the default-mode binding
    /// does not round-trip. The reproduction was removed rather than inverted, because asserting
    /// that a quirk is absent holds nothing to account - it would pass for any reason at all,
    /// including the row never being realized. The columns stay one-way regardless, and this is
    /// the assertion that says so.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AOneWayColumnBinding_NeverAsksTheConverterToGoBackwards()
    {
        Assert.Equal(0, BackwardCallsFor(BindingMode.OneWay));
    }
}
