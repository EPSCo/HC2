using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HC2.App.Converters;

/// <summary>Visible when the bound value is not null, Collapsed when it is.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
