using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Löst das effektive Routenwechselziel für einen Betriebstag auf.</summary>
public static class RouteChangeTargetResolver
{
    public static string Resolve(RouteStopItem? stop, DateOnly operatingDate)
    {
        if (stop is null || !stop.RouteChangeEnabled)
        {
            return string.Empty;
        }

        foreach (var entry in stop.RouteChangeTargetsByDate)
        {
            if (!Matches(entry, operatingDate))
            {
                continue;
            }

            var dated = entry.SelectedLineCourseTrip?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(dated) &&
                !string.Equals(dated, RouteStopEditorCatalog.NoLineCourseTripLabel, StringComparison.OrdinalIgnoreCase))
            {
                return dated;
            }
        }

        return NormalizeDefault(stop.SelectedLineCourseTrip);
    }

    /// <summary>
    /// Mehrere mögliche Betriebstage (z. B. 00:00–02:59 = Vortag und Kalendertag).
    /// Erste datierte Treffer-Variante gewinnt; sonst Standard.
    /// </summary>
    public static string Resolve(RouteStopItem? stop, IEnumerable<DateOnly> candidateOperatingDates)
    {
        if (stop is null || !stop.RouteChangeEnabled)
        {
            return string.Empty;
        }

        foreach (var date in candidateOperatingDates.Distinct().OrderByDescending(d => d))
        {
            foreach (var entry in stop.RouteChangeTargetsByDate)
            {
                if (!Matches(entry, date))
                {
                    continue;
                }

                var dated = entry.SelectedLineCourseTrip?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(dated) &&
                    !string.Equals(dated, RouteStopEditorCatalog.NoLineCourseTripLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return dated;
                }
            }
        }

        return NormalizeDefault(stop.SelectedLineCourseTrip);
    }

    public static bool HasDatedTargets(RouteStopItem? stop) =>
        stop?.RouteChangeTargetsByDate.Any(e =>
            e.HasScheduleConstraint &&
            !string.IsNullOrWhiteSpace(e.SelectedLineCourseTrip)) == true;

    public static bool Matches(RouteChangeTargetEntry entry, DateOnly operatingDate)
    {
        if (!entry.HasScheduleConstraint)
        {
            return false;
        }

        if (entry.OperatingDates.Count > 0 && entry.OperatingDates.Contains(operatingDate))
        {
            return true;
        }

        if (entry.OperatingDays.Count > 0)
        {
            var day = DutyOperatingDayHelper.FromDate(operatingDate);
            return entry.OperatingDays.Contains(day);
        }

        return false;
    }

    private static string NormalizeDefault(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.Equals(trimmed, RouteStopEditorCatalog.NoLineCourseTripLabel, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return trimmed;
    }
}
