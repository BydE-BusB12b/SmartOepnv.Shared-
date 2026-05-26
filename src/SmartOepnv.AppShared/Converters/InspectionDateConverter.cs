using System.Globalization;
using System.Windows.Data;

namespace SmartOepnv.AppShared.Converters;

/// <summary>Bindet HU/SP-Datum (yyyy-MM-dd im JSON) an einen WPF-DatePicker.</summary>
public sealed class InspectionDateConverter : IValueConverter
{
    private const string Format = "yyyy-MM-dd";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        if (DateTime.TryParseExact(s.Trim(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var loose) ? loose : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt)
        {
            return string.Empty;
        }

        return dt.ToString(Format, CultureInfo.InvariantCulture);
    }
}
