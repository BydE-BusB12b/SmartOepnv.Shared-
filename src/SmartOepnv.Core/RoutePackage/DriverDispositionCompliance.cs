namespace SmartOepnv.Core.RoutePackage;

/// <summary>FPersV / Lenk- und Ruhezeiten für die Fahrerdisposition.</summary>
public static class DriverDispositionCompliance
{
    public const int MinimumRestHours = 11;

    public const int ReducedRestHours = 9;

    public const int MaxReducedRestPerCalendarWeek = 2;

    public const int MaxDailyDrivingHours = 9;

    public const int MaxDailyDrivingExtendedHours = 10;

    public const int MaxExtendedDrivingDaysPerCalendarWeek = 2;

    public const int MaxWeeklyDrivingHours = 56;

    public const int MaxFortnightlyDrivingHours = 90;

    public const int MinimumWeeklyRestHours = 24;

    public const int RegularWeeklyRestHours = 45;

    public const int MaxReducedWeeklyRestBetweenRegular = 3;

    public const int FortnightDays = 14;

    private static readonly long MinimumRestMs = HoursToMs(MinimumRestHours);

    private static readonly long ReducedRestMs = HoursToMs(ReducedRestHours);

    private static readonly long MinimumWeeklyRestMs = HoursToMs(MinimumWeeklyRestHours);

    private static readonly long RegularWeeklyRestMs = HoursToMs(RegularWeeklyRestHours);

    public const string OverlapMessage = "Fahrer ist in diesem Zeitraum bereits verplant.";

    public const string RestPeriodMessage =
        "Mindest-Ruhezeit von 11 Stunden zwischen Diensten nicht eingehalten (FPersV / Lenk- und Ruhezeiten).";

    public const string ReducedRestQuotaMessage =
        "Ruhezeit-Verkürzung auf 9 Stunden ist in dieser Kalenderwoche bereits zweimal genutzt.";

    public const string DailyDrivingMessage =
        "Tägliche Lenkzeit überschreitet 9 Stunden (max. 10 Stunden an höchstens 2 Tagen pro Kalenderwoche).";

    public const string ExtendedDrivingQuotaMessage =
        "Lenkzeit-Verlängerung auf 10 Stunden ist in dieser Kalenderwoche bereits zweimal genutzt.";

    public const string WeeklyDrivingMessage =
        "Wöchentliche Lenkzeit überschreitet 56 Stunden (FPersV).";

    public const string FortnightlyDrivingMessage =
        "Lenkzeit überschreitet 90 Stunden in zwei aufeinanderfolgenden Kalenderwochen (FPersV).";

    public const string WeeklyRestMessage =
        "Wochenruhe von mindestens 24 Stunden in der Kalenderwoche nicht eingehalten (FPersV).";

    public const string RegularWeeklyRestMessage =
        "Reguläre Wochenruhe von 45 Stunden in 14 Tagen nicht eingehalten (FPersV).";

    public const string ReducedWeeklyRestQuotaMessage =
        "Wochenruhe-Verkürzung auf 24 Stunden ist bis zur nächsten 45-h-Ruhe bereits dreimal genutzt.";

    public const string DrivingTimeAssumptionHint =
        "Lenkzeit wird als Dienstzeit (von–bis) angenommen, sofern keine gesonderte Lenkzeit erfasst ist.";

    public static bool TryValidate(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        long startEpochMs,
        long endEpochMs,
        string? excludeAssignmentId,
        bool requestReducedRest,
        bool requestExtendedDriving,
        bool requestReducedWeeklyRest,
        out bool appliedReducedRest,
        out bool appliedExtendedDriving,
        out bool appliedReducedWeeklyRest,
        out string errorMessage,
        long part1EndEpochMs = 0,
        long part2StartEpochMs = 0)
    {
        appliedReducedRest = false;
        appliedExtendedDriving = false;
        appliedReducedWeeklyRest = false;
        errorMessage = string.Empty;
        if (string.IsNullOrEmpty(driverKey))
        {
            return true;
        }

        var isSplit = part1EndEpochMs > 0 && part2StartEpochMs > part1EndEpochMs;
        if (isSplit)
        {
            if (!SplitShiftCompliance.TryValidateStructure(
                    startEpochMs,
                    part1EndEpochMs,
                    part2StartEpochMs,
                    endEpochMs,
                    out errorMessage))
            {
                return false;
            }
        }

        var relevant = GetDriverAssignments(assignments, driverKey, excludeAssignmentId).ToList();
        var candidate = new DriverDispositionAssignment
        {
            Id = excludeAssignmentId ?? "__candidate__",
            DriverKey = driverKey,
            StartEpochMs = startEpochMs,
            EndEpochMs = endEpochMs,
            Part1EndEpochMs = isSplit ? part1EndEpochMs : 0,
            Part2StartEpochMs = isSplit ? part2StartEpochMs : 0
        };

        foreach (var other in relevant)
        {
            if (other.StartEpochMs < endEpochMs && other.EndEpochMs > startEpochMs)
            {
                errorMessage = OverlapMessage;
                return false;
            }

            if (other.EndEpochMs <= startEpochMs)
            {
                var gapMs = startEpochMs - other.EndEpochMs;
                if (!IsPredecessorGapValid(
                        assignments,
                        driverKey,
                        gapMs,
                        startEpochMs,
                        excludeAssignmentId,
                        requestReducedRest,
                        out var usesReduced))
                {
                    var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(startEpochMs).LocalDateTime;
                    errorMessage = requestReducedRest &&
                                   gapMs >= ReducedRestMs &&
                                   gapMs < MinimumRestMs &&
                                   !CanApplyReducedRest(assignments, driverKey, startLocal, excludeAssignmentId)
                        ? ReducedRestQuotaMessage
                        : RestPeriodMessage;
                    return false;
                }

                if (usesReduced)
                {
                    appliedReducedRest = true;
                }
            }
            else if (endEpochMs <= other.StartEpochMs)
            {
                var gapMs = other.StartEpochMs - endEpochMs;
                if (!IsSuccessorGapValid(other, gapMs))
                {
                    errorMessage = RestPeriodMessage;
                    return false;
                }
            }
        }

        var timeline = MergeCandidate(relevant, candidate, excludeAssignmentId);
        if (!ValidateDrivingTimes(
                timeline,
                driverKey,
                startEpochMs,
                endEpochMs,
                excludeAssignmentId,
                requestExtendedDriving,
                out appliedExtendedDriving,
                out errorMessage))
        {
            return false;
        }

        if (!ValidateWeeklyRest(
                timeline,
                driverKey,
                startEpochMs,
                endEpochMs,
                requestReducedWeeklyRest,
                out appliedReducedWeeklyRest,
                out errorMessage))
        {
            return false;
        }

        return true;
    }

    public static string BuildComplianceSummary(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        long? startEpochMs,
        long? endEpochMs,
        string? excludeAssignmentId,
        long part1EndEpochMs = 0,
        long part2StartEpochMs = 0)
    {
        if (string.IsNullOrEmpty(driverKey))
        {
            return DrivingTimeAssumptionHint;
        }

        var relevant = GetDriverAssignments(assignments, driverKey, excludeAssignmentId).ToList();
        if (startEpochMs is not null && endEpochMs is not null)
        {
            var isSplit = part1EndEpochMs > 0 && part2StartEpochMs > part1EndEpochMs;
            relevant = MergeCandidate(
                relevant,
                new DriverDispositionAssignment
                {
                    Id = excludeAssignmentId ?? "__preview__",
                    DriverKey = driverKey,
                    StartEpochMs = startEpochMs.Value,
                    EndEpochMs = endEpochMs.Value,
                    Part1EndEpochMs = isSplit ? part1EndEpochMs : 0,
                    Part2StartEpochMs = isSplit ? part2StartEpochMs : 0
                },
                excludeAssignmentId);
        }

        if (relevant.Count == 0)
        {
            return DrivingTimeAssumptionHint;
        }

        var reference = DateTimeOffset.FromUnixTimeMilliseconds(
            startEpochMs ?? relevant.Max(a => a.EndEpochMs)).LocalDateTime;
        var weekStart = GetCalendarWeekStart(reference.Date);
        var weekEnd = weekStart.AddDays(7);
        var prevWeekStart = weekStart.AddDays(-7);

        var weekMs = SumDrivingMsInRange(relevant, weekStart, weekEnd);
        var fortnightMs = SumDrivingMsInRange(relevant, prevWeekStart, weekEnd);
        var dayMs = SumDrivingMsOnDay(relevant, reference.Date);
        var longestWeekRest = GetLongestRestGapMs(relevant, weekStart, weekEnd);
        var longestFortnightRest = GetLongestRestGapMs(
            relevant,
            reference.Date.AddDays(-(FortnightDays - 1)),
            reference.Date.AddDays(1));

        return
            $"{DrivingTimeAssumptionHint} " +
            $"Lenkzeit Tag: {FormatHours(dayMs)}/{MaxDailyDrivingHours} h · " +
            $"Woche: {FormatHours(weekMs)}/{MaxWeeklyDrivingHours} h · " +
            $"2 Wochen: {FormatHours(fortnightMs)}/{MaxFortnightlyDrivingHours} h · " +
            $"Längste Ruhe (Woche): {FormatHours(longestWeekRest)} h · " +
            $"Längste Ruhe (14 Tage): {FormatHours(longestFortnightRest)} h (min. {RegularWeeklyRestHours} h).";
    }

    public static DateTime GetEarliestAllowedStart(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        DateTime notBeforeLocal,
        bool useReducedRest,
        string? excludeAssignmentId)
    {
        if (string.IsNullOrEmpty(driverKey))
        {
            return notBeforeLocal;
        }

        if (!useReducedRest || !CanApplyReducedRest(assignments, driverKey, notBeforeLocal, excludeAssignmentId))
        {
            useReducedRest = false;
        }

        var restMs = useReducedRest ? ReducedRestMs : MinimumRestMs;
        var earliestMs = new DateTimeOffset(notBeforeLocal).ToUnixTimeMilliseconds();
        var endOfSelectedDayMs = new DateTimeOffset(notBeforeLocal.Date.AddDays(1)).ToUnixTimeMilliseconds();

        foreach (var assignment in GetDriverAssignments(assignments, driverKey, excludeAssignmentId))
        {
            if (assignment.StartEpochMs >= endOfSelectedDayMs)
            {
                continue;
            }

            earliestMs = Math.Max(earliestMs, assignment.EndEpochMs + restMs);
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(earliestMs).LocalDateTime;
    }

    public static int CountReducedRestUsesInCalendarWeek(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        DateTime referenceLocal,
        string? excludeAssignmentId)
    {
        var weekStart = GetCalendarWeekStart(referenceLocal.Date);
        var weekEnd = weekStart.AddDays(7);
        var weekStartMs = new DateTimeOffset(weekStart).ToUnixTimeMilliseconds();
        var weekEndMs = new DateTimeOffset(weekEnd).ToUnixTimeMilliseconds();

        return assignments.Count(a =>
            string.Equals(a.DriverKey, driverKey, StringComparison.Ordinal) &&
            a.ReducedRestBefore &&
            (excludeAssignmentId is null || !string.Equals(a.Id, excludeAssignmentId, StringComparison.Ordinal)) &&
            a.StartEpochMs >= weekStartMs &&
            a.StartEpochMs < weekEndMs);
    }

    public static int CountExtendedDrivingDaysInCalendarWeek(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        DateTime referenceLocal,
        string? excludeAssignmentId)
    {
        var weekStart = GetCalendarWeekStart(referenceLocal.Date);
        var weekEnd = weekStart.AddDays(7);
        var weekStartMs = new DateTimeOffset(weekStart).ToUnixTimeMilliseconds();
        var weekEndMs = new DateTimeOffset(weekEnd).ToUnixTimeMilliseconds();

        return assignments.Count(a =>
            string.Equals(a.DriverKey, driverKey, StringComparison.Ordinal) &&
            a.ExtendedDrivingDay &&
            (excludeAssignmentId is null || !string.Equals(a.Id, excludeAssignmentId, StringComparison.Ordinal)) &&
            a.StartEpochMs >= weekStartMs &&
            a.StartEpochMs < weekEndMs);
    }

    public static bool CanApplyReducedRest(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        DateTime referenceLocal,
        string? excludeAssignmentId) =>
        CountReducedRestUsesInCalendarWeek(assignments, driverKey, referenceLocal, excludeAssignmentId) <
        MaxReducedRestPerCalendarWeek;

    public static bool CanApplyExtendedDriving(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        DateTime referenceLocal,
        string? excludeAssignmentId) =>
        CountExtendedDrivingDaysInCalendarWeek(assignments, driverKey, referenceLocal, excludeAssignmentId) <
        MaxExtendedDrivingDaysPerCalendarWeek;

    public static bool CanApplyReducedWeeklyRest(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        long referenceEpochMs,
        string? excludeAssignmentId) =>
        CountReducedWeeklyRestSinceLastRegular(assignments, driverKey, referenceEpochMs, excludeAssignmentId) <
        MaxReducedWeeklyRestBetweenRegular;

    public static DateTime GetCalendarWeekStart(DateTime date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-diff);
    }

    private static bool ValidateDrivingTimes(
        IReadOnlyList<DriverDispositionAssignment> timeline,
        string driverKey,
        long startEpochMs,
        long endEpochMs,
        string? excludeAssignmentId,
        bool requestExtendedDriving,
        out bool appliedExtendedDriving,
        out string errorMessage)
    {
        appliedExtendedDriving = false;
        errorMessage = string.Empty;

        var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(startEpochMs).LocalDateTime;
        var endLocal = DateTimeOffset.FromUnixTimeMilliseconds(endEpochMs).LocalDateTime;
        var needsExtended = false;

        foreach (var day in EnumerateDays(startLocal.Date, endLocal.Date))
        {
            var dayMs = SumDrivingMsOnDay(timeline, day);
            var dayHours = MsToHours(dayMs);
            if (dayHours > MaxDailyDrivingExtendedHours)
            {
                errorMessage = DailyDrivingMessage;
                return false;
            }

            if (dayHours > MaxDailyDrivingHours)
            {
                needsExtended = true;
                if (!requestExtendedDriving)
                {
                    errorMessage = DailyDrivingMessage;
                    return false;
                }

                if (!CanApplyExtendedDriving(timeline, driverKey, day, excludeAssignmentId))
                {
                    errorMessage = ExtendedDrivingQuotaMessage;
                    return false;
                }
            }
        }

        if (needsExtended)
        {
            appliedExtendedDriving = true;
        }

        var weekStart = GetCalendarWeekStart(startLocal.Date);
        var weekEnd = weekStart.AddDays(7);
        if (MsToHours(SumDrivingMsInRange(timeline, weekStart, weekEnd)) > MaxWeeklyDrivingHours)
        {
            errorMessage = WeeklyDrivingMessage;
            return false;
        }

        var prevWeekStart = weekStart.AddDays(-7);
        if (MsToHours(SumDrivingMsInRange(timeline, prevWeekStart, weekEnd)) > MaxFortnightlyDrivingHours)
        {
            errorMessage = FortnightlyDrivingMessage;
            return false;
        }

        return true;
    }

    private static bool ValidateWeeklyRest(
        IReadOnlyList<DriverDispositionAssignment> timeline,
        string driverKey,
        long startEpochMs,
        long endEpochMs,
        bool requestReducedWeeklyRest,
        out bool appliedReducedWeeklyRest,
        out string errorMessage)
    {
        appliedReducedWeeklyRest = false;
        errorMessage = string.Empty;

        var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(startEpochMs).LocalDateTime;
        var endLocal = DateTimeOffset.FromUnixTimeMilliseconds(endEpochMs).LocalDateTime;

        foreach (var weekStart in EnumerateWeeks(startLocal.Date, endLocal.Date))
        {
            var weekEnd = weekStart.AddDays(7);
            var longestGap = GetLongestRestGapMs(timeline, weekStart, weekEnd);
            if (longestGap < MinimumWeeklyRestMs)
            {
                errorMessage = WeeklyRestMessage;
                return false;
            }
        }

        var fortnightStart = endLocal.Date.AddDays(-(FortnightDays - 1));
        var fortnightEnd = endLocal.Date.AddDays(1);
        var longestFortnightGap = GetLongestRestGapMs(timeline, fortnightStart, fortnightEnd);
        if (longestFortnightGap >= RegularWeeklyRestMs)
        {
            return true;
        }

        if (longestFortnightGap < MinimumWeeklyRestMs)
        {
            errorMessage = RegularWeeklyRestMessage;
            return false;
        }

        if (!requestReducedWeeklyRest)
        {
            errorMessage = RegularWeeklyRestMessage;
            return false;
        }

        if (!CanApplyReducedWeeklyRest(timeline, driverKey, startEpochMs, null))
        {
            errorMessage = ReducedWeeklyRestQuotaMessage;
            return false;
        }

        if (!GapBeforeShiftQualifiesAsReducedWeeklyRest(timeline, startEpochMs))
        {
            errorMessage = RegularWeeklyRestMessage;
            return false;
        }

        appliedReducedWeeklyRest = true;
        return true;
    }

    private static bool GapBeforeShiftQualifiesAsReducedWeeklyRest(
        IReadOnlyList<DriverDispositionAssignment> timeline,
        long shiftStartEpochMs)
    {
        var predecessor = timeline
            .Where(a => a.EndEpochMs <= shiftStartEpochMs)
            .OrderByDescending(a => a.EndEpochMs)
            .FirstOrDefault();
        var gapMs = predecessor is null
            ? shiftStartEpochMs - new DateTimeOffset(
                DateTimeOffset.FromUnixTimeMilliseconds(shiftStartEpochMs).LocalDateTime.Date).ToUnixTimeMilliseconds()
            : shiftStartEpochMs - predecessor.EndEpochMs;

        return gapMs >= MinimumWeeklyRestMs && gapMs < RegularWeeklyRestMs;
    }

    private static int CountReducedWeeklyRestSinceLastRegular(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        long referenceEpochMs,
        string? excludeAssignmentId)
    {
        var relevant = GetDriverAssignments(assignments, driverKey, excludeAssignmentId)
            .Where(a => a.StartEpochMs <= referenceEpochMs)
            .OrderBy(a => a.StartEpochMs)
            .ToList();

        var lastRegularMs = FindLastRegularWeeklyRestEndMs(relevant, referenceEpochMs);
        return relevant.Count(a =>
            a.ReducedWeeklyRestBefore &&
            a.StartEpochMs > lastRegularMs);
    }

    private static long FindLastRegularWeeklyRestEndMs(
        IReadOnlyList<DriverDispositionAssignment> timeline,
        long beforeEpochMs)
    {
        if (timeline.Count == 0)
        {
            return 0;
        }

        var rangeStart = DateTimeOffset.FromUnixTimeMilliseconds(timeline[0].StartEpochMs).LocalDateTime.Date
            .AddDays(-FortnightDays);
        var gaps = BuildRestGaps(
            timeline.Where(a => a.StartEpochMs < beforeEpochMs).ToList(),
            rangeStart,
            DateTimeOffset.FromUnixTimeMilliseconds(beforeEpochMs).LocalDateTime);

        long lastRegularEnd = 0;
        foreach (var gap in gaps.OrderBy(g => g.StartMs))
        {
            if (gap.DurationMs >= RegularWeeklyRestMs)
            {
                lastRegularEnd = gap.EndMs;
            }
        }

        return lastRegularEnd;
    }

    private static long GetLongestRestGapMs(
        IReadOnlyList<DriverDispositionAssignment> timeline,
        DateTime rangeStartLocal,
        DateTime rangeEndLocalExclusive)
    {
        var gaps = BuildRestGaps(timeline, rangeStartLocal, rangeEndLocalExclusive);
        return gaps.Count == 0 ? 0 : gaps.Max(g => g.DurationMs);
    }

    private static List<RestGap> BuildRestGaps(
        IReadOnlyList<DriverDispositionAssignment> timeline,
        DateTime rangeStartLocal,
        DateTime rangeEndLocalExclusive)
    {
        var rangeStartMs = new DateTimeOffset(rangeStartLocal).ToUnixTimeMilliseconds();
        var rangeEndMs = new DateTimeOffset(rangeEndLocalExclusive).ToUnixTimeMilliseconds();
        var shifts = timeline
            .Where(a => a.EndEpochMs > rangeStartMs && a.StartEpochMs < rangeEndMs)
            .OrderBy(a => a.StartEpochMs)
            .ToList();

        var gaps = new List<RestGap>();
        if (shifts.Count == 0)
        {
            gaps.Add(new RestGap(rangeStartMs, rangeEndMs));
            return gaps;
        }

        var firstStart = Math.Max(shifts[0].StartEpochMs, rangeStartMs);
        if (firstStart > rangeStartMs)
        {
            gaps.Add(new RestGap(rangeStartMs, firstStart));
        }

        for (var i = 0; i < shifts.Count - 1; i++)
        {
            var gapStart = Math.Max(shifts[i].EndEpochMs, rangeStartMs);
            var gapEnd = Math.Min(shifts[i + 1].StartEpochMs, rangeEndMs);
            if (gapEnd > gapStart)
            {
                gaps.Add(new RestGap(gapStart, gapEnd));
            }
        }

        var lastEnd = Math.Min(shifts[^1].EndEpochMs, rangeEndMs);
        if (rangeEndMs > lastEnd)
        {
            gaps.Add(new RestGap(lastEnd, rangeEndMs));
        }

        return gaps;
    }

    private static long SumDrivingMsInRange(
        IReadOnlyList<DriverDispositionAssignment> timeline,
        DateTime rangeStartLocal,
        DateTime rangeEndLocalExclusive)
    {
        long total = 0;
        foreach (var day in EnumerateDays(rangeStartLocal.Date, rangeEndLocalExclusive.AddDays(-1).Date))
        {
            total += SumDrivingMsOnDay(timeline, day);
        }

        return total;
    }

    private static long SumDrivingMsOnDay(IReadOnlyList<DriverDispositionAssignment> timeline, DateTime day)
    {
        long total = 0;
        foreach (var assignment in timeline)
        {
            total += SplitShiftCompliance.SumWorkMsOnDay(assignment, day);
        }

        return total;
    }

    private static IEnumerable<DateTime> EnumerateDays(DateTime startDate, DateTime endDate)
    {
        for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    private static IEnumerable<DateTime> EnumerateWeeks(DateTime startDate, DateTime endDate)
    {
        var week = GetCalendarWeekStart(startDate);
        var lastWeek = GetCalendarWeekStart(endDate);
        while (week <= lastWeek)
        {
            yield return week;
            week = week.AddDays(7);
        }
    }

    private static List<DriverDispositionAssignment> MergeCandidate(
        IReadOnlyList<DriverDispositionAssignment> relevant,
        DriverDispositionAssignment candidate,
        string? excludeAssignmentId)
    {
        var timeline = relevant
            .Where(a => excludeAssignmentId is null || !string.Equals(a.Id, excludeAssignmentId, StringComparison.Ordinal))
            .ToList();
        timeline.Add(candidate);
        return timeline.OrderBy(a => a.StartEpochMs).ToList();
    }

    private static IEnumerable<DriverDispositionAssignment> GetDriverAssignments(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        string? excludeAssignmentId) =>
        assignments
            .Where(a => string.Equals(a.DriverKey, driverKey, StringComparison.Ordinal))
            .Where(a => excludeAssignmentId is null || !string.Equals(a.Id, excludeAssignmentId, StringComparison.Ordinal))
            .OrderBy(a => a.StartEpochMs);

    private static bool IsPredecessorGapValid(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        long gapMs,
        long newStartEpochMs,
        string? excludeAssignmentId,
        bool requestReducedRest,
        out bool usesReducedRest)
    {
        usesReducedRest = false;
        if (gapMs >= MinimumRestMs)
        {
            return true;
        }

        if (gapMs < ReducedRestMs || !requestReducedRest)
        {
            return false;
        }

        var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(newStartEpochMs).LocalDateTime;
        if (!CanApplyReducedRest(assignments, driverKey, startLocal, excludeAssignmentId))
        {
            return false;
        }

        usesReducedRest = true;
        return true;
    }

    private static bool IsSuccessorGapValid(DriverDispositionAssignment successor, long gapMs)
    {
        if (gapMs >= MinimumRestMs)
        {
            return true;
        }

        return gapMs >= ReducedRestMs && successor.ReducedRestBefore;
    }

    private static long HoursToMs(int hours) => hours * 60L * 60L * 1000L;

    private static double MsToHours(long ms) => ms / (60d * 60d * 1000d);

    private static string FormatHours(long ms) => MsToHours(ms).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

    private readonly record struct RestGap(long StartMs, long EndMs)
    {
        public long DurationMs => EndMs - StartMs;
    }
}
