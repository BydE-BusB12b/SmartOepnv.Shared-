using System.Globalization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Zeitberechnung wie <c>MainActivity.calculateTripStartTime</c> / <c>calculateStopTime</c>.</summary>
public static class RouteScheduleTimeCalculator
{
    private const string TimeFormat = "HH:mm";

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

    public static bool TryParseTime(string? value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return TimeOnly.TryParseExact(
            value.Trim(),
            TimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);
    }
}
