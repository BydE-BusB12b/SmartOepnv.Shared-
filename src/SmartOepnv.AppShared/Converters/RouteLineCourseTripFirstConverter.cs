using System.Globalization;
using System.Windows.Data;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Converters;

/// <summary>Zeigt Routenschlüssel als „Linie/Kurs, Fahrt … Name“ an.</summary>
public sealed class RouteLineCourseTripFirstConverter : IValueConverter
{
    public static readonly RouteLineCourseTripFirstConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key
            ? RouteDisplayHelper.ToLineCourseTripFirstDisplayString(key)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
