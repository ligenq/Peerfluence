using System;
using System.Globalization;
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
