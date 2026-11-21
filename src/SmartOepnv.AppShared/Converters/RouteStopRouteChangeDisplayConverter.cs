using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Converters;

public sealed class RouteStopRouteChangeDisplayConverter : IMultiValueConverter
{
    public static readonly RouteStopRouteChangeDisplayConverter Instance = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        _ = values.Length > 1 ? values[1] : null;
        if (values.Length > 0 && values[0] is RouteStopItem stop)
        {
            return RouteChangeDisplayHelper.FormatContinuation(stop) ?? string.Empty;
        }

        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RouteStopRouteChangeVisibleConverter : IMultiValueConverter
{
    public static readonly RouteStopRouteChangeVisibleConverter Instance = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        _ = values.Length > 1 ? values[1] : null;
        if (values.Length > 0 && values[0] is RouteStopItem stop)
        {
            return !string.IsNullOrWhiteSpace(RouteChangeDisplayHelper.FormatContinuation(stop))
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
