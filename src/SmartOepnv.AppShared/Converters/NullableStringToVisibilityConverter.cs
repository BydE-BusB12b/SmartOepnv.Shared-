using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartOepnv.AppShared.Converters;

/// <summary>Leer/null/Whitespace: Collapsed, sonst Visible (für einzelne Detailzeilen).</summary>
public sealed class NullableStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        return string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
