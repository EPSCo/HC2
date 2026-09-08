using System;
using System.Globalization;
using System.Windows.Data;

namespace HC2.App.Converters;

/// <summary>Replaces a null or blank string with an em dash so detail rows never look broken.</summary>
public sealed class EmptyTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();

        return string.IsNullOrWhiteSpace(text) ? "—" : text!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
