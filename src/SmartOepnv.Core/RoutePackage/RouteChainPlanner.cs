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
        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstCanonical = RouteDisplayHelper.ToCanonicalRouteKey(startRouteKey);
        var currentKey = ResolveRouteKey(editor, startRouteKey, null);
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
            var targetReference = FindRouteChangeTarget(editor, currentKey);
            if (string.IsNullOrWhiteSpace(targetReference))
            {
                break;
            }

            var resolvedTarget = ResolveRouteKey(editor, targetReference, currentKey);
            if (resolvedTarget is not null &&
                string.Equals(
                    RouteDisplayHelper.ToCanonicalRouteKey(resolvedTarget),
                    firstCanonical,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentKey = resolvedTarget;
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
        var segments = new List<ChainSegment>();
        for (var i = 0; i < chainKeys.Count; i++)
        {
            var routeKey = chainKeys[i];
            var stops = editor.GetStops(routeKey).Where(s => !s.IsWaypoint).ToList();
            var startTime = TryGetRouteStartTime(stops);
            var routeChangeReference = FindRouteChangeTarget(editor, routeKey);
            string? routeChangeDisplay = null;
            if (!string.IsNullOrWhiteSpace(routeChangeReference))
            {
                var resolvedTarget = ResolveRouteKey(editor, routeChangeReference, routeKey);
                routeChangeDisplay = FormatRouteLabel(resolvedTarget ?? routeChangeReference);
            }

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
                routeChangeDisplay,
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

    private static string? FindRouteChangeTarget(EditableRoutePackage editor, string routeKey)
    {
        var candidates = editor.GetStops(routeKey)
            .Where(s => s.RouteChangeEnabled)
            .Where(s => !string.IsNullOrWhiteSpace(s.SelectedLineCourseTrip))
            .ToList();

        return candidates.FirstOrDefault(s => s.IsEndStop)?.SelectedLineCourseTrip?.Trim()
               ?? candidates.FirstOrDefault()?.SelectedLineCourseTrip?.Trim();
    }

    private static string? ResolveRouteKey(
        EditableRoutePackage editor,
        string routeReference,
        string? contextRouteKey)
    {
        var trimmed = routeReference.Trim();
        var exact = editor.RouteNames.FirstOrDefault(r => string.Equals(r, trimmed, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        var canonicalMatches = editor.RouteNames
            .Where(r => RouteDisplayHelper.RouteKeysMatch(r, trimmed))
            .ToList();
        if (canonicalMatches.Count == 1)
        {
            return canonicalMatches[0];
        }

        if (canonicalMatches.Count > 1)
        {
            var disambiguated = DisambiguateByOperatingDays(editor, canonicalMatches, contextRouteKey);
            if (disambiguated is not null)
            {
                return disambiguated;
            }
        }

        var tripNumber = RouteDisplayHelper.NormalizeTripNumber(RouteDisplayHelper.Parse(trimmed).TripNumber);
        if (!string.IsNullOrEmpty(tripNumber) &&
            RouteStopEditorCatalog.TryResolveLineCourseTripByTripNumber(
                editor.RouteNames,
                tripNumber,
                contextRouteKey,
                out var tripMatch,
                out _))
        {
            return tripMatch;
        }

        var referenceDefinition = RouteDisplayHelper.Parse(trimmed);
        if (string.IsNullOrWhiteSpace(referenceDefinition.Name))
        {
            return null;
        }

        var identityMatches = editor.RouteNames
            .Where(route =>
            {
                var definition = RouteDisplayHelper.Parse(route);
                return string.Equals(definition.Name, referenceDefinition.Name, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(
                           RouteDisplayHelper.NormalizeLineCourse(definition.LineCourse),
                           RouteDisplayHelper.NormalizeLineCourse(referenceDefinition.LineCourse),
                           StringComparison.Ordinal);
            })
            .ToList();

        identityMatches = DisambiguateCandidatesByOperatingDays(editor, identityMatches, contextRouteKey);
        if (identityMatches.Count == 1)
        {
            return identityMatches[0];
        }

        if (identityMatches.Count > 1 && !string.IsNullOrWhiteSpace(contextRouteKey))
        {
            return TryResolveNextScheduledRoute(editor, contextRouteKey, identityMatches);
        }

        return null;
    }

    private static string? DisambiguateByOperatingDays(
        EditableRoutePackage editor,
        IReadOnlyList<string> candidates,
        string? contextRouteKey)
    {
        var filtered = DisambiguateCandidatesByOperatingDays(editor, candidates, contextRouteKey);
        return filtered.Count == 1 ? filtered[0] : null;
    }

    private static List<string> DisambiguateCandidatesByOperatingDays(
        EditableRoutePackage editor,
        IReadOnlyList<string> candidates,
        string? contextRouteKey)
    {
        if (string.IsNullOrWhiteSpace(contextRouteKey) || candidates.Count <= 1)
        {
            return candidates.ToList();
        }

        var contextDays = editor.GetRouteOperatingDays(contextRouteKey);
        var filtered = candidates
            .Where(route => RouteOperatingDaysEditor.DaysOverlap(
                contextDays,
                editor.GetRouteOperatingDays(route)))
            .ToList();

        return filtered.Count > 0 ? filtered : candidates.ToList();
    }

    private static string? TryResolveNextScheduledRoute(
        EditableRoutePackage editor,
        string contextRouteKey,
        IReadOnlyList<string> candidates)
    {
        var contextStops = editor.GetStops(contextRouteKey).Where(stop => !stop.IsWaypoint).ToList();
        var contextEnd = TryGetRouteEndTime(contextStops) ?? TryGetRouteStartTime(contextStops);
        if (!contextEnd.HasValue)
        {
            return null;
        }

        var contextMinutes = contextEnd.Value.Hour * 60 + contextEnd.Value.Minute;
        var ranked = candidates
            .Select(route =>
            {
                var stops = editor.GetStops(route).Where(stop => !stop.IsWaypoint).ToList();
                var start = TryGetRouteStartTime(stops);
                return (Route: route, Start: start);
            })
            .Where(entry => entry.Start.HasValue)
            .Select(entry => (
                entry.Route,
                StartMinutes: entry.Start!.Value.Hour * 60 + entry.Start!.Value.Minute))
            .Where(entry => entry.StartMinutes >= contextMinutes)
            .OrderBy(entry => entry.StartMinutes)
            .ThenBy(entry => ParseTripSortKey(RouteDisplayHelper.Parse(entry.Route).TripNumber))
            .ThenBy(entry => entry.Route, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ranked.Count > 0 ? ranked[0].Route : null;
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

    private static TimeOnly? TryGetRouteEndTime(IReadOnlyList<RouteStopItem> stops)
    {
        for (var index = stops.Count - 1; index >= 0; index--)
        {
            if (RouteScheduleTimeCalculator.TryParseTime(stops[index].Time, out var time))
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
