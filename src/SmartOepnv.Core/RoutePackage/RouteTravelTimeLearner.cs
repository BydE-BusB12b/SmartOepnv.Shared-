namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Lernt typische Fahrzeiten zwischen aufeinanderfolgenden Haltestellen (per PlannerStopCode)
/// aus vorhandenen Routen und schlägt Folgezeiten vor.
/// </summary>
public static class RouteTravelTimeLearner
{
    /// <summary>
    /// Baut eine Tabelle FromCode→ToCode → typische Minuten (Median der beobachteten Abstände).
    /// Waypoints und Halte ohne gültige ID/Zeit werden übersprungen (unterbrechen die Kette).
    /// </summary>
    public static IReadOnlyDictionary<(string FromCode, string ToCode), int> BuildTypicalMinutes(
        IEnumerable<IEnumerable<RouteStopItem>> routeStopLists)
    {
        var samples = new Dictionary<(string From, string To), List<int>>();

        foreach (var stops in routeStopLists)
        {
            string? previousCode = null;
            TimeOnly? previousTime = null;

            foreach (var stop in stops)
            {
                if (stop.IsWaypoint)
                {
                    continue;
                }

                var code = PlannerStopCode.Normalize(stop.PlannerStopCode);
                var hasCode = PlannerStopCode.IsValid(code);
                var hasTime = RouteScheduleTimeCalculator.TryParseTime(stop.Time, out var time);

                if (hasCode &&
                    hasTime &&
                    previousCode is not null &&
                    previousTime is not null &&
                    !string.Equals(previousCode, code, StringComparison.Ordinal) &&
                    TryGetForwardMinutes(previousTime.Value, time, out var minutes) &&
                    minutes > 0)
                {
                    var key = (previousCode, code);
                    if (!samples.TryGetValue(key, out var list))
                    {
                        list = [];
                        samples[key] = list;
                    }

                    list.Add(minutes);
                }

                if (hasCode && hasTime)
                {
                    previousCode = code;
                    previousTime = time;
                }
                else
                {
                    previousCode = null;
                    previousTime = null;
                }
            }
        }

        var result = new Dictionary<(string FromCode, string ToCode), int>(samples.Count);
        foreach (var (key, list) in samples)
        {
            list.Sort();
            result[key] = list[list.Count / 2];
        }

        return result;
    }

    public static bool TrySuggestArrival(
        IReadOnlyDictionary<(string FromCode, string ToCode), int> typicalMinutes,
        string? fromCode,
        string? fromTime,
        string? toCode,
        out string suggestedTimeHhMm)
    {
        suggestedTimeHhMm = string.Empty;
        var from = PlannerStopCode.Normalize(fromCode);
        var to = PlannerStopCode.Normalize(toCode);
        if (!PlannerStopCode.IsValid(from) ||
            !PlannerStopCode.IsValid(to) ||
            !RouteScheduleTimeCalculator.TryParseTime(fromTime, out var start) ||
            !typicalMinutes.TryGetValue((from, to), out var minutes) ||
            minutes <= 0)
        {
            return false;
        }

        var arrival = start.AddMinutes(minutes);
        suggestedTimeHhMm = arrival.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryGetForwardMinutes(TimeOnly from, TimeOnly to, out int minutes)
    {
        var fromMin = from.Hour * 60 + from.Minute;
        var toMin = to.Hour * 60 + to.Minute;
        if (toMin >= fromMin)
        {
            minutes = toMin - fromMin;
            return true;
        }

        // Über Mitternacht vorwärts (wie RouteStopTimeOrder).
        var overnight = (24 * 60 - fromMin) + toMin;
        if (overnight > 0 && overnight <= RouteStopTimeOrder.MaxOvernightForwardHours * 60)
        {
            minutes = overnight;
            return true;
        }

        minutes = 0;
        return false;
    }
}
