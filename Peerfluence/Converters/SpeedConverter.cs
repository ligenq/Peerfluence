using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Peerfluence.Converters;

public sealed class SpeedConverter : IValueConverter
{
    public static readonly SpeedConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytesConverter = ByteSizeConverter.Instance;
        var formatted = bytesConverter.Convert(value, targetType, parameter, culture);
        return $"{formatted}/s";
    }

    /// <summary>
    /// Refused rather than answered. This converter formats a number for display and there is no
    /// way back from "1.5 GB" to the bytes it came from, so a two-way binding through it is a
    /// mistake in the XAML. Returning the input, which this used to do, made that mistake silent.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException($"{nameof(SpeedConverter)} formats values for display and cannot convert back.");
    }
}
