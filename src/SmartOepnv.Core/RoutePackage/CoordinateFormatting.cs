using System.Globalization;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>GPS-Koordinaten als „lat, lon“ mit fester Genauigkeit (Planer-UI).</summary>
public static class CoordinateFormatting
{
    public const int DecimalPlaces = 6;

    public static string Format(double lat, double lon) =>
        $"{FormatComponent(lat)}, {FormatComponent(lon)}";

    public static string FormatFromParts(string? latRaw, string? lonRaw)
    {
        if (!TryParseParts(latRaw, lonRaw, out var lat, out var lon))
        {
            return string.Empty;
        }

        return Format(lat, lon);
    }

    public static bool TryParsePair(string? raw, out string latOut, out string lonOut)
    {
        latOut = lonOut = string.Empty;
        if (!RouteCoordinateParser.TryParse(raw, out var lat, out var lon))
        {
            return false;
        }

        latOut = FormatComponent(lat);
        lonOut = FormatComponent(lon);
        return true;
    }

    public static bool TryParseParts(string? latRaw, string? lonRaw, out double lat, out double lon)
    {
        lat = lon = double.NaN;
        if (string.IsNullOrWhiteSpace(latRaw) || string.IsNullOrWhiteSpace(lonRaw))
        {
            return false;
        }

        if (!TryParseDouble(latRaw, out lat) || !TryParseDouble(lonRaw, out lon))
        {
            return false;
        }

        return double.IsFinite(lat) && double.IsFinite(lon) &&
               Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
    }

    private static bool TryParseDouble(string raw, out double value) =>
        double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(raw.Trim(), NumberStyles.Float, new CultureInfo("de-DE"), out value);

    public static void NormalizeTemplate(ManagedStopTemplateItem template)
    {
        if (TryParseParts(template.AnnouncementLat, template.AnnouncementLng, out var annLat, out var annLon))
        {
            template.AnnouncementLat = FormatComponent(annLat);
            template.AnnouncementLng = FormatComponent(annLon);
        }

        if (TryParseParts(template.StopLat, template.StopLng, out var stopLat, out var stopLon))
        {
            template.StopLat = FormatComponent(stopLat);
            template.StopLng = FormatComponent(stopLon);
        }
    }

    public static string FormatComponent(double value) =>
        Math.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero)
            .ToString($"F{DecimalPlaces}", CultureInfo.InvariantCulture);
}
