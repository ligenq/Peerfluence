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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
