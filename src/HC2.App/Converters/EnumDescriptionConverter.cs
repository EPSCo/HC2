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
        => value is Enum e ? Describe(e) : value?.ToString() ?? string.Empty;

    /// <summary>The same lookup for code building labels outside a binding, e.g. <c>ADAM-4017P</c>.</summary>
    public static string Describe(Enum value)
    {
        var field = value.GetType().GetField(value.ToString(), BindingFlags.Public | BindingFlags.Static);

        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
