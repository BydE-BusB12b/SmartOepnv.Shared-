namespace SmartOepnv.Core.Zeitwirtschaft;

/// <summary>
/// Pausen und Grenzen nach dem Arbeitszeitgesetz.
/// Ruhepausen sind keine Arbeitszeit (§ 2). Die Mindestdauer hängt von der Arbeitszeit ab (§ 4):
/// über 6 Stunden 30 Minuten, über 9 Stunden 45 Minuten, in Abschnitten von mindestens 15 Minuten.
/// Im Verkehr darf die Ruhezeit auf 10 Stunden verkürzt werden, wenn sie ausgeglichen wird (§ 5 Abs. 2).
/// </summary>
public static class ZeitwirtschaftArbzg
{
    public const int QualifyingSegmentMinutes = 15;
    public const int BreakFrom6HoursMinutes = 30;
    public const int BreakFrom9HoursMinutes = 45;
    public const int MaxContinuousMinutes = 6 * 60;
    public const int StandardDayMinutes = 8 * 60;
    public const int AbsoluteDayMinutes = 10 * 60;
    public const int FullRestMinutes = 11 * 60;
    public const int TransportRestMinutes = 10 * 60;

    public readonly record struct Interval(long StartMs, long EndMs);

    public static List<Interval> ClosedIntervals(
        long shiftStartMs,
        long shiftEndMs,
        IReadOnlyList<ZeitwirtschaftPause> pauses)
    {
        if (shiftEndMs <= shiftStartMs || pauses.Count == 0)
        {
            return [];
        }

        var raw = new List<Interval>();
        foreach (var pause in pauses)
        {
            if (pause.StartEpochMs <= 0)
            {
                continue;
            }

            if (pause.EndEpochMs is not > 0)
            {
                continue;
            }

            var start = pause.StartEpochMs;
            var end = pause.EndEpochMs.Value;
            if (end <= shiftStartMs || start >= shiftEndMs || end <= start)
            {
                continue;
            }

            if (start < shiftStartMs)
            {
                start = shiftStartMs;
            }

            if (end > shiftEndMs)
            {
                end = shiftEndMs;
            }

            if (end > start)
            {
                raw.Add(new Interval(start, end));
            }
        }

        if (raw.Count == 0)
        {
            return [];
        }

        raw.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        var merged = new List<Interval>();
        var current = raw[0];
        for (var i = 1; i < raw.Count; i++)
        {
            var next = raw[i];
            if (next.StartMs <= current.EndMs)
            {
                current = new Interval(current.StartMs, Math.Max(current.EndMs, next.EndMs));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    public static int MinutesOf(IReadOnlyList<Interval> intervals)
    {
        var sum = 0;
        foreach (var interval in intervals)
        {
            sum += ZeitwirtschaftMergeService.WholeMinutesBetween(interval.StartMs, interval.EndMs);
        }

        return sum;
    }

    public static int QualifyingMinutes(IReadOnlyList<Interval> intervals)
    {
        var sum = 0;
        foreach (var interval in intervals)
        {
            var minutes = ZeitwirtschaftMergeService.WholeMinutesBetween(interval.StartMs, interval.EndMs);
            if (minutes >= QualifyingSegmentMinutes)
            {
                sum += minutes;
            }
        }

        return sum;
    }

    public static List<Interval> QualifyingIntervals(IReadOnlyList<Interval> intervals)
    {
        var list = new List<Interval>();
        foreach (var interval in intervals)
        {
            var minutes = ZeitwirtschaftMergeService.WholeMinutesBetween(interval.StartMs, interval.EndMs);
            if (minutes >= QualifyingSegmentMinutes)
            {
                list.Add(interval);
            }
        }

        return list;
    }

    public static int LongestWorkStretchMinutes(
        long shiftStartMs,
        long shiftEndMs,
        IReadOnlyList<Interval> qualifyingBreaks)
    {
        var cursor = shiftStartMs;
        var longest = 0;
        foreach (var breakInterval in qualifyingBreaks)
        {
            if (breakInterval.StartMs > cursor)
            {
                longest = Math.Max(
                    longest,
                    ZeitwirtschaftMergeService.WholeMinutesBetween(cursor, breakInterval.StartMs));
            }

            if (breakInterval.EndMs > cursor)
            {
                cursor = breakInterval.EndMs;
            }
        }

        if (shiftEndMs > cursor)
        {
            longest = Math.Max(
                longest,
                ZeitwirtschaftMergeService.WholeMinutesBetween(cursor, shiftEndMs));
        }

        return longest;
    }

    public static int RequiredBreakMinutes(int netWorkMinutes)
    {
        if (netWorkMinutes > 9 * 60)
        {
            return BreakFrom9HoursMinutes;
        }

        if (netWorkMinutes > 6 * 60)
        {
            return BreakFrom6HoursMinutes;
        }

        return 0;
    }

    public static string BreakHint(int netWorkMinutes, int qualifyingMinutes, int longestStretch)
    {
        var parts = new List<string>();
        var required = RequiredBreakMinutes(netWorkMinutes);
        if (required > 0 && qualifyingMinutes < required)
        {
            parts.Add($"Pausenpflicht {required} Min. nicht erfüllt (gültig erfasst {qualifyingMinutes} Min.)");
        }

        if (longestStretch > MaxContinuousMinutes)
        {
            parts.Add("Mehr als 6 Stunden ohne Pause");
        }

        return string.Join("\n", parts);
    }

    public static string DailyHint(int dayNetMinutes)
    {
        if (dayNetMinutes > AbsoluteDayMinutes)
        {
            return "Tagesarbeitszeit über 10 Stunden";
        }

        if (dayNetMinutes > StandardDayMinutes)
        {
            return "Tagesarbeitszeit über 8 Stunden (Ausgleich nötig)";
        }

        return string.Empty;
    }

    public static string RestHint(long gapMs)
    {
        if (gapMs <= 0)
        {
            return string.Empty;
        }

        var minutes = (int)Math.Floor(TimeSpan.FromMilliseconds(Math.Max(0, gapMs)).TotalMinutes);
        if (minutes < TransportRestMinutes)
        {
            return "Ruhezeit unter 10 Stunden";
        }

        if (minutes < FullRestMinutes)
        {
            return "Ruhezeit unter 11 Stunden (Verkehr: 10 Stunden nur mit Ausgleich)";
        }

        return string.Empty;
    }
}
