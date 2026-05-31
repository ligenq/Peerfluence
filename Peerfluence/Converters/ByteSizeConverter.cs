using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Peerfluence.Converters;

public sealed class ByteSizeConverter : IValueConverter
{
    public static readonly ByteSizeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return "0 B";
        }

        if (!TryGetDouble(value, out var bytes))
        {
            return value;
        }

        return FormatBytes(bytes);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    private static bool TryGetDouble(object value, out double bytes)
    {
        switch (value)
        {
            case byte b:
                bytes = b;
                return true;
            case short s:
                bytes = s;
                return true;
            case int i:
                bytes = i;
                return true;
            case long l:
                bytes = l;
                return true;
            case float f:
                bytes = f;
                return true;
            case double d:
                bytes = d;
                return true;
            case ulong ul:
                bytes = ul;
                return true;
            case uint ui:
                bytes = ui;
                return true;
            case ushort us:
                bytes = us;
                return true;
            default:
                bytes = 0;
                return false;
        }
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        var absBytes = Math.Abs(bytes);

        while (absBytes >= 1024 && unitIndex < units.Length - 1)
        {
            absBytes /= 1024;
            bytes /= 1024;
            unitIndex++;
        }

        return $"{bytes:0.##} {units[unitIndex]}";
    }
}
