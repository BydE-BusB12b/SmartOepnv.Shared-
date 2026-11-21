using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Converters;

/// <summary>
/// Zeilenhintergrund der Haltestellenliste.
/// MultiBinding: [0]=RouteStopItem, [1]=Aktualisierungszähler (nur zum Neuzeichnen).
/// Zielwechsel gelb, Endhaltestelle rot, Starthaltestelle grün.
/// Dunkel: gedeckt, damit weiße Schrift lesbar bleibt. Hell: blasse Fläche.
/// </summary>
public sealed class RouteStopRoleBrushConverter : IMultiValueConverter
{
    public static readonly RouteStopRoleBrushConverter Instance = new();

    private static readonly SolidColorBrush ZielwechselDarkBrush = Freeze(Color.FromRgb(0x4A, 0x3F, 0x18));
    private static readonly SolidColorBrush EndDarkBrush = Freeze(Color.FromRgb(0x5C, 0x2B, 0x33));
    private static readonly SolidColorBrush StartDarkBrush = Freeze(Color.FromRgb(0x1E, 0x4A, 0x38));

    private static readonly SolidColorBrush ZielwechselLightBrush = Freeze(Color.FromRgb(0xFB, 0xF6, 0xE4));
    private static readonly SolidColorBrush EndLightBrush = Freeze(Color.FromRgb(0xFD, 0xEC, 0xEE));
    private static readonly SolidColorBrush StartLightBrush = Freeze(Color.FromRgb(0xE7, 0xF6, 0xEC));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not RouteStopItem stop)
        {
            return Brushes.Transparent;
        }

        var light = UiTheme.IsLight;
        if (stop.ZielwechselEnabled)
        {
            return light ? ZielwechselLightBrush : ZielwechselDarkBrush;
        }

        if (stop.IsEndStop)
        {
            return light ? EndLightBrush : EndDarkBrush;
        }

        if (RouteStopEditorCatalog.IsStartStop(stop))
        {
            return light ? StartLightBrush : StartDarkBrush;
        }

        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
