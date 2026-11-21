using System.Globalization;
using System.Windows.Data;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Converters;

public sealed class RouteStopStrikeThroughConverter : IValueConverter
{
    public static readonly RouteStopStrikeThroughConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RouteStopItem stop && RouteStopEditorCatalog.ShouldStrikeThroughDisplay(stop);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
