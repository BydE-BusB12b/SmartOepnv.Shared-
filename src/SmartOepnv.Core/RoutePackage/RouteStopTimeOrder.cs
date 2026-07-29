namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Prüft, ob Haltestellenzeiten in Routenreihenfolge rückwärts springen
/// (z. B. 10:51 → 09:53). Über Mitternacht vorwärts (23:50 → 00:15) zählt nicht als Fehler.
/// </summary>
public static class RouteStopTimeOrder
{
    /// <summary>Max. Vorwärts-Sprung über Mitternacht ohne Warnung (Stunden).</summary>
    public const int MaxOvernightForwardHours = 8;

    public readonly record struct Issue(
        int Index,
        string StopName,
        string Time,
        string PreviousStopName,
        string PreviousTime);

    public static bool IsEarlierThanPrevious(string? previousTime, string? currentTime)
    {
        if (!RouteScheduleTimeCalculator.TryParseTime(previousTime, out var previous) ||
            !RouteScheduleTimeCalculator.TryParseTime(currentTime, out var current))
        {
            return false;
        }

        if (current >= previous)
        {
            return false;
        }

        var previousMinutes = previous.Hour * 60 + previous.Minute;
        var currentMinutes = current.Hour * 60 + current.Minute;
        var overnightForward = (24 * 60 - previousMinutes) + currentMinutes;
        if (overnightForward > 0 && overnightForward <= MaxOvernightForwardHours * 60)
        {
            return false;
        }

        return true;
    }

    public static IReadOnlyList<Issue> FindIssues(IReadOnlyList<RouteStopItem> stops)
    {
        var issues = new List<Issue>();
        if (stops.Count < 2)
        {
            return issues;
        }

        string? previousName = null;
        string? previousTime = null;

        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            if (stop.IsWaypoint)
            {
                continue;
            }

            var time = stop.Time?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(time) ||
                !RouteScheduleTimeCalculator.TryParseTime(time, out _))
            {
                continue;
            }

            if (previousTime is not null &&
                IsEarlierThanPrevious(previousTime, time))
            {
                issues.Add(new Issue(
                    i,
                    string.IsNullOrWhiteSpace(stop.Name) ? "Haltestelle" : stop.Name.Trim(),
                    RouteScheduleTimeCalculator.NormalizeTimeInput(time),
                    previousName ?? "vorherige Haltestelle",
                    RouteScheduleTimeCalculator.NormalizeTimeInput(previousTime)));
            }

            previousName = string.IsNullOrWhiteSpace(stop.Name) ? "Haltestelle" : stop.Name.Trim();
            previousTime = time;
        }

        return issues;
    }

    public static bool HasIssueForStop(IReadOnlyList<RouteStopItem> stops, RouteStopItem stop)
    {
        var index = -1;
        for (var i = 0; i < stops.Count; i++)
        {
            if (ReferenceEquals(stops[i], stop))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        return FindIssues(stops).Any(issue => issue.Index == index);
    }

    public static string FormatWarningMessage(IReadOnlyList<Issue> issues)
    {
        if (issues.Count == 0)
        {
            return string.Empty;
        }

        var lines = issues
            .Take(5)
            .Select(issue =>
                $"„{issue.StopName}“ ({issue.Time}) liegt vor „{issue.PreviousStopName}“ ({issue.PreviousTime}).");
        var suffix = issues.Count > 5 ? $" … (+{issues.Count - 5} weitere)" : string.Empty;
        return "Uhrzeit-Reihenfolge prüfen: " + string.Join(" ", lines) + suffix;
    }
}
