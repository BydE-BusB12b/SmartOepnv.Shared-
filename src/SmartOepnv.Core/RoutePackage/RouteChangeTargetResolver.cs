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
            if (entry.OperatingDates.Count == 0)
            {
                continue;
            }

            if (!entry.OperatingDates.Contains(operatingDate))
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
                if (entry.OperatingDates.Count == 0 || !entry.OperatingDates.Contains(date))
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
            e.OperatingDates.Count > 0 &&
            !string.IsNullOrWhiteSpace(e.SelectedLineCourseTrip)) == true;

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
