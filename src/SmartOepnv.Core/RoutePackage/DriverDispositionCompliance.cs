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

    public const int MaxExtendedDailyShiftsPerFortnight = 3;

    public const int StandardMaxSingleShiftHours = 10;

    public const int ExtendedMaxSingleShiftHours = 15;

    public const int MaxSplitServiceShiftHours = SplitShiftRules.MaxServiceShiftHours;

    private static readonly DateTime FortnightEpochMonday = new(2020, 1, 6);

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
        "Tägliche Lenkzeit überschreitet 9 Stunden (max. 10 Stunden an höchstens 2 Tagen pro Kalenderwoche). " +
        "Die 15-h-Dienstschicht-Ausnahme betrifft nur die Dienstschicht, nicht die Lenkzeit – bitte „Lenkzeit bis 10 Stunden“ aktivieren.";

    public const string DailyDrivingExceedsTenMessage =
        "Tägliche Lenkzeit überschreitet 10 Stunden (FPersV-Höchstgrenze, auch mit Ausnahme). " +
        "Prüfen Sie die Zeiten oder die reine Lenkzeit in der Dienstvorlage.";

    public const string TemplateComplianceHintPrefix = "Werte aus Dienstvorlage:";

    public const string ExtendedDrivingQuotaMessage =
        "Lenkzeit-Verlängerung auf 10 Stunden ist in dieser Kalenderwoche bereits zweimal genutzt.";

    public const string ExtendedDailyShiftQuotaMessage =
        "Dienstschicht bis 15 Stunden ist in diesem 2-Wochen-Abschnitt (Mo–So) bereits dreimal genutzt.";

    public static readonly string ExtendedDailyShiftRequiredMessage =
        $"Dienstschicht über {StandardMaxSingleShiftHours} Stunden erfordert die FPersV-Ausnahme bis {ExtendedMaxSingleShiftHours} Stunden.";

    public static readonly string ExtendedDailyShiftTooLongMessage =
        $"Die Dienstschicht darf höchstens {ExtendedMaxSingleShiftHours} Stunden umfassen " +
        $"(FPersV-Ausnahme mit verkürzter täglicher Ruhezeit, max. {MaxExtendedDailyShiftsPerFortnight}× in 2 Kalenderwochen Mo–So).";

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

    public static string ResolveTemplateComplianceHint(int knownServiceDurationMinutes, int knownDrivingMinutes)
    {
        if (knownServiceDurationMinutes <= 0 && knownDrivingMinutes <= 0)
        {
            return DrivingTimeAssumptionHint;
        }

        var parts = new List<string>();
        if (knownServiceDurationMinutes > 0)
        {
            parts.Add($"Dienstlänge {FormatDurationMinutes(knownServiceDurationMinutes)}");
        }

        if (knownDrivingMinutes > 0)
        {
            parts.Add($"Lenkzeit {FormatDurationMinutes(knownDrivingMinutes)}");
        }

        return $"{TemplateComplianceHintPrefix} {string.Join(" · ", parts)}.";
    }

    public static bool TryValidate(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        long startEpochMs,
        long endEpochMs,
        string? excludeAssignmentId,
        bool requestReducedRest,
        bool requestExtendedDriving,
        bool requestReducedWeeklyRest,
        bool requestExtendedDailyShift,
        out bool appliedReducedRest,
        out bool appliedExtendedDriving,
        out bool appliedReducedWeeklyRest,
        out bool appliedExtendedDailyShift,
        out string errorMessage,
        long part1EndEpochMs = 0,
        long part2StartEpochMs = 0,
        int knownDrivingMinutes = 0,
        int knownServiceDurationMinutes = 0)
    {
        appliedReducedRest = false;
        appliedExtendedDriving = false;
        appliedReducedWeeklyRest = false;
        appliedExtendedDailyShift = false;
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

        if (!ValidateServiceShiftDuration(
                assignments,
                driverKey,
                startEpochMs,
                endEpochMs,
                excludeAssignmentId,
                requestExtendedDailyShift,
                isSplit,
                knownServiceDurationMinutes,
                out appliedExtendedDailyShift,
                out errorMessage))
        {
            return false;
        }

        var relevant = GetDriverAssignments(assignments, driverKey, excludeAssignmentId).ToList();
        var candidate = new DriverDispositionAssignment
        {
            Id = excludeAssignmentId ?? "__candidate__",
            DriverKey = driverKey,
            StartEpochMs = startEpochMs,
            EndEpochMs = endEpochMs,
            Part1EndEpochMs = isSplit ? part1EndEpochMs : 0,
            Part2StartEpochMs = isSplit ? part2StartEpochMs : 0,
            KnownServiceDurationMinutes = Math.Max(0, knownServiceDurationMinutes),
            KnownDrivingMinutes = Math.Max(0, knownDrivingMinutes)
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

    public sealed record DriverDispositionPreviewOptions(
        bool ReducedRestBefore,
        bool ExtendedDrivingDay,
        bool ExtendedDailyShift,
        bool ReducedWeeklyRestBefore);

    public sealed record ComplianceQuotaCounts(
        int ReducedRestUsesInWeek,
        int ExtendedDrivingDaysInWeek,
        int ExtendedDailyShiftsInFortnight,
        int ReducedWeeklyRestUses);

    public static ComplianceQuotaCounts GetQuotaCounts(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        DateTime referenceLocal,
        string? excludeAssignmentId,
        long? previewStartEpochMs = null,
        long? previewEndEpochMs = null,
        long part1EndEpochMs = 0,
        long part2StartEpochMs = 0,
        DriverDispositionPreviewOptions? previewOptions = null,
        int previewKnownDrivingMinutes = 0,
        int previewKnownServiceDurationMinutes = 0)
    {
        var options = previewOptions ?? new DriverDispositionPreviewOptions(false, false, false, false);
        var hasPreview = previewStartEpochMs.HasValue && previewEndEpochMs.HasValue;
        var noFlags = new DriverDispositionPreviewOptions(false, false, false, false);

        var timelineBase = BuildPreviewTimeline(
            assignments,
            driverKey,
            excludeAssignmentId,
            previewStartEpochMs,
            previewEndEpochMs,
            part1EndEpochMs,
            part2StartEpochMs,
            hasPreview ? noFlags : null,
            previewKnownDrivingMinutes,
            previewKnownServiceDurationMinutes);

        var reference = hasPreview
            ? DateTimeOffset.FromUnixTimeMilliseconds(previewStartEpochMs!.Value).LocalDateTime
            : referenceLocal;
        var referenceEpochMs = hasPreview
            ? previewStartEpochMs!.Value
            : new DateTimeOffset(referenceLocal).ToUnixTimeMilliseconds();
        var previewId = excludeAssignmentId ?? "__preview__";

        var reduced = CountReducedRestUsesInCalendarWeek(timelineBase, driverKey, reference, excludeAssignmentId: null);
        var driving = CountExtendedDrivingDaysInCalendarWeek(timelineBase, driverKey, reference, excludeAssignmentId: null);
        var daily15 = CountExtendedDailyShiftsInFortnight(timelineBase, driverKey, reference, excludeAssignmentId: null);
        var weekly = CountReducedWeeklyRestSinceLastRegular(timelineBase, driverKey, referenceEpochMs, excludeAssignmentId: null);

        if (!hasPreview)
        {
            return new ComplianceQuotaCounts(reduced, driving, daily15, weekly);
        }

        var preview = timelineBase.FirstOrDefault(a =>
            string.Equals(a.DriverKey, driverKey, StringComparison.Ordinal) &&
            string.Equals(a.Id, previewId, StringComparison.Ordinal));
        if (preview is null)
        {
            return new ComplianceQuotaCounts(reduced, driving, daily15, weekly);
        }

        if (options.ReducedRestBefore && !preview.ReducedRestBefore)
        {
            reduced = Math.Min(reduced + 1, MaxReducedRestPerCalendarWeek);
        }

        if (options.ExtendedDrivingDay && !preview.ExtendedDrivingDay)
        {
            driving = Math.Min(driving + 1, MaxExtendedDrivingDaysPerCalendarWeek);
        }

        if (options.ExtendedDailyShift && !preview.ExtendedDailyShift)
        {
            daily15 = Math.Min(daily15 + 1, MaxExtendedDailyShiftsPerFortnight);
        }

        if (options.ReducedWeeklyRestBefore && !preview.ReducedWeeklyRestBefore)
        {
            weekly = Math.Min(weekly + 1, MaxReducedWeeklyRestBetweenRegular);
        }

        return new ComplianceQuotaCounts(reduced, driving, daily15, weekly);
    }

    public static string BuildComplianceSummary(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        long? startEpochMs,
        long? endEpochMs,
        string? excludeAssignmentId,
        long part1EndEpochMs = 0,
        long part2StartEpochMs = 0,
        DriverDispositionPreviewOptions? previewOptions = null,
        int previewKnownDrivingMinutes = 0,
        int previewKnownServiceDurationMinutes = 0)
    {
        if (string.IsNullOrEmpty(driverKey))
        {
            return DrivingTimeAssumptionHint;
        }

        var relevant = BuildPreviewTimeline(
            assignments,
            driverKey,
            excludeAssignmentId,
            startEpochMs,
            endEpochMs,
            part1EndEpochMs,
            part2StartEpochMs,
            previewOptions,
            previewKnownDrivingMinutes,
            previewKnownServiceDurationMinutes);

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
        var quotas = GetQuotaCounts(
            assignments,
            driverKey,
            reference,
            excludeAssignmentId,
            startEpochMs,
            endEpochMs,
            part1EndEpochMs,
            part2StartEpochMs,
            previewOptions,
            previewKnownDrivingMinutes,
            previewKnownServiceDurationMinutes);

        var serviceDurationMinutes = previewKnownServiceDurationMinutes > 0
            ? previewKnownServiceDurationMinutes
            : relevant.MaxBy(a => a.KnownServiceDurationMinutes)?.KnownServiceDurationMinutes ?? 0;
        var drivingMinutes = previewKnownDrivingMinutes > 0
            ? previewKnownDrivingMinutes
            : relevant.MaxBy(a => a.KnownDrivingMinutes)?.KnownDrivingMinutes ?? 0;
        var complianceHint = ResolveTemplateComplianceHint(serviceDurationMinutes, drivingMinutes);

        return
            $"{complianceHint} " +
            $"Lenkzeit Tag: {FormatHours(dayMs)}/{MaxDailyDrivingHours} h · " +
            $"Woche: {FormatHours(weekMs)}/{MaxWeeklyDrivingHours} h · " +
            $"2 Wochen: {FormatHours(fortnightMs)}/{MaxFortnightlyDrivingHours} h · " +
            $"Lenkzeit 10 h: {quotas.ExtendedDrivingDaysInWeek}/{MaxExtendedDrivingDaysPerCalendarWeek} · " +
            $"15-h-Dienst: {quotas.ExtendedDailyShiftsInFortnight}/{MaxExtendedDailyShiftsPerFortnight} · " +
            $"Wochenruhe 24 h: {quotas.ReducedWeeklyRestUses}/{MaxReducedWeeklyRestBetweenRegular} · " +
            $"Längste Ruhe (Woche): {FormatHours(longestWeekRest)} h · " +
            $"Längste Ruhe (14 Tage): {FormatHours(longestFortnightRest)} h (min. {RegularWeeklyRestHours} h).";
    }

    private static List<DriverDispositionAssignment> BuildPreviewTimeline(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        string? excludeAssignmentId,
        long? startEpochMs,
        long? endEpochMs,
        long part1EndEpochMs,
        long part2StartEpochMs,
        DriverDispositionPreviewOptions? previewOptions,
        int previewKnownDrivingMinutes = 0,
        int previewKnownServiceDurationMinutes = 0)
    {
        var relevant = GetDriverAssignments(assignments, driverKey, excludeAssignmentId).ToList();
        if (startEpochMs is null || endEpochMs is null)
        {
            return relevant;
        }

        var isSplit = part1EndEpochMs > 0 && part2StartEpochMs > part1EndEpochMs;
        var options = previewOptions ?? new DriverDispositionPreviewOptions(false, false, false, false);
        return MergeCandidate(
            relevant,
            new DriverDispositionAssignment
            {
                Id = excludeAssignmentId ?? "__preview__",
                DriverKey = driverKey,
                StartEpochMs = startEpochMs.Value,
                EndEpochMs = endEpochMs.Value,
                Part1EndEpochMs = isSplit ? part1EndEpochMs : 0,
                Part2StartEpochMs = isSplit ? part2StartEpochMs : 0,
                ReducedRestBefore = options.ReducedRestBefore,
                ExtendedDrivingDay = options.ExtendedDrivingDay,
                ExtendedDailyShift = options.ExtendedDailyShift,
                ReducedWeeklyRestBefore = options.ReducedWeeklyRestBefore,
                KnownServiceDurationMinutes = Math.Max(0, previewKnownServiceDurationMinutes),
                KnownDrivingMinutes = Math.Max(0, previewKnownDrivingMinutes)
            },
            excludeAssignmentId);
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

    public static DateTime GetFortnightPeriodStart(DateTime date)
    {
        var monday = GetCalendarWeekStart(date);
        var daysSinceEpoch = (monday - FortnightEpochMonday).Days;
        var fortnightIndex = (int)Math.Floor(daysSinceEpoch / 14.0);
        return FortnightEpochMonday.AddDays(fortnightIndex * 14);
    }

    public static DateTime GetFortnightPeriodEndExclusive(DateTime date) =>
        GetFortnightPeriodStart(date).AddDays(14);

    public static bool RequiresExtendedDailyShift(
        long startEpochMs,
        long endEpochMs,
        int knownServiceDurationMinutes = 0) =>
        knownServiceDurationMinutes > 0
            ? knownServiceDurationMinutes > StandardMaxSingleShiftHours * 60
            : endEpochMs - startEpochMs > HoursToMs(StandardMaxSingleShiftHours);

    public static int CountExtendedDailyShiftsInFortnight(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        DateTime referenceLocal,
        string? excludeAssignmentId)
    {
        var periodStart = GetFortnightPeriodStart(referenceLocal);
        var periodEnd = GetFortnightPeriodEndExclusive(referenceLocal);
        var periodStartMs = new DateTimeOffset(periodStart).ToUnixTimeMilliseconds();
        var periodEndMs = new DateTimeOffset(periodEnd).ToUnixTimeMilliseconds();

        return assignments.Count(a =>
            string.Equals(a.DriverKey, driverKey, StringComparison.Ordinal) &&
            a.ExtendedDailyShift &&
            (excludeAssignmentId is null || !string.Equals(a.Id, excludeAssignmentId, StringComparison.Ordinal)) &&
            a.StartEpochMs >= periodStartMs &&
            a.StartEpochMs < periodEndMs);
    }

    public static bool CanApplyExtendedDailyShift(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        DateTime referenceLocal,
        string? excludeAssignmentId) =>
        CountExtendedDailyShiftsInFortnight(assignments, driverKey, referenceLocal, excludeAssignmentId) <
        MaxExtendedDailyShiftsPerFortnight;

    private static bool ValidateServiceShiftDuration(
        IEnumerable<DriverDispositionAssignment> assignments,
        string driverKey,
        long startEpochMs,
        long endEpochMs,
        string? excludeAssignmentId,
        bool requestExtendedDailyShift,
        bool isSplit,
        int knownServiceDurationMinutes,
        out bool appliedExtendedDailyShift,
        out string errorMessage)
    {
        appliedExtendedDailyShift = false;
        errorMessage = string.Empty;

        var shiftMs = knownServiceDurationMinutes > 0
            ? knownServiceDurationMinutes * 60_000L
            : endEpochMs - startEpochMs;

        if (isSplit)
        {
            if (shiftMs > HoursToMs(MaxSplitServiceShiftHours))
            {
                errorMessage = SplitShiftRules.MaxServiceShiftMessage;
                return false;
            }

            return true;
        }

        if (shiftMs > HoursToMs(ExtendedMaxSingleShiftHours))
        {
            errorMessage = ExtendedDailyShiftTooLongMessage;
            return false;
        }

        if (shiftMs <= HoursToMs(StandardMaxSingleShiftHours))
        {
            return true;
        }

        if (!requestExtendedDailyShift)
        {
            errorMessage = ExtendedDailyShiftRequiredMessage;
            return false;
        }

        var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(startEpochMs).LocalDateTime;
        if (!CanApplyExtendedDailyShift(assignments, driverKey, startLocal, excludeAssignmentId))
        {
            errorMessage = ExtendedDailyShiftQuotaMessage;
            return false;
        }

        appliedExtendedDailyShift = true;
        return true;
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
            var maxExtendedMs = HoursToMs(MaxDailyDrivingExtendedHours);
            var maxDailyMs = HoursToMs(MaxDailyDrivingHours);
            if (dayMs > maxExtendedMs)
            {
                errorMessage = DailyDrivingExceedsTenMessage;
                return false;
            }

            if (dayMs > maxDailyMs)
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
            total += SumAssignmentDrivingMsOnDay(assignment, day);
        }

        return total;
    }

    private static long SumAssignmentDrivingMsOnDay(DriverDispositionAssignment assignment, DateTime day)
    {
        var workMs = SplitShiftCompliance.SumWorkMsOnDay(assignment, day);
        if (workMs <= 0 || assignment.KnownDrivingMinutes <= 0)
        {
            return workMs;
        }

        var totalWorkMs = assignment.EnumerateWorkSegments().Sum(segment => segment.EndMs - segment.StartMs);
        if (totalWorkMs <= 0)
        {
            return workMs;
        }

        var drivingMs = assignment.KnownDrivingMinutes * 60_000L;
        return (long)(drivingMs * ((double)workMs / totalWorkMs));
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

    private static string FormatDurationMinutes(int minutes)
    {
        var hours = minutes / 60;
        var mins = minutes % 60;
        return mins == 0 ? $"{hours} h" : $"{hours}:{mins:D2} h";
    }

    private readonly record struct RestGap(long StartMs, long EndMs)
    {
        public long DurationMs => EndMs - StartMs;
    }
}
