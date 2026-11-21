using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartOepnv.AppShared.Converters;

/// <summary>Schiebt einen Kalenderbalken in seine Zeile unter den anderen Maßnahmen.</summary>
public sealed class CalendarLaneMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var lane = value is int index ? index : 0;
        return new Thickness(1, lane * 22, 1, 2);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
