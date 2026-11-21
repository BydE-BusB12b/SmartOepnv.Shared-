using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Converters;

/// <summary>HU-/SP-Hinweisfarben wie auf der Startseite (Fahrzeugliste).</summary>
public sealed class VehicleInspectionBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var useForeground = string.Equals(parameter as string, "Foreground", StringComparison.OrdinalIgnoreCase);
        if (value is not RegisteredVehicleItem vehicle)
        {
            return useForeground ? BodyForeground() : Brushes.Transparent;
        }

        var level = VehicleInspectionWarningEvaluator.GetWorstWarningLevel(vehicle);
        var (backgroundHex, foregroundHex) = level switch
        {
            VehicleInspectionWarningLevel.Notice30To16Days => ("#FFF176", "#002171"),
            VehicleInspectionWarningLevel.Notice15To5Days => ("#FFE0B2", "#002171"),
            VehicleInspectionWarningLevel.Urgent4To1Days => ("#FFCDD2", "#002171"),
            VehicleInspectionWarningLevel.Overdue => ("#9C27B0", "#FFFFFF"),
            _ => (string.Empty, string.Empty)
        };

        var hex = useForeground ? foregroundHex : backgroundHex;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return useForeground ? BodyForeground() : Brushes.Transparent;
        }

        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return useForeground ? BodyForeground() : Brushes.Transparent;
        }
    }

    /// <summary>Hellmodus dunkelblau, Dunkelmodus weiß – wie die übrigen Seiten.</summary>
    private static Brush BodyForeground() => UiTheme.ForegroundBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
