using System.Globalization;
using System.Windows.Data;

namespace SmartOepnv.AppShared.Converters;

/// <summary>Millimeter → Vorschau-Pixel (Faktor 6).</summary>
public sealed class MmToPreviewPxConverter : IValueConverter
{
    public const double Scale = 6.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var mm = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            decimal m => (double)m,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
            _ => 0d
        };
        return mm * Scale;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Schriftgröße in pt → Vorschau-Pixel (1 pt = 25,4/72 mm × Scale).</summary>
public sealed class FontPtToPreviewPxConverter : IValueConverter
{
    private const double MmPerPt = 25.4 / 72.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pt = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            decimal m => (double)m,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
            _ => 12d
        };
        return Math.Max(6.0, pt * MmPerPt * MmToPreviewPxConverter.Scale);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
