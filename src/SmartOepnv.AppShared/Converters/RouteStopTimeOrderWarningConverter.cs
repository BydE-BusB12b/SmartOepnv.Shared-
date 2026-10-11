using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Converters;

/// <summary>
/// MultiBinding: [0]=RouteStopItem, [1]=RoutesViewModel, [2]=StopTimeOrderWarningTick.
/// Rot, wenn die Uhrzeit vor der vorherigen Haltestelle liegt.
/// </summary>
public sealed class RouteStopTimeOrderWarningConverter : IMultiValueConverter
{
    public static readonly RouteStopTimeOrderWarningConverter Instance = new();

    private static readonly SolidColorBrush WarningBrush = CreateFrozenBrush(0xFF, 0xFF, 0x52, 0x52);
    private static readonly SolidColorBrush NormalBrush = CreateFrozenBrush(0xFF, 0xFF, 0xFF, 0xFF);

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 ||
            values[0] is not RouteStopItem stop)
        {
            return NormalBrush;
        }

        // Bevorzugt ViewModel-Cache (zuverlässig nach Tick-Refresh).
        if (values[1] is RoutesViewModel viewModel)
        {
            return viewModel.HasStopTimeOrderWarning(stop) ? WarningBrush : NormalBrush;
        }

        // Fallback: Liste direkt auswerten (z. B. ItemsSource).
        if (values[1] is System.Collections.IEnumerable enumerable)
        {
            var stops = enumerable.OfType<RouteStopItem>().ToList();
            return RouteStopTimeOrder.HasIssueForStop(stops, stop) ? WarningBrush : NormalBrush;
        }

        return NormalBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
