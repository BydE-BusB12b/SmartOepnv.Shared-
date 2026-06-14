namespace SmartOepnv.Core.Dienstvorlagen;

public static class DutyTemplateEmptyRunInserter
{
    public const string EmptyRunFromStopLabel = "Leerfahrt nach";

    public static (List<DutyTemplateRow> Rows, int InsertedCount) InsertEmptyRuns(
        IReadOnlyList<DutyTemplateRow> rows,
        IReadOnlyList<DutyTemplateEmptyRunRule> rules,
        string leerfahrtRemark,
        string lineCourse)
    {
        var validRules = rules.Where(rule => rule.IsValid).ToList();
        if (validRules.Count == 0 || rows.Count < 2)
        {
            return (rows.ToList(), 0);
        }

        var ordered = DutyTemplateCalculator.OrderRows(rows);
        var spansMidnight = DutyTemplateCalculator.SpansOperatingDayMidnight(ordered);
        var result = new List<DutyTemplateRow>();
        var inserted = 0;

        foreach (var current in ordered)
        {
            if (IsRegularDrivingTrip(current))
            {
                var previous = FindPreviousDrivingTrip(result);
                if (previous is not null
                    && !HasMatchingEmptyRunBetween(result, previous, current)
                    && TryFindMatchingRule(previous, current, validRules) is { } rule
                    && TryComputeEmptyRunTimes(
                        previous,
                        current,
                        rule.DurationMinutes,
                        spansMidnight,
                        out var fromMinutes,
                        out var toMinutes))
                {
                    result.Add(CreateEmptyRun(
                        current,
                        rule,
                        leerfahrtRemark,
                        lineCourse,
                        fromMinutes,
                        toMinutes));
                    inserted++;
                }
            }

            result.Add(current);
        }

        return (result, inserted);
    }

    public static EmptyRunMatchDiagnostics AnalyzeMisses(
        IReadOnlyList<DutyTemplateRow> rows,
        IReadOnlyList<DutyTemplateEmptyRunRule> rules)
    {
        var validRules = rules.Where(rule => rule.IsValid).ToList();
        var diagnostics = new EmptyRunMatchDiagnostics();
        if (validRules.Count == 0 || rows.Count < 2)
        {
            return diagnostics;
        }

        var ordered = DutyTemplateCalculator.OrderRows(rows);
        var spansMidnight = DutyTemplateCalculator.SpansOperatingDayMidnight(ordered);
        DutyTemplateRow? previous = null;

        foreach (var current in ordered)
        {
            if (!IsRegularDrivingTrip(current))
            {
                continue;
            }

            if (previous is null)
            {
                previous = current;
                continue;
            }

            foreach (var rule in validRules)
            {
                if (!DutyTemplateStopNameHelper.StopsMatch(previous.ToStop, rule.FromStop)
                    || !DutyTemplateStopNameHelper.StopsMatch(current.FromStop, rule.ToStop))
                {
                    continue;
                }

                diagnostics.StopMatches++;
                if (TryComputeEmptyRunTimes(previous, current, rule.DurationMinutes, spansMidnight, out _, out _))
                {
                    diagnostics.ReadyMatches++;
                }
                else
                {
                    diagnostics.TimeTooShort++;
                    var gap = ComputeGapMinutes(previous, current, spansMidnight);
                    if (gap is not null && (diagnostics.ShortestGapMinutes is null || gap < diagnostics.ShortestGapMinutes))
                    {
                        diagnostics.ShortestGapMinutes = gap;
                    }
                }
            }

            previous = current;
        }

        return diagnostics;
    }

    private static bool IsRegularDrivingTrip(DutyTemplateRow row) =>
        DutyTemplateCalculator.IsDrivingRow(row)
        && !DutyTemplateRemarkHelper.IsLeerfahrt(row.Remark)
        && !DutyTemplateRemarkHelper.IsLeerzeile(row.Remark);

    private static DutyTemplateRow? FindPreviousDrivingTrip(IReadOnlyList<DutyTemplateRow> rows)
    {
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (IsRegularDrivingTrip(rows[index]))
            {
                return rows[index];
            }
        }

        return null;
    }

    private static bool HasMatchingEmptyRunBetween(
        IReadOnlyList<DutyTemplateRow> rows,
        DutyTemplateRow previous,
        DutyTemplateRow current)
    {
        var previousIndex = -1;
        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(rows[index].Id, previous.Id, StringComparison.Ordinal))
            {
                previousIndex = index;
                break;
            }
        }

        if (previousIndex < 0)
        {
            return false;
        }

        for (var index = previousIndex + 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!DutyTemplateRemarkHelper.IsLeerfahrt(row.Remark))
            {
                continue;
            }

            if (DutyTemplateStopNameHelper.StopsMatch(row.ToStop, current.FromStop))
            {
                return true;
            }
        }

        return false;
    }

    private static DutyTemplateEmptyRunRule? TryFindMatchingRule(
        DutyTemplateRow previous,
        DutyTemplateRow current,
        IReadOnlyList<DutyTemplateEmptyRunRule> rules)
    {
        foreach (var rule in rules)
        {
            if (DutyTemplateStopNameHelper.StopsMatch(previous.ToStop, rule.FromStop)
                && DutyTemplateStopNameHelper.StopsMatch(current.FromStop, rule.ToStop))
            {
                return rule;
            }
        }

        return null;
    }

    private static bool TryComputeEmptyRunTimes(
        DutyTemplateRow previous,
        DutyTemplateRow current,
        int durationMinutes,
        bool spansMidnight,
        out int fromMinutes,
        out int toMinutes)
    {
        fromMinutes = 0;
        toMinutes = 0;

        var previousEnd = DutyTemplateCalculator.ParseMinutes(previous.ToTime);
        var nextStart = DutyTemplateCalculator.ParseMinutes(current.FromTime);
        if (previousEnd is null || nextStart is null || durationMinutes <= 0)
        {
            return false;
        }

        var previousEndKey = DutyTemplateCalculator.ToOperatingDaySortKey(previousEnd.Value, spansMidnight);
        var nextStartKey = DutyTemplateCalculator.ToOperatingDaySortKey(nextStart.Value, spansMidnight);
        var gap = nextStartKey - previousEndKey;
        if (gap < durationMinutes)
        {
            return false;
        }

        var toKey = nextStartKey;
        var fromKey = toKey - durationMinutes;
        if (fromKey < previousEndKey)
        {
            fromKey = previousEndKey;
            toKey = previousEndKey + durationMinutes;
        }

        if (toKey > nextStartKey)
        {
            return false;
        }

        fromMinutes = FromOperatingDaySortKey(fromKey);
        toMinutes = FromOperatingDaySortKey(toKey);
        return true;
    }

    private static int? ComputeGapMinutes(
        DutyTemplateRow previous,
        DutyTemplateRow current,
        bool spansMidnight)
    {
        var previousEnd = DutyTemplateCalculator.ParseMinutes(previous.ToTime);
        var nextStart = DutyTemplateCalculator.ParseMinutes(current.FromTime);
        if (previousEnd is null || nextStart is null)
        {
            return null;
        }

        var previousEndKey = DutyTemplateCalculator.ToOperatingDaySortKey(previousEnd.Value, spansMidnight);
        var nextStartKey = DutyTemplateCalculator.ToOperatingDaySortKey(nextStart.Value, spansMidnight);
        return nextStartKey - previousEndKey;
    }

    private static int FromOperatingDaySortKey(int sortKey) =>
        ((sortKey % (24 * 60)) + (24 * 60)) % (24 * 60);

    private static DutyTemplateRow CreateEmptyRun(
        DutyTemplateRow current,
        DutyTemplateEmptyRunRule rule,
        string leerfahrtRemark,
        string lineCourse,
        int fromMinutes,
        int toMinutes) =>
        new()
        {
            Remark = leerfahrtRemark,
            LineCourse = lineCourse.Trim(),
            FromTime = DutyTemplateCalculator.FormatClockDisplay(fromMinutes),
            FromStop = EmptyRunFromStopLabel,
            ToTime = DutyTemplateCalculator.FormatClockDisplay(toMinutes),
            ToStop = DutyTemplateStopNameHelper.StripHaltestelleMarker(current.FromStop)
        };
}

public sealed class EmptyRunMatchDiagnostics
{
    public int StopMatches { get; set; }

    public int ReadyMatches { get; set; }

    public int TimeTooShort { get; set; }

    public int? ShortestGapMinutes { get; set; }
}
