using System.Globalization;
using System.Windows.Data;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core;
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

/// <summary>Routenliste: „681/01 2390 Hbf &gt; GWP Mo-Fr“. Jeden Tag ohne Kürzel.</summary>
public sealed class CompactRouteListLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null!;
        }

        if (value is not string key)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return null!;
        }

        var days = AppServices.Routes.Editor?.GetRouteOperatingDays(key);
        return RouteDisplayHelper.ToCompactRouteListLabel(key, days);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Einzelroute oder Routenschnur, je nach Schalter in der Routenliste.</summary>
public sealed class RouteListLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not string key || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (values.Length > 1 && values[1] is RoutesViewModel viewModel)
        {
            return viewModel.LabelForListedRoute(key);
        }

        var days = AppServices.Routes.Editor?.GetRouteOperatingDays(key);
        return RouteDisplayHelper.ToCompactRouteListLabel(key, days);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
