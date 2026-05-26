using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePath;

public static partial class RouteCoordinateParser
{
    public static bool TryParse(string? raw, out double lat, out double lon)
    {
        lat = lon = double.NaN;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        text = text.Replace(';', ',').Replace("\t", " ");

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            TryParseNumber(parts[0], out lat) &&
            TryParseNumber(parts[1], out lon))
        {
            return NormalizeLatLon(ref lat, ref lon);
        }

        if (parts.Length == 4 &&
            TryParseNumber($"{parts[0]}.{parts[1]}", out lat) &&
            TryParseNumber($"{parts[2]}.{parts[3]}", out lon))
        {
            return NormalizeLatLon(ref lat, ref lon);
        }

        var match = CoordinateRegex().Match(text);
        if (match.Success &&
            TryParseNumber(match.Groups["lat"].Value, out lat) &&
            TryParseNumber(match.Groups["lon"].Value, out lon))
        {
            return NormalizeLatLon(ref lat, ref lon);
        }

        return false;
    }

    private static bool TryParseNumber(string value, out double number)
    {
        value = value.Trim();
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        return double.TryParse(value, NumberStyles.Float, new CultureInfo("de-DE"), out number);
    }

    private static bool NormalizeLatLon(ref double lat, ref double lon)
    {
        if (!double.IsFinite(lat) || !double.IsFinite(lon))
        {
            return false;
        }

        if (Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)
        {
            return true;
        }

        if (Math.Abs(lon) <= 90 && Math.Abs(lat) <= 180)
        {
            (lat, lon) = (lon, lat);
            return Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
        }

        return false;
    }

    [GeneratedRegex(@"(?<lat>-?\d+(?:[.,]\d+)?)\s*,\s*(?<lon>-?\d+(?:[.,]\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinateRegex();
}
