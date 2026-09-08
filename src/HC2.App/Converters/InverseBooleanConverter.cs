using System;
using System.Globalization;
using System.Windows.Data;

namespace HC2.App.Converters;

/// <summary>Negates a boolean — used to disable inputs while an operation is running.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}
