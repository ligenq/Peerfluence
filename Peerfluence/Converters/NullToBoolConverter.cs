using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Peerfluence.Converters;

public class NullToBoolConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNotNull = value != null;
        return Invert ? !isNotNull : isNotNull;
    }

    /// <summary>
    /// Refuses to answer, without throwing.
    ///
    /// <para>
    /// This converter formats a number for display and there is no way back from "1.5 GB" to the
    /// bytes it came from. Returning the input, which this once did, wrote a display string into the
    /// source; throwing, which it did after that, took the application down while a download was on
    /// screen, because a DataGridTextColumn binds two ways by default and asks for the way back just
    /// to show a row. DoNothing is the third answer: nothing is written and nothing breaks.
    /// </para>
    ///
    /// <para>
    /// The binding that should never have asked is caught by an architecture test instead, where a
    /// mistake in the markup costs a failing build rather than somebody's evening.
    /// </para>
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
