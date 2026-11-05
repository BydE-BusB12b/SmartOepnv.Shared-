using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Zeitberechnung wie <c>MainActivity.calculateTripStartTime</c> / <c>calculateStopTime</c>.</summary>
public static class RouteScheduleTimeCalculator
{
    private const string TimeFormat = "HH:mm";
    private static readonly Regex CompactTimeRegex = new(@"^\d{1,4}$", RegexOptions.Compiled);

    /// <summary>
    /// Verschiebt alle parsebaren Haltestellenzeiten um <paramref name="deltaMinutes"/>
    /// (Abstände bleiben gleich). Leere/ungültige Zeiten bleiben unverändert.
    /// </summary>
    /// <returns>Anzahl geänderter Haltestellen.</returns>
    public static int ShiftAllStopTimes(IEnumerable<RouteStopItem> stops, int deltaMinutes)
    {
        if (deltaMinutes == 0)
        {
            return 0;
        }

        var changed = 0;
        foreach (var stop in stops)
        {
            if (stop.IsWaypoint || string.IsNullOrWhiteSpace(stop.Time))
            {
                continue;
            }

            if (!TryParseTime(stop.Time, out var time))
            {
                continue;
            }

            stop.Time = time.AddMinutes(deltaMinutes).ToString(TimeFormat, CultureInfo.InvariantCulture);
            changed++;
        }

        return changed;
    }

    public static string CalculateTripStartTime(string baseTime, int intervalMinutes)
    {
        if (!TryParseTime(baseTime, out var start))
        {
            return baseTime;
        }

        return start.AddMinutes(intervalMinutes).ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    public static string? CalculateStopTime(string newStartTime, string? templateStartTime, string? templateStopTime)
    {
        if (string.IsNullOrWhiteSpace(templateStartTime) || string.IsNullOrWhiteSpace(templateStopTime))
        {
            return null;
        }

        if (!TryParseTime(templateStartTime, out var templateStart) ||
            !TryParseTime(templateStopTime, out var templateStop) ||
            !TryParseTime(newStartTime, out var newStart))
        {
            return null;
        }

        var offset = templateStop - templateStart;
        return newStart.Add(offset).ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Normalisiert Haltestellenzeiten: <c>14:24</c> bleibt, <c>1424</c> wird zu <c>14:24</c>, <c>924</c> zu <c>09:24</c>.
    /// </summary>
    public static string NormalizeTimeInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return TryParseTime(value, out var time)
            ? time.ToString(TimeFormat, CultureInfo.InvariantCulture)
            : value.Trim();
    }

    public static bool TryParseTime(string? value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().Replace('.', ':');
        if (TimeOnly.TryParseExact(
                trimmed,
                TimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time))
        {
            return true;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (!CompactTimeRegex.IsMatch(digits))
        {
            return false;
        }

        return TryParseCompactDigits(digits, out time);
    }

    private static bool TryParseCompactDigits(string digits, out TimeOnly time)
    {
        time = default;
        if (digits.Length == 0)
        {
            return false;
        }

        int hour;
        int minute;
        switch (digits.Length)
        {
            case 1:
            case 2:
                hour = int.Parse(digits, CultureInfo.InvariantCulture);
                minute = 0;
                break;
            case 3:
                hour = int.Parse(digits[..1], CultureInfo.InvariantCulture);
                minute = int.Parse(digits[1..], CultureInfo.InvariantCulture);
                break;
            default:
                hour = int.Parse(digits[..2], CultureInfo.InvariantCulture);
                minute = int.Parse(digits[2..4], CultureInfo.InvariantCulture);
                break;
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }
}
