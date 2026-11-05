using System.Globalization;
using System.Text;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Zeitwirtschaft;

/// <summary>Personalstamm, Diensteinteilung und Dienstvorlagen für die gesetzliche Bewertung.</summary>
public sealed class ZeitwirtschaftLegalContext
{
    public DateOnly? BirthDate { get; init; }

    public bool BirthDateMissing { get; init; }

    public DateOnly? EntryDate { get; init; }

    public bool EntryDateMissing { get; init; }

    public bool MaternityProtection { get; init; }

    public string PersonnelDigits { get; init; } = string.Empty;

    public IReadOnlyList<DriverDispositionAssignment> Assignments { get; init; } = [];

    public IReadOnlyDictionary<string, DutyTemplate> Templates { get; init; } =
        new Dictionary<string, DutyTemplate>(StringComparer.Ordinal);

    public bool SamePerson(string driverKey)
    {
        if (PersonnelDigits.Length == 0 || string.IsNullOrWhiteSpace(driverKey))
        {
            return false;
        }

        var fromKey = EmployeeDispoKeys.TryGetPersonnelDigits(driverKey);
        return fromKey is not null &&
               EmployeeRosterItem.NormalizePersonnelDigits(fromKey) == PersonnelDigits;
    }

    public static ZeitwirtschaftLegalContext Create(
        string personnelNumber,
        EmployeeRosterItem? roster,
        IReadOnlyList<DriverDispositionAssignment> assignments,
        IReadOnlyList<DutyTemplate> templates)
    {
        var digits = EmployeeRosterItem.NormalizePersonnelDigits(roster?.PersonnelNumber ?? personnelNumber);
        DateOnly? birth = null;
        var missing = true;
        if (roster is not null && ZeitwirtschaftCompliance.TryParseBirthDate(roster.BirthDate, out var parsed))
        {
            birth = parsed;
            missing = false;
        }

        DateOnly? entry = null;
        var entryMissing = true;
        if (roster is not null && ZeitwirtschaftCompliance.TryParseBirthDate(roster.EntryDate, out var parsedEntry))
        {
            entry = parsedEntry;
            entryMissing = false;
        }

        return new ZeitwirtschaftLegalContext
        {
            BirthDate = birth,
            BirthDateMissing = missing,
            EntryDate = entry,
            EntryDateMissing = entryMissing,
            MaternityProtection = roster?.MaternityProtection == true,
            PersonnelDigits = digits,
            Assignments = assignments,
            Templates = templates
                .Where(t => !string.IsNullOrWhiteSpace(t.Id))
                .GroupBy(t => t.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
        };
    }
}

public sealed class ZeitwirtschaftShiftFigures
{
    public static ZeitwirtschaftShiftFigures Empty { get; } = new();

    public bool Open { get; init; }

    public int StatutoryMinutes { get; init; }

    public int QualifyingPauseMinutes { get; init; }

    public int PayMinutes { get; init; }

    public IReadOnlyList<string> Hints { get; init; } = [];
}

public sealed class ZeitwirtschaftAssessment
{
    public required string Summary { get; init; }

    public required IReadOnlyDictionary<string, ZeitwirtschaftShiftFigures> Figures { get; init; }

    public required IReadOnlyDictionary<string, string> PeriodHints { get; init; }

    public required IReadOnlyList<ZeitwirtschaftTimeTableRow> MissingRows { get; init; }
}

/// <summary>
/// Bewertung nach dem Arbeitszeitgesetz. Die App zeichnet nur Stempel auf.
/// Tarifpausen eines Sechstel-Dienstes mindern die gesetzliche Arbeitszeit nicht.
/// </summary>
public static class ZeitwirtschaftCompliance
{
    private const int YouthDayMinutes = 8 * 60;
    private const int YouthWeekMinutes = 40 * 60;
    private const int YouthRestMinutes = 12 * 60;
    private const int YouthStretchMinutes = 270;
    private const int MaternityDayMinutes = 8 * 60 + 30;
    private const int CompensatingRestMinutes = 12 * 60;

    public static bool TryParseBirthDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        string[] formats = ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "yyyy-MM-dd"];
        if (DateOnly.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateOnly.TryParse(trimmed, CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out date);
    }

    public static bool IsSixthDuty(ZeitwirtschaftMergedEntry entry, ZeitwirtschaftLegalContext? legal)
    {
        if (entry.SixthDuty is bool chosen)
        {
            return chosen;
        }

        return MatchTemplate(legal, entry)?.IsSixthDuty == true;
    }

    public static ZeitwirtschaftAssessment Assess(
        ZeitwirtschaftMergedEmployee employee,
        ZeitwirtschaftLegalContext? legal,
        int? year,
        int? month,
        IReadOnlyDictionary<string, string>? vehicleLabels)
    {
        var figures = new Dictionary<string, ZeitwirtschaftShiftFigures>(StringComparer.Ordinal);
        var workByDay = new Dictionary<DateOnly, int>();
        var nightByDay = new Dictionary<DateOnly, int>();
        var night20ByDay = new Dictionary<DateOnly, int>();
        var anchor = new Dictionary<DateOnly, (string Id, long Start)>();
        var finished = new List<FinishedShift>();

        foreach (var entry in employee.Entries)
        {
            var shift = Measure(entry, legal);
            figures[entry.EntryId] = shift.Figures;
            if (shift.Open || shift.EndMs is not long endMs)
            {
                continue;
            }

            finished.Add(new FinishedShift(entry.EntryId, shift.StartMs, endMs, entry.Voided));
            AddSlices(entry.EntryId, shift.StartMs, endMs, shift.QualifyingBreaks, workByDay, nightByDay, night20ByDay, anchor);
        }

        var period = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string id, string text)
        {
            if (text.Length == 0 || id.Length == 0)
            {
                return;
            }

            if (!period.TryGetValue(id, out var list))
            {
                list = [];
                period[id] = list;
            }

            if (!list.Contains(text))
            {
                list.Add(text);
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var over10 = new List<DateOnly>();
        var over8Open = new List<DateOnly>();
        var over8Failed = new List<DateOnly>();
        var sundayFailed = 0;
        foreach (var day in workByDay.Keys.Order())
        {
            var minutes = workByDay[day];
            var id = anchor.TryGetValue(day, out var anchored) ? anchored.Id : string.Empty;
            var age = AgeOn(legal?.BirthDate, day);
            var youth = age is < 18;
            var maternity = legal?.MaternityProtection == true;
            var nightDay = nightByDay.GetValueOrDefault(day) >= 120;

            if (youth && minutes > YouthDayMinutes)
            {
                Add(id, "Jugendschutz: Tagesarbeitszeit über 8 Stunden, keine Verlängerung auf 10 Stunden.");
            }
            else if (maternity && minutes > MaternityDayMinutes)
            {
                Add(id, "Mutterschutz: Tagesarbeitszeit über 8 Stunden 30 Minuten.");
            }
            else if (minutes > ZeitwirtschaftArbzg.AbsoluteDayMinutes)
            {
                over10.Add(day);
                Add(id, "Tagesarbeitszeit über 10 Stunden. Das ist nicht ausgleichbar.");
            }
            else if (minutes > ZeitwirtschaftArbzg.StandardDayMinutes)
            {
                var status = DescribeExtension(day, today, workByDay, nightDay);
                if (status.Covered)
                {
                    Add(id, status.Text);
                }
                else if (status.StillOpen)
                {
                    over8Open.Add(day);
                    Add(id, status.Text);
                }
                else
                {
                    over8Failed.Add(day);
                    Add(id, status.Text);
                }
            }

            if (youth && night20ByDay.GetValueOrDefault(day) > 0)
            {
                Add(id, "Jugendschutz: Nachtarbeit zwischen 20 und 6 Uhr.");
            }
            else if (maternity && night20ByDay.GetValueOrDefault(day) > 0)
            {
                Add(id, "Mutterschutz: Nachtarbeit zwischen 20 und 6 Uhr.");
            }

            if (IsPublicHoliday(day) && day.DayOfWeek != DayOfWeek.Sunday && minutes > 0)
            {
                var holidayHint = SubstituteRestHint(day, today, workByDay, "Feiertagsarbeit", legal?.EntryDate);
                Add(id, holidayHint);
                if (holidayHint.Contains("ohne Ersatzruhetag", StringComparison.Ordinal))
                {
                    sundayFailed++;
                }
                if (youth)
                {
                    Add(id, "Jugendschutz: Beschäftigung an einem Feiertag.");
                }
                else if (maternity)
                {
                    Add(id, "Mutterschutz: Beschäftigung an einem Feiertag.");
                }
            }
        }

        if (legal?.BirthDate is not null)
        {
            foreach (var week in workByDay.GroupBy(p => WeekStart(p.Key)))
            {
                if (week.Sum(p => p.Value) <= YouthWeekMinutes)
                {
                    continue;
                }

                var sample = week.OrderBy(p => p.Key).First().Key;
                if (AgeOn(legal.BirthDate, sample) is < 18)
                {
                    var id = anchor.TryGetValue(sample, out var anchored) ? anchored.Id : string.Empty;
                    Add(id, "Jugendschutz: über 40 Stunden in der Kalenderwoche.");
                }
            }
        }

        var ordered = finished.OrderBy(s => s.StartMs).ToList();
        var restFailed = 0;
        var restOpen = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var next = ordered[i];
            if (IsSplitInterior(legal, previous.EndMs, next.StartMs))
            {
                Add(next.Id, "Unterbrechung im geteilten Dienst. Das ist keine tägliche Ruhezeit.");
                continue;
            }

            var restMinutes = ZeitwirtschaftMergeService.WholeMinutesBetween(previous.EndMs, next.StartMs);
            var restDay = LocalDate(next.StartMs);
            var age = AgeOn(legal?.BirthDate, restDay);
            if (age is < 18 && restMinutes < YouthRestMinutes)
            {
                restFailed++;
                Add(next.Id, "Jugendschutz: Ruhezeit unter 12 Stunden.");
                continue;
            }

            if (restMinutes >= ZeitwirtschaftArbzg.FullRestMinutes)
            {
                continue;
            }

            if (restMinutes < ZeitwirtschaftArbzg.TransportRestMinutes)
            {
                restFailed++;
                Add(next.Id, "Ruhezeit unter 10 Stunden. Das ist nicht ausgleichbar.");
                continue;
            }

            if (HasCompensatingRest(ordered, i, next.StartMs))
            {
                Add(next.Id, "Ruhezeit unter 11 Stunden, ausgeglichen durch eine spätere Ruhezeit von mindestens 12 Stunden.");
                continue;
            }

            var deadline = restDay.AddDays(28);
            var monthEnd = new DateOnly(restDay.Year, restDay.Month, DateTime.DaysInMonth(restDay.Year, restDay.Month));
            if (today <= deadline || today <= monthEnd)
            {
                restOpen++;
                Add(next.Id, "Ruhezeit unter 11 Stunden. Ausgleich durch 12 Stunden Ruhe ist noch offen (4 Wochen oder Kalendermonat).");
            }
            else
            {
                restFailed++;
                Add(next.Id, "Ruhezeit unter 11 Stunden ohne Ausgleich durch 12 Stunden Ruhe.");
            }
        }

        foreach (var day in workByDay.Keys.Where(d => d.DayOfWeek == DayOfWeek.Sunday && workByDay[d] > 0).Order())
        {
            var id = anchor.TryGetValue(day, out var anchored) ? anchored.Id : string.Empty;
            var hint = SubstituteRestHint(day, today, workByDay, "Sonntagsarbeit", legal?.EntryDate);
            Add(id, hint);
            if (hint.Contains("ohne Ersatzruhetag", StringComparison.Ordinal))
            {
                sundayFailed++;
            }

            var age = AgeOn(legal?.BirthDate, day);
            if (age is < 18)
            {
                Add(id, "Jugendschutz: Beschäftigung an einem Sonntag.");
            }
            else if (legal?.MaternityProtection == true)
            {
                Add(id, "Mutterschutz: Beschäftigung an einem Sonntag.");
            }
        }

        var missing = MissingRows(employee, legal, year, month, vehicleLabels);
        var summary = BuildSummary(
            legal,
            workByDay,
            nightByDay,
            year,
            month,
            over10.Count,
            over8Open.Count,
            over8Failed.Count,
            restFailed,
            restOpen,
            sundayFailed,
            missing.Count,
            today);

        return new ZeitwirtschaftAssessment
        {
            Summary = summary,
            Figures = figures,
            PeriodHints = period.ToDictionary(p => p.Key, p => string.Join("\n", p.Value), StringComparer.Ordinal),
            MissingRows = missing
        };
    }

    private static ShiftMeasure Measure(ZeitwirtschaftMergedEntry entry, ZeitwirtschaftLegalContext? legal)
    {
        var start = ZeitwirtschaftMergeService.EffectiveStartMs(entry);
        var end = ZeitwirtschaftMergeService.EffectiveEndMs(entry);
        var hints = new List<string>();
        if (entry.Voided)
        {
            hints.Add("Storno hebt die Aufzeichnung nicht auf.");
        }

        var unclosed = entry.Pauses.Any(p => p.StartEpochMs > 0 && p.EndEpochMs is not > 0);
        if (unclosed)
        {
            hints.Add("Pause ohne Ende. Sie wird nicht von der Arbeitszeit abgezogen.");
        }

        if (end is null || end <= start)
        {
            var running = ZeitwirtschaftMergeService.WholeMinutesBetween(start, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            hints.Add("Arbeitsende fehlt.");
            if (running > ZeitwirtschaftArbzg.AbsoluteDayMinutes)
            {
                hints.Add("Seit Dienstbeginn bereits über 10 Stunden.");
            }

            return new ShiftMeasure(
                new ZeitwirtschaftShiftFigures
                {
                    Open = true,
                    Hints = hints
                },
                start,
                null,
                [],
                true);
        }

        var closed = ZeitwirtschaftArbzg.ClosedIntervals(start, end.Value, entry.Pauses);
        var qualifying = ZeitwirtschaftArbzg.QualifyingIntervals(closed);
        var qualifyingMinutes = ZeitwirtschaftArbzg.QualifyingMinutes(closed);
        var shortMinutes = Math.Max(0, ZeitwirtschaftArbzg.MinutesOf(closed) - qualifyingMinutes);
        var statutory = Math.Max(0, ZeitwirtschaftMergeService.WholeMinutesBetween(start, end.Value) - qualifyingMinutes);
        if (shortMinutes > 0)
        {
            hints.Add(
                "Kurzunterbrechung " + ZeitwirtschaftMergeService.FormatMinutesHhMm(shortMinutes) +
                " bleibt Arbeitszeit.");
        }

        var day = LocalDate(start);
        var age = AgeOn(legal?.BirthDate, day);
        var youth = age is < 18;
        var longest = ZeitwirtschaftArbzg.LongestWorkStretchMinutes(start, end.Value, qualifying);
        if (youth)
        {
            if (statutory > 6 * 60 && qualifyingMinutes < 60)
            {
                hints.Add("Jugendschutz: bei mehr als 6 Stunden sind 60 Minuten Pause nötig.");
            }
            else if (statutory > YouthStretchMinutes && qualifyingMinutes < 30)
            {
                hints.Add("Jugendschutz: bei mehr als 4,5 Stunden sind 30 Minuten Pause nötig.");
            }

            if (longest > YouthStretchMinutes)
            {
                hints.Add("Jugendschutz: mehr als 4,5 Stunden ohne Pause.");
            }
        }
        else
        {
            var required = ZeitwirtschaftArbzg.RequiredBreakMinutes(statutory);
            if (required > 0 && qualifyingMinutes < required)
            {
                hints.Add($"Pausenpflicht {required} Min. nicht erfüllt (gültig erfasst {qualifyingMinutes} Min.).");
            }

            if (longest > ZeitwirtschaftArbzg.MaxContinuousMinutes)
            {
                hints.Add("Mehr als 6 Stunden ohne Pause.");
            }
        }

        var template = MatchTemplate(legal, entry);
        var sixth = entry.SixthDuty ?? template?.IsSixthDuty == true;
        var pay = statutory;
        if (sixth)
        {
            var threshold = template?.WendePauseFromMinutes > 0 ? template.WendePauseFromMinutes : 8;
            if (template is null)
            {
                hints.Add("Sechstel-Dienst ohne Dienstvorlage. Es sind keine Wendezeiten aus dem Dienstplan bekannt.");
            }
            else
            {
                var (tariffAll, tariffLong) = TariffGaps(template, threshold);
                var tariffShort = Math.Max(0, tariffAll - tariffLong);
                var extra = tariffShort + Math.Max(0, tariffLong - qualifyingMinutes);
                pay = Math.Max(0, statutory - extra);
                hints.Add(
                    "Tarif Sechstel: Wendezeiten ab " + threshold + " Min. = " +
                    ZeitwirtschaftMergeService.FormatMinutesHhMm(tariffAll) +
                    ". Das zählt nicht als gesetzliche Ruhepause.");
            }
        }

        var needsAdvance = youth ? statutory > YouthStretchMinutes : statutory > 6 * 60;
        if (needsAdvance)
        {
            if (template is null)
            {
                hints.Add("Keine Diensteinteilung zum Stempel. Die Ruhepause war nicht im Voraus festgelegt.");
            }
            else if (!HasPlannedGap(template, ZeitwirtschaftArbzg.QualifyingSegmentMinutes))
            {
                hints.Add("Die Ruhepause war im Dienstplan nicht im Voraus festgelegt.");
            }
        }

        return new ShiftMeasure(
            new ZeitwirtschaftShiftFigures
            {
                StatutoryMinutes = statutory,
                QualifyingPauseMinutes = qualifyingMinutes,
                PayMinutes = pay,
                Hints = hints
            },
            start,
            end,
            qualifying,
            false);
    }

    private static void AddSlices(
        string entryId,
        long startMs,
        long endMs,
        IReadOnlyList<ZeitwirtschaftArbzg.Interval> qualifying,
        Dictionary<DateOnly, int> workByDay,
        Dictionary<DateOnly, int> nightByDay,
        Dictionary<DateOnly, int> night20ByDay,
        Dictionary<DateOnly, (string Id, long Start)> anchor)
    {
        foreach (var (segmentStart, segmentEnd) in WorkSegments(startMs, endMs, qualifying))
        {
            var cursor = segmentStart;
            while (cursor < segmentEnd)
            {
                var local = DateTimeOffset.FromUnixTimeMilliseconds(cursor).ToLocalTime();
                var nextMidnight = new DateTimeOffset(local.Date.AddDays(1), local.Offset).ToUnixTimeMilliseconds();
                var sliceEnd = Math.Min(segmentEnd, nextMidnight);
                if (sliceEnd <= cursor)
                {
                    sliceEnd = Math.Min(segmentEnd, cursor + 60_000);
                }

                var date = DateOnly.FromDateTime(local.DateTime);
                var minutes = ZeitwirtschaftMergeService.WholeMinutesBetween(cursor, sliceEnd);
                workByDay[date] = workByDay.GetValueOrDefault(date) + minutes;
                var localEnd = DateTimeOffset.FromUnixTimeMilliseconds(sliceEnd).ToLocalTime().DateTime;
                nightByDay[date] = nightByDay.GetValueOrDefault(date) +
                                   OverlapMinutes(local.DateTime, localEnd, local.Date, local.Date.AddHours(6)) +
                                   OverlapMinutes(local.DateTime, localEnd, local.Date.AddHours(23), local.Date.AddDays(1));
                night20ByDay[date] = night20ByDay.GetValueOrDefault(date) +
                                     OverlapMinutes(local.DateTime, localEnd, local.Date, local.Date.AddHours(6)) +
                                     OverlapMinutes(local.DateTime, localEnd, local.Date.AddHours(20), local.Date.AddDays(1));
                if (!anchor.TryGetValue(date, out var current) || startMs >= current.Start)
                {
                    anchor[date] = (entryId, startMs);
                }

                cursor = sliceEnd;
            }
        }
    }

    private static List<(long Start, long End)> WorkSegments(
        long startMs,
        long endMs,
        IReadOnlyList<ZeitwirtschaftArbzg.Interval> breaks)
    {
        var list = new List<(long Start, long End)>();
        var cursor = startMs;
        foreach (var pause in breaks.OrderBy(p => p.StartMs))
        {
            var pauseStart = Math.Max(pause.StartMs, startMs);
            var pauseEnd = Math.Min(pause.EndMs, endMs);
            if (pauseStart > cursor)
            {
                list.Add((cursor, Math.Min(pauseStart, endMs)));
            }

            if (pauseEnd > cursor)
            {
                cursor = pauseEnd;
            }

            if (cursor >= endMs)
            {
                break;
            }
        }

        if (endMs > cursor)
        {
            list.Add((cursor, endMs));
        }

        return list;
    }

    private static ExtensionStatus DescribeExtension(
        DateOnly day,
        DateOnly today,
        Dictionary<DateOnly, int> workByDay,
        bool nightWindows)
    {
        WindowResult[] windows = nightWindows
            ?
            [
                Window(day.AddDays(-(28 - 1)), day, today, workByDay),
                Window(new DateOnly(day.Year, day.Month, 1), LastOfMonth(day), today, workByDay),
                Window(day, day.AddDays(28 - 1), today, workByDay),
                Window(new DateOnly(day.Year, day.Month, 1), LastOfMonth(day), today, workByDay)
            ]
            :
            [
                Window(day.AddDays(-(24 * 7 - 1)), day, today, workByDay),
                Window(day.AddMonths(-6), day, today, workByDay),
                Window(day, day.AddDays(24 * 7 - 1), today, workByDay),
                Window(day, day.AddMonths(6), today, workByDay)
            ];

        var covered = windows.Any(w => w.Covered);
        var stillOpen = windows.Any(w => w.StillOpen);
        var back = nightWindows
            ? $"4 Wochen Ø {FormatAverage(windows[0].AverageMinutes)}, Kalendermonat Ø {FormatAverage(windows[1].AverageMinutes)}"
            : $"24 Wochen Ø {FormatAverage(windows[0].AverageMinutes)}, 6 Monate Ø {FormatAverage(windows[1].AverageMinutes)}";
        var kind = nightWindows ? "Nachtarbeit über 8 Stunden" : "Tagesarbeitszeit über 8 Stunden";
        if (covered)
        {
            return new ExtensionStatus(true, false, kind + ", Ausgleich gedeckt (" + back + ").");
        }

        if (stillOpen)
        {
            return new ExtensionStatus(false, true, kind + ", Ausgleich noch offen (" + back + ").");
        }

        return new ExtensionStatus(false, false, kind + " ohne Ausgleich (" + back + ").");
    }

    private readonly record struct WindowResult(bool Covered, bool StillOpen, double AverageMinutes);

    private readonly record struct ExtensionStatus(bool Covered, bool StillOpen, string Text);

    private static WindowResult Window(
        DateOnly from,
        DateOnly to,
        DateOnly today,
        Dictionary<DateOnly, int> workByDay)
    {
        var end = to < today ? to : today;
        if (from > end)
        {
            return new WindowResult(false, to >= today, 0);
        }

        var sum = 0;
        var count = 0;
        for (var day = from; day <= end; day = day.AddDays(1))
        {
            if (!IsWerktag(day))
            {
                continue;
            }

            count++;
            sum += workByDay.GetValueOrDefault(day);
        }

        var average = count == 0 ? 0 : (double)sum / count;
        return new WindowResult(average <= ZeitwirtschaftArbzg.StandardDayMinutes, to > today, average);
    }

    private static bool HasCompensatingRest(List<FinishedShift> ordered, int shortenedIndex, long shortenedRestEndedMs)
    {
        var restDay = LocalDate(shortenedRestEndedMs);
        var deadline = restDay.AddDays(28);
        var monthEnd = LastOfMonth(restDay);
        for (var i = shortenedIndex + 1; i < ordered.Count; i++)
        {
            var gapDay = LocalDate(ordered[i].StartMs);
            if (gapDay > deadline && gapDay > monthEnd)
            {
                break;
            }

            var gap = ZeitwirtschaftMergeService.WholeMinutesBetween(ordered[i - 1].EndMs, ordered[i].StartMs);
            if (gap < CompensatingRestMinutes)
            {
                continue;
            }

            if (gapDay <= deadline || gapDay <= monthEnd)
            {
                return true;
            }
        }

        var tail = ordered[^1];
        if (tail.EndMs > shortenedRestEndedMs)
        {
            var since = ZeitwirtschaftMergeService.WholeMinutesBetween(tail.EndMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var now = DateOnly.FromDateTime(DateTime.Today);
            if (since >= CompensatingRestMinutes && (now <= deadline || now <= monthEnd))
            {
                return true;
            }
        }

        return false;
    }

    private static string SubstituteRestHint(
        DateOnly employmentDay,
        DateOnly today,
        Dictionary<DateOnly, int> workByDay,
        string label,
        DateOnly? entryDate)
    {
        if (entryDate is null)
        {
            return label + ", Ersatzruhetag noch offen (Eintrittsdatum fehlt).";
        }

        DateOnly? free = null;
        var from = employmentDay.AddDays(-13);
        if (from < entryDate.Value)
        {
            from = entryDate.Value;
        }

        for (var day = from; day <= employmentDay.AddDays(13); day = day.AddDays(1))
        {
            if (day == employmentDay || day > today || day < entryDate.Value)
            {
                continue;
            }

            if (workByDay.GetValueOrDefault(day) == 0)
            {
                free = day;
                break;
            }
        }

        if (free is DateOnly granted)
        {
            return label + ", Ersatzruhetag am " + granted.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + ".";
        }

        if (employmentDay.AddDays(13) >= today)
        {
            return label + ", Ersatzruhetag noch offen.";
        }

        return label + " ohne Ersatzruhetag.";
    }

    private static List<ZeitwirtschaftTimeTableRow> MissingRows(
        ZeitwirtschaftMergedEmployee employee,
        ZeitwirtschaftLegalContext? legal,
        int? year,
        int? month,
        IReadOnlyDictionary<string, string>? vehicleLabels)
    {
        var rows = new List<ZeitwirtschaftTimeTableRow>();
        if (legal is null)
        {
            return rows;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var assignment in legal.Assignments.OrderBy(a => a.StartEpochMs))
        {
            if (!legal.SamePerson(assignment.DriverKey) || assignment.EndEpochMs <= assignment.StartEpochMs)
            {
                continue;
            }

            if (assignment.EndEpochMs > now)
            {
                continue;
            }

            var day = LocalDate(assignment.StartEpochMs);
            if (year is > 0 && month is > 0 && (day.Year != year || day.Month != month))
            {
                continue;
            }

            var covered = employee.Entries.Any(entry =>
            {
                var start = ZeitwirtschaftMergeService.EffectiveStartMs(entry);
                var end = ZeitwirtschaftMergeService.EffectiveEndMs(entry) ?? now;
                return start < assignment.EndEpochMs && end > assignment.StartEpochMs;
            });
            if (covered)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(assignment.DutyNumber)
                ? assignment.Label
                : assignment.DutyNumber;
            rows.Add(new ZeitwirtschaftTimeTableRow
            {
                VehiclePhone = string.Empty,
                VehicleDisplayName = string.IsNullOrWhiteSpace(label) ? "Dienstplan" : label.Trim(),
                PersonnelNumber = employee.PersonnelNumber,
                Kommen = ZeitwirtschaftMergeService.FormatStamp(assignment.StartEpochMs, null),
                Gehen = ZeitwirtschaftMergeService.FormatStamp(assignment.EndEpochMs, null),
                Arbeitszeit = "—",
                Lohnstunden = "—",
                Pause = "—",
                Hinweis = "Keine Zeiterfassung zum eingeteilten Dienst.",
                EntryId = string.Empty
            });
        }

        _ = vehicleLabels;
        return rows;
    }

    private static string BuildSummary(
        ZeitwirtschaftLegalContext? legal,
        Dictionary<DateOnly, int> workByDay,
        Dictionary<DateOnly, int> nightByDay,
        int? year,
        int? month,
        int over10,
        int over8Open,
        int over8Failed,
        int restFailed,
        int restOpen,
        int sundayFailed,
        int missing,
        DateOnly today)
    {
        var shown = workByDay.Where(p => year is not > 0 || month is not > 0 || (p.Key.Year == year && p.Key.Month == month));
        var legalMinutes = shown.Sum(p => p.Value);
        var builder = new StringBuilder();
        builder.Append("Gesetzliche Arbeitszeit");
        if (year is > 0 && month is > 0)
        {
            builder.Append(" im gewählten Monat");
        }

        builder.Append(": ");
        builder.Append(legalMinutes > 0 ? ZeitwirtschaftMergeService.FormatMinutesHhMm(legalMinutes) : "—");
        builder.Append(". Stunden über acht Stunden bleiben in der Aufzeichnung, mindestens zwei Jahre. Eine Stornierung nimmt sie nicht heraus.");

        builder.Append("\nTage über 10 Stunden: ").Append(over10).Append('.');
        builder.Append(" Tage über 8 Stunden ohne abgeschlossenen Ausgleich: ").Append(over8Failed).Append('.');
        builder.Append(" Ausgleich noch offen: ").Append(over8Open).Append('.');
        builder.Append("\nRuhezeiten unter 10 Stunden oder ohne Ausgleich: ").Append(restFailed).Append('.');
        builder.Append(" Ruhezeitausgleich noch offen: ").Append(restOpen).Append('.');
        builder.Append(" Sonn- oder Feiertage ohne Ersatzruhetag: ").Append(sundayFailed).Append('.');
        builder.Append(" Eingeteilte Dienste ohne Stempel: ").Append(missing).Append('.');

        foreach (var calendarYear in workByDay.Keys.Select(d => d.Year).Distinct().Order())
        {
            var sundays = AllSundays(calendarYear).ToList();
            var free = sundays.Count(d => d <= today && workByDay.GetValueOrDefault(d) == 0);
            var remaining = sundays.Count(d => d > today);
            if (free + remaining < 15)
            {
                builder.Append("\n").Append(calendarYear).Append(": weniger als 15 beschäftigungsfreie Sonntage erreichbar (frei ")
                    .Append(free).Append(", verbleibend ").Append(remaining).Append(").");
            }
        }

        var nightDays = nightByDay.Count(p => p.Value >= 120);
        if (nightDays > 0)
        {
            builder.Append("\nTage mit mindestens zwei Stunden Nachtarbeit (23 bis 6 Uhr): ").Append(nightDays).Append('.');
        }

        if (legal?.BirthDateMissing != false)
        {
            builder.Append("\nGeburtsdatum fehlt in der Personalverwaltung. Der Jugendschutz ist nicht geprüft.");
        }

        if (legal?.EntryDateMissing != false)
        {
            builder.Append("\nEintrittsdatum fehlt in der Personalverwaltung. Ein Ersatzruhetag wird daraus nicht abgeleitet.");
        }

        if (legal?.MaternityProtection == true)
        {
            builder.Append("\nMutterschutz ist in der Personalverwaltung gesetzt.");
        }

        return builder.ToString();
    }

    private static DutyTemplate? MatchTemplate(ZeitwirtschaftLegalContext? legal, ZeitwirtschaftMergedEntry entry)
    {
        if (legal is null)
        {
            return null;
        }

        var start = ZeitwirtschaftMergeService.EffectiveStartMs(entry);
        var end = ZeitwirtschaftMergeService.EffectiveEndMs(entry) ?? start;
        DriverDispositionAssignment? match = null;
        foreach (var assignment in legal.Assignments)
        {
            if (!legal.SamePerson(assignment.DriverKey) || assignment.EndEpochMs <= assignment.StartEpochMs)
            {
                continue;
            }

            if (start < assignment.EndEpochMs && end > assignment.StartEpochMs)
            {
                match = assignment;
                if (!string.IsNullOrWhiteSpace(assignment.DutyTemplateId))
                {
                    break;
                }
            }
        }

        match ??= legal.Assignments.FirstOrDefault(a =>
            legal.SamePerson(a.DriverKey) && LocalDate(a.StartEpochMs) == LocalDate(start));
        if (match is null || string.IsNullOrWhiteSpace(match.DutyTemplateId))
        {
            return null;
        }

        return legal.Templates.TryGetValue(match.DutyTemplateId, out var template) ? template : null;
    }

    private static bool IsSplitInterior(ZeitwirtschaftLegalContext? legal, long previousEndMs, long nextStartMs)
    {
        if (legal is null)
        {
            return false;
        }

        const long tolerance = 15L * 60 * 1000;
        foreach (var assignment in legal.Assignments)
        {
            if (!legal.SamePerson(assignment.DriverKey) || !assignment.IsSplitShift)
            {
                continue;
            }

            if (previousEndMs >= assignment.Part1EndEpochMs - tolerance &&
                previousEndMs <= assignment.Part1EndEpochMs + tolerance &&
                nextStartMs >= assignment.Part2StartEpochMs - tolerance &&
                nextStartMs <= assignment.Part2StartEpochMs + tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static (int All, int Long) TariffGaps(DutyTemplate template, int threshold)
    {
        var all = 0;
        var longGaps = 0;
        foreach (var gap in PlannedGaps(template))
        {
            if (gap < threshold)
            {
                continue;
            }

            all += gap;
            if (gap >= ZeitwirtschaftArbzg.QualifyingSegmentMinutes)
            {
                longGaps += gap;
            }
        }

        return (all, longGaps);
    }

    private static bool HasPlannedGap(DutyTemplate template, int minimum) =>
        PlannedGaps(template).Any(gap => gap >= minimum);

    private static IEnumerable<int> PlannedGaps(DutyTemplate template)
    {
        var spans = new List<(int From, int To)>();
        foreach (var row in template.Rows.Concat(template.Part2Rows).Concat(template.Part3Rows))
        {
            if (TryClock(row.FromTime, out var from) && TryClock(row.ToTime, out var to) && to >= from)
            {
                spans.Add((from, to));
            }
        }

        spans.Sort((a, b) => a.From.CompareTo(b.From));
        for (var i = 1; i < spans.Count; i++)
        {
            var gap = spans[i].From - spans[i - 1].To;
            if (gap > 0)
            {
                yield return gap;
            }
        }
    }

    private static bool TryClock(string text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':', '.');
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mins) ||
            hours is < 0 or > 47 ||
            mins is < 0 or > 59)
        {
            return false;
        }

        minutes = hours * 60 + mins;
        return true;
    }

    private static bool IsWerktag(DateOnly day) =>
        day.DayOfWeek is not DayOfWeek.Sunday && !IsPublicHoliday(day);

    public static bool IsPublicHoliday(DateOnly day)
    {
        if (day is { Month: 1, Day: 1 } or { Month: 5, Day: 1 } or { Month: 10, Day: 3 } or { Month: 11, Day: 1 }
            or { Month: 12, Day: 25 } or { Month: 12, Day: 26 })
        {
            return true;
        }

        var easter = EasterSunday(day.Year);
        return day == easter.AddDays(-2) ||
               day == easter.AddDays(1) ||
               day == easter.AddDays(39) ||
               day == easter.AddDays(50) ||
               day == easter.AddDays(60);
    }

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static IEnumerable<DateOnly> AllSundays(int year)
    {
        var day = new DateOnly(year, 1, 1);
        while (day.DayOfWeek != DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        while (day.Year == year)
        {
            yield return day;
            day = day.AddDays(7);
        }
    }

    private static DateOnly WeekStart(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-offset);
    }

    private static DateOnly LastOfMonth(DateOnly day) =>
        new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    private static DateOnly LocalDate(long epochMs) =>
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToLocalTime().DateTime);

    private static int? AgeOn(DateOnly? birth, DateOnly day)
    {
        if (birth is not DateOnly born || day < born)
        {
            return null;
        }

        var age = day.Year - born.Year;
        if (day < born.AddYears(age))
        {
            age--;
        }

        return age;
    }

    private static int OverlapMinutes(DateTime start, DateTime end, DateTime windowStart, DateTime windowEnd)
    {
        var from = start > windowStart ? start : windowStart;
        var to = end < windowEnd ? end : windowEnd;
        if (to <= from)
        {
            return 0;
        }

        return (int)Math.Floor((to - from).TotalMinutes);
    }

    private static string FormatAverage(double minutes)
    {
        var rounded = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        return ZeitwirtschaftMergeService.FormatMinutesHhMm(Math.Max(0, rounded));
    }

    private readonly record struct FinishedShift(string Id, long StartMs, long EndMs, bool Voided);

    private readonly record struct ShiftMeasure(
        ZeitwirtschaftShiftFigures Figures,
        long StartMs,
        long? EndMs,
        IReadOnlyList<ZeitwirtschaftArbzg.Interval> QualifyingBreaks,
        bool Open);
}
