using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace HC2.App.Converters;

/// <summary>Shows an enum value's <see cref="DescriptionAttribute"/> instead of its member name.</summary>
public sealed class EnumDescriptionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum) return value?.ToString() ?? string.Empty;

        var field = value.GetType().GetField(value.ToString()!, BindingFlags.Public | BindingFlags.Static);

        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString()!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
