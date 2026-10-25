using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmoothFrame.UI.Converters;

/// <summary>Collapsed when true, Visible when false (inverse of BooleanToVisibilityConverter).</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility != Visibility.Visible;
    }
}
