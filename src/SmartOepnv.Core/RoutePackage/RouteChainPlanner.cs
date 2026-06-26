using System.Globalization;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Linie/Kurs-Fahrten und Routenschnur (verbundene Fahrten über Routenwechsel) – wie in der App.
/// </summary>
public static class RouteChainPlanner
{
    public sealed record TripCandidate(
        string RouteKey,
        RouteDefinition Definition,
        TimeOnly? StartTime,
        int StopCount);

    public sealed record StopScheduleRow(string Name, string TimeDisplay, bool IsRouteChangeStop);

    public sealed record ChainSegment(
        int Index,
        string RouteKey,
        string RouteLabel,
        string? StartTimeDisplay,
        string? OperatingDaysDisplay,
        string? RouteChangeTo,
        IReadOnlyList<StopScheduleRow> Stops);

    public static IReadOnlyList<TripCandidate> FindTripsByLineCourse(
        EditableRoutePackage editor,
        string normalizedLineCourse)
    {
        if (string.IsNullOrWhiteSpace(normalizedLineCourse))
        {
            return [];
        }

        var target = RouteDisplayHelper.NormalizeLineCourse(normalizedLineCourse);
        var results = new List<TripCandidate>();
        foreach (var routeKey in editor.RouteNames)
        {
            var definition = RouteDisplayHelper.Parse(routeKey);
            if (!string.Equals(
                    RouteDisplayHelper.NormalizeLineCourse(definition.LineCourse),
                    target,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var stops = editor.GetStops(routeKey).Where(s => !s.IsWaypoint).ToList();
            results.Add(new TripCandidate(
                routeKey,
                definition,
                TryGetRouteStartTime(stops),
                stops.Count));
        }

        var startMinutes = results
            .Select(t => ToMinutesFromMidnight(t.StartTime))
            .Where(minutes => minutes.HasValue)
            .Select(minutes => minutes!.Value)
            .ToList();
        var spansOperatingDayMidnight = DutyTemplateCalculator.SpansOperatingDayMidnight(startMinutes);

        return results
            .OrderBy(t => ToOperatingDaySortKey(t.StartTime, spansOperatingDayMidnight))
            .ThenBy(t => ParseTripSortKey(t.Definition.TripNumber))
            .ThenBy(t => t.RouteKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> BuildConnectedRouteChain(
        EditableRoutePackage editor,
        string startRouteKey)
    {
        var allStops = editor.StopsByRoute.Values.SelectMany(s => s).ToList();
        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstCanonical = RouteDisplayHelper.ToCanonicalRouteKey(startRouteKey);
        var currentKey = ResolveRouteKey(editor, startRouteKey);
        if (currentKey is null)
        {
            return [];
        }

        while (!string.IsNullOrWhiteSpace(currentKey))
        {
            var canonical = RouteDisplayHelper.ToCanonicalRouteKey(currentKey);
            if (!visited.Add(canonical))
            {
                break;
            }

            chain.Add(currentKey);
            var targetReference = FindRouteChangeTarget(currentKey, allStops);
            if (string.IsNullOrWhiteSpace(targetReference))
            {
                break;
            }

            if (string.Equals(
                    RouteDisplayHelper.ToCanonicalRouteKey(targetReference),
                    firstCanonical,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentKey = ResolveRouteKey(editor, targetReference);
            if (currentKey is null)
            {
                break;
            }
        }

        return chain;
    }

    public static IReadOnlyList<ChainSegment> BuildChainSchedule(
        EditableRoutePackage editor,
        string startRouteKey)
    {
        var chainKeys = BuildConnectedRouteChain(editor, startRouteKey);
        var allStops = editor.StopsByRoute.Values.SelectMany(s => s).ToList();
        var segments = new List<ChainSegment>();
        for (var i = 0; i < chainKeys.Count; i++)
        {
            var routeKey = chainKeys[i];
            var stops = editor.GetStops(routeKey).Where(s => !s.IsWaypoint).ToList();
            var startTime = TryGetRouteStartTime(stops);
            var routeChangeTarget = FindRouteChangeTarget(routeKey, allStops);
            var operatingDays = editor.GetRouteOperatingDays(routeKey);
            var daysLabel = RouteOperatingDaysEditor.IsConfiguredForAllDays(operatingDays)
                ? null
                : DutyOperatingDayHelper.FormatDisplay(operatingDays);

            var rows = stops.Select(stop =>
            {
                var isChange = stop.RouteChangeEnabled &&
                               !string.IsNullOrWhiteSpace(stop.SelectedLineCourseTrip);
                var time = string.IsNullOrWhiteSpace(stop.Time) ? "--:--" : stop.Time.Trim();
                return new StopScheduleRow(stop.Name, time, isChange);
            }).ToList();

            segments.Add(new ChainSegment(
                i + 1,
                routeKey,
                FormatRouteLabel(routeKey),
                startTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                daysLabel,
                string.IsNullOrWhiteSpace(routeChangeTarget)
                    ? null
                    : FormatRouteLabel(routeChangeTarget),
                rows));
        }

        return segments;
    }

    private static string FormatRouteLabel(string routeKey)
    {
        var parsed = RouteDisplayHelper.Parse(routeKey);
        var trip = RouteDisplayHelper.NormalizeTripNumber(parsed.TripNumber);
        if (!string.IsNullOrWhiteSpace(trip))
        {
            return $"Fahrt {trip} – {parsed.Name}";
        }

        return routeKey;
    }

    private static string? FindRouteChangeTarget(string routeKey, IEnumerable<RouteStopItem> allStops)
    {
        var candidates = allStops
            .Where(s => RouteDisplayHelper.RouteKeysMatch(s.RouteName, routeKey))
            .Where(s => s.RouteChangeEnabled)
            .Where(s => !string.IsNullOrWhiteSpace(s.SelectedLineCourseTrip))
            .ToList();

        return candidates.FirstOrDefault(s => s.IsEndStop)?.SelectedLineCourseTrip?.Trim()
               ?? candidates.FirstOrDefault()?.SelectedLineCourseTrip?.Trim();
    }

    private static string? ResolveRouteKey(EditableRoutePackage editor, string routeReference)
    {
        var trimmed = routeReference.Trim();
        var exact = editor.RouteNames.FirstOrDefault(r => string.Equals(r, trimmed, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        return editor.RouteNames.FirstOrDefault(r => RouteDisplayHelper.RouteKeysMatch(r, trimmed));
    }

    private static TimeOnly? TryGetRouteStartTime(IReadOnlyList<RouteStopItem> stops)
    {
        foreach (var stop in stops)
        {
            if (RouteScheduleTimeCalculator.TryParseTime(stop.Time, out var time))
            {
                return time;
            }
        }

        return null;
    }

    private static int? ToMinutesFromMidnight(TimeOnly? time) =>
        time.HasValue ? time.Value.Hour * 60 + time.Value.Minute : null;

    private static int ToOperatingDaySortKey(TimeOnly? time, bool spansOperatingDayMidnight)
    {
        var minutes = ToMinutesFromMidnight(time);
        if (!minutes.HasValue)
        {
            return int.MaxValue;
        }

        return DutyTemplateCalculator.ToOperatingDaySortKey(minutes.Value, spansOperatingDayMidnight);
    }

    private static int ParseTripSortKey(string? tripNumber)
    {
        var normalized = RouteDisplayHelper.NormalizeTripNumber(tripNumber);
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : int.MaxValue;
    }
}
