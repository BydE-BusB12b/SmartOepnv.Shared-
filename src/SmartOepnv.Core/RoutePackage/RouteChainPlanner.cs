using System.Globalization;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Linie/Kurs-Fahrten und Routenschnur (verbundene Fahrten über Routenwechsel) – wie in der App.
/// Optional mit Prüfdatum von/bis: Verkehrstag + Gültigkeit für korrekte Folgefahrten.
/// </summary>
public static class RouteChainPlanner
{
    /// <summary>Prüfzeitraum für Fahrtenliste und Routenschnur-Auflösung.</summary>
    public sealed record ChainCheckFilter(DateOnly? From, DateOnly? To)
    {
        public static ChainCheckFilter None { get; } = new(null, null);

        public bool HasDates => From is not null || To is not null;

        /// <summary>Referenzdatum für Verkehrstag (bevorzugt „von“, sonst „bis“).</summary>
        public DateOnly? ReferenceDate => From ?? To;

        public RouteDateRange AsQueryRange => new() { From = From, To = To };
    }

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
        string normalizedLineCourse,
        ChainCheckFilter? filter = null)
    {
        if (string.IsNullOrWhiteSpace(normalizedLineCourse))
        {
            return [];
        }

        var check = filter ?? ChainCheckFilter.None;
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

            if (!MatchesAnyDayInFilter(editor, routeKey, check))
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
        string startRouteKey,
        ChainCheckFilter? filter = null)
    {
        var check = filter ?? ChainCheckFilter.None;
        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstCanonical = RouteDisplayHelper.ToCanonicalRouteKey(startRouteKey);
        var currentKey = ResolveRouteKey(editor, startRouteKey, null, check);
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

            var resolvedTarget = ResolveRouteKey(editor, targetReference, currentKey, check);
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
        string startRouteKey,
        ChainCheckFilter? filter = null)
    {
        var chainKeys = BuildConnectedRouteChain(editor, startRouteKey, filter);
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
                var resolvedTarget = ResolveRouteKey(editor, routeChangeReference, routeKey, filter);
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

    /// <summary>Fahrtenliste: Route an mindestens einem Tag im Prüfzeitraum gültig.</summary>
    internal static bool MatchesAnyDayInFilter(
        EditableRoutePackage editor,
        string routeKey,
        ChainCheckFilter filter)
    {
        if (!filter.HasDates)
        {
            return true;
        }

        if (filter.From is { } from && filter.To is { } to)
        {
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                if (IsRouteActiveOn(editor, routeKey, date))
                {
                    return true;
                }
            }

            return false;
        }

        return filter.ReferenceDate is { } reference && IsRouteActiveOn(editor, routeKey, reference);
    }

    /// <summary>Routenschnur: Route am Referenzdatum („von“) gültig.</summary>
    internal static bool MatchesReferenceDay(
        EditableRoutePackage editor,
        string routeKey,
        ChainCheckFilter filter)
    {
        if (!filter.HasDates || filter.ReferenceDate is not { } reference)
        {
            return true;
        }

        return IsRouteActiveOn(editor, routeKey, reference);
    }

    private static bool IsRouteActiveOn(
        EditableRoutePackage editor,
        string routeKey,
        DateOnly date)
    {
        var range = editor.GetRouteDateRange(routeKey);
        if (!range.Contains(date))
        {
            return false;
        }

        var operatingDates = editor.GetRouteOperatingDates(routeKey);
        if (!RouteOperatingDatesEditor.Contains(operatingDates, date))
        {
            return false;
        }

        var requiredDay = DutyOperatingDayHelper.FromDate(date);
        var routeDays = editor.GetRouteOperatingDays(routeKey);
        return RouteOperatingDaysEditor.EffectiveDaySet(routeDays).Contains(requiredDay);
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
        string? contextRouteKey,
        ChainCheckFilter? filter)
    {
        var check = filter ?? ChainCheckFilter.None;
        var trimmed = routeReference.Trim();
        var exact = editor.RouteNames.FirstOrDefault(r => string.Equals(r, trimmed, StringComparison.Ordinal));
        if (exact is not null)
        {
            return PreferMatchingFilter(editor, [exact], check) ?? exact;
        }

        var canonicalMatches = editor.RouteNames
            .Where(r => RouteDisplayHelper.RouteKeysMatch(r, trimmed))
            .ToList();
        if (canonicalMatches.Count == 1)
        {
            return PreferMatchingFilter(editor, canonicalMatches, check) ?? canonicalMatches[0];
        }

        if (canonicalMatches.Count > 1)
        {
            var disambiguated = DisambiguateCandidates(editor, canonicalMatches, contextRouteKey, check);
            if (disambiguated is not null)
            {
                return disambiguated;
            }
        }

        var tripNumber = RouteDisplayHelper.NormalizeTripNumber(RouteDisplayHelper.Parse(trimmed).TripNumber);
        if (!string.IsNullOrEmpty(tripNumber))
        {
            var tripMatches = editor.RouteNames
                .Where(route =>
                    string.Equals(
                        RouteDisplayHelper.NormalizeTripNumber(RouteDisplayHelper.Parse(route).TripNumber),
                        tripNumber,
                        StringComparison.Ordinal))
                .ToList();

            if (!string.IsNullOrWhiteSpace(contextRouteKey))
            {
                var contextLineCourse = RouteDisplayHelper.NormalizeLineCourse(
                    RouteDisplayHelper.Parse(contextRouteKey).LineCourse);
                if (!string.IsNullOrEmpty(contextLineCourse))
                {
                    var sameLine = tripMatches
                        .Where(route =>
                            string.Equals(
                                RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(route).LineCourse),
                                contextLineCourse,
                                StringComparison.Ordinal))
                        .ToList();
                    if (sameLine.Count > 0)
                    {
                        tripMatches = sameLine;
                    }
                }
            }

            var tripResolved = DisambiguateCandidates(editor, tripMatches, contextRouteKey, check);
            if (tripResolved is not null)
            {
                return tripResolved;
            }

            if (tripMatches.Count == 1)
            {
                return tripMatches[0];
            }
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

        var identityResolved = DisambiguateCandidates(editor, identityMatches, contextRouteKey, check);
        if (identityResolved is not null)
        {
            return identityResolved;
        }

        if (identityMatches.Count > 1 && !string.IsNullOrWhiteSpace(contextRouteKey))
        {
            var filtered = FilterCandidatesByCheck(editor, identityMatches, check);
            return TryResolveNextScheduledRoute(editor, contextRouteKey, filtered);
        }

        return null;
    }

    private static string? DisambiguateCandidates(
        EditableRoutePackage editor,
        IReadOnlyList<string> candidates,
        string? contextRouteKey,
        ChainCheckFilter filter)
    {
        var filtered = FilterCandidatesByCheck(editor, candidates, filter);
        if (filtered.Count == 1)
        {
            return filtered[0];
        }

        if (string.IsNullOrWhiteSpace(contextRouteKey) || filtered.Count <= 1)
        {
            return filtered.Count == 1 ? filtered[0] : null;
        }

        var contextDays = editor.GetRouteOperatingDays(contextRouteKey);
        var byDays = filtered
            .Where(route => RouteOperatingDaysEditor.DaysOverlap(
                contextDays,
                editor.GetRouteOperatingDays(route)))
            .ToList();

        var dayFiltered = byDays.Count > 0 ? byDays : filtered;
        if (dayFiltered.Count == 1)
        {
            return dayFiltered[0];
        }

        if (!string.IsNullOrWhiteSpace(contextRouteKey) && dayFiltered.Count > 1)
        {
            return TryResolveNextScheduledRoute(editor, contextRouteKey, dayFiltered);
        }

        return null;
    }

    private static List<string> FilterCandidatesByCheck(
        EditableRoutePackage editor,
        IReadOnlyList<string> candidates,
        ChainCheckFilter filter)
    {
        if (!filter.HasDates)
        {
            return candidates.ToList();
        }

        var matched = candidates.Where(c => MatchesReferenceDay(editor, c, filter)).ToList();
        return matched.Count > 0 ? matched : candidates.ToList();
    }

    private static string? PreferMatchingFilter(
        EditableRoutePackage editor,
        IReadOnlyList<string> candidates,
        ChainCheckFilter filter)
    {
        if (!filter.HasDates)
        {
            return candidates.Count == 1 ? candidates[0] : null;
        }

        var matched = candidates.Where(c => MatchesReferenceDay(editor, c, filter)).ToList();
        return matched.Count == 1 ? matched[0] : null;
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
