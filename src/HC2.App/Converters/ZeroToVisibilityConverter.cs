using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HC2.App.Converters;

/// <summary>Visible when the bound count is zero — used for the empty-list message.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
