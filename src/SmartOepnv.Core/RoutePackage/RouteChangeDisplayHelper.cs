namespace SmartOepnv.Core.RoutePackage;

/// <summary>Anzeige für automatischen Routenwechsel an der Endhaltestelle (Planer-Liste).</summary>
public static class RouteChangeDisplayHelper
{
    /// <summary>
    /// z. B. „weiter als: 002/01 Fahrt 3 von Düsseldorf Hbf nach Köln Hbf“.
    /// </summary>
    public static string? FormatContinuation(RouteStopItem? stop)
    {
        if (stop is null || !stop.IsEndStop || !stop.RouteChangeEnabled)
        {
            return null;
        }

        var targetRef = stop.SelectedLineCourseTrip?.Trim();
        if (string.IsNullOrWhiteSpace(targetRef) ||
            string.Equals(targetRef, RouteStopEditorCatalog.NoLineCourseTripLabel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parsed = RouteDisplayHelper.Parse(targetRef);
        var lineCourse = (parsed.LineCourse ?? string.Empty).Trim();
        var trip = RouteDisplayHelper.NormalizeTripNumber(parsed.TripNumber ?? string.Empty);
        var targetName = (parsed.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetName))
        {
            targetName = targetRef;
        }

        var fromName = (stop.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fromName))
        {
            fromName = (stop.StopDisplay ?? string.Empty).Trim();
        }

        if (string.IsNullOrWhiteSpace(fromName))
        {
            fromName = "Endhaltestelle";
        }

        var segments = new List<string> { "weiter als:" };
        if (!string.IsNullOrEmpty(lineCourse))
        {
            segments.Add(lineCourse);
        }

        if (!string.IsNullOrEmpty(trip))
        {
            segments.Add($"Fahrt {trip}");
        }

        segments.Add($"von {fromName}");
        segments.Add($"nach {targetName}");
        return string.Join(" ", segments);
    }
}
