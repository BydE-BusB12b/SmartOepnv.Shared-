using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.Dienstvorlagen;

public static class DutyTemplateCalculator
{
    private static readonly Regex TimeRegex = new(@"\b(\d{1,2})[.:](\d{2})\b", RegexOptions.Compiled);

    /// <summary>Betriebstag beginnt um 03:00 und endet um 02:59 am Folgetag.</summary>
    public const int OperatingDayStartMinutes = 3 * 60;

    /// <summary>Ab dieser Abfahrtszeit gilt eine Fahrt als Abend-/Nachtdienst mit Betriebstagsübergang.</summary>
    private const int EveningTripThresholdMinutes = 18 * 60;

    /// <summary>Standardabzug für unbezahlte Pause (Checkbox).</summary>
    public const int UnpaidBreakDeductionMinutes = 30;

    public const int DefaultWorkPreparationMinutes = 10;

    public const int DefaultWorkFollowUpMinutes = 5;

    public static DutyTemplateStats Compute(DutyTemplate template) =>
        ComputeSummary(template);

    public static DutyTemplateStats ComputeSummary(DutyTemplate template)
    {
        var preparationMinutes = ResolvePreparationMinutes(template.WorkPreparationMinutes);
        var followUpMinutes = ResolveFollowUpMinutes(template.WorkFollowUpMinutes);
        var unpaidBreakDeductionPart1 = ResolveUnpaidBreakDeductionMinutes(template, 1);
        var unpaidBreakDeductionPart2 = ResolveUnpaidBreakDeductionMinutes(template, 2);
        var unpaidBreakDeductionPart3 = ResolveUnpaidBreakDeductionMinutes(template, 3);

        if (template.Part3Rows.Count > 0 && template.IsDutyDivision)
        {
            return ComputeThreePartSummary(
                template.Rows,
                template.Part2Rows,
                template.Part3Rows,
                preparationMinutes,
                followUpMinutes,
                unpaidBreakDeductionPart1,
                unpaidBreakDeductionPart2,
                unpaidBreakDeductionPart3);
        }

        if (template.IsSplitShift && template.Part2Rows.Count > 0)
        {
            return ComputeSplitShiftSummary(
                template.Rows,
                template.Part2Rows,
                preparationMinutes,
                followUpMinutes,
                unpaidBreakDeductionPart1);
        }

        if (template.Part2Rows.Count > 0)
        {
            return ComputeSplitSummary(
                template.Rows,
                template.Part2Rows,
                preparationMinutes,
                followUpMinutes,
                unpaidBreakDeductionPart1,
                unpaidBreakDeductionPart2);
        }

        return ComputeSingle(template.Rows, preparationMinutes, followUpMinutes, unpaidBreakDeductionPart1);
    }

    public static DutyTemplateStats ComputePart(
        IReadOnlyList<DutyTemplateRow> rows,
        int preparationMinutes,
        int followUpMinutes,
        int unpaidBreakDeductionMinutes = 0)
    {
        var prep = preparationMinutes > 0 ? preparationMinutes : 0;
        var followUp = followUpMinutes > 0 ? followUpMinutes : 0;
        return ComputeSingle(rows, prep, followUp, unpaidBreakDeductionMinutes);
    }

    public static int ComputePartDuration(
        IReadOnlyList<DutyTemplateRow> rows,
        int preparationMinutes = 0,
        int followUpMinutes = 0) =>
        ComputeServiceDurationMinutes(rows, preparationMinutes, followUpMinutes);

    /// <summary>
    /// Dienstlänge = früheste Abfahrt bis späteste Ankunft inkl. Arbeitsvorbereitung und -nachbereitung.
    /// </summary>
    public static int ComputeServiceDurationMinutes(
        IReadOnlyList<DutyTemplateRow> rows,
        int preparationMinutes = 0,
        int followUpMinutes = 0)
    {
        var drivingRows = GetDrivingRows(rows);
        if (drivingRows.Count == 0)
        {
            return Math.Max(0, preparationMinutes + followUpMinutes);
        }

        var spansMidnight = SpansOperatingDayMidnight(drivingRows);
        var startKey = GetOperatingDayStartKey(drivingRows, spansMidnight);
        var endKey = GetOperatingDayEndKey(drivingRows, spansMidnight);
        if (startKey is null || endKey is null)
        {
            return Math.Max(0, preparationMinutes + followUpMinutes);
        }

        var dutyStartKey = startKey.Value - Math.Max(0, preparationMinutes);
        var dutyEndKey = endKey.Value + Math.Max(0, followUpMinutes);
        if (dutyEndKey < dutyStartKey)
        {
            dutyEndKey += 24 * 60;
        }

        return Math.Max(0, dutyEndKey - dutyStartKey);
    }

    public static DutyTemplateStats ComputeThreePartSummary(
        IReadOnlyList<DutyTemplateRow> part1Rows,
        IReadOnlyList<DutyTemplateRow> part2Rows,
        IReadOnlyList<DutyTemplateRow> part3Rows,
        int preparationMinutes,
        int followUpMinutes,
        int unpaidBreakDeductionPart1,
        int unpaidBreakDeductionPart2,
        int unpaidBreakDeductionPart3)
    {
        var part1 = ComputePart(part1Rows, preparationMinutes, followUpMinutes, unpaidBreakDeductionPart1);
        var part2 = ComputePart(part2Rows, preparationMinutes, followUpMinutes, unpaidBreakDeductionPart2);
        var part3 = ComputePart(part3Rows, preparationMinutes, followUpMinutes, unpaidBreakDeductionPart3);

        return new DutyTemplateStats
        {
            ServiceDurationMinutes = part1.ServiceDurationMinutes + part2.ServiceDurationMinutes + part3.ServiceDurationMinutes,
            PayMinutes = part1.PayMinutes + part2.PayMinutes + part3.PayMinutes,
            BreakMinutes = part1.BreakMinutes + part2.BreakMinutes + part3.BreakMinutes,
            DrivingMinutes = part1.DrivingMinutes + part2.DrivingMinutes + part3.DrivingMinutes,
            UnpaidBreakDeductedMinutes = unpaidBreakDeductionPart1 + unpaidBreakDeductionPart2 + unpaidBreakDeductionPart3,
            WorkPreparationMinutes = preparationMinutes * 3,
            WorkFollowUpMinutes = followUpMinutes * 3
        };
    }

    public static DutyTemplateStats ComputeSplitSummary(
        IReadOnlyList<DutyTemplateRow> part1Rows,
        IReadOnlyList<DutyTemplateRow> part2Rows,
        int preparationMinutes,
        int followUpMinutes,
        int unpaidBreakDeductionPart1,
        int unpaidBreakDeductionPart2)
    {
        var part1 = ComputePart(part1Rows, preparationMinutes, followUpMinutes, unpaidBreakDeductionPart1);
        var part2 = ComputePart(part2Rows, preparationMinutes, followUpMinutes, unpaidBreakDeductionPart2);
        var breakMinutes = part1.BreakMinutes + part2.BreakMinutes;
        var serviceDuration = part1.ServiceDurationMinutes + part2.ServiceDurationMinutes;
        var pay = part1.PayMinutes + part2.PayMinutes;

        return new DutyTemplateStats
        {
            ServiceDurationMinutes = serviceDuration,
            PayMinutes = pay,
            BreakMinutes = breakMinutes,
            DrivingMinutes = part1.DrivingMinutes + part2.DrivingMinutes,
            UnpaidBreakDeductedMinutes = unpaidBreakDeductionPart1 + unpaidBreakDeductionPart2,
            WorkPreparationMinutes = preparationMinutes * 2,
            WorkFollowUpMinutes = followUpMinutes * 2
        };
    }

    /// <summary>
    /// FPersV geteilter Dienst: Dienstschicht = Beginn Arbeitsteil 1 (inkl. Vorbereitung) bis Ende Arbeitsteil 2 (inkl. Nachbereitung).
    /// Arbeitsvorbereitung und -nachbereitung gelten je Arbeitsteil.
    /// </summary>
    public static DutyTemplateStats ComputeSplitShiftSummary(
        IReadOnlyList<DutyTemplateRow> part1Rows,
        IReadOnlyList<DutyTemplateRow> part2Rows,
        int preparationMinutes,
        int followUpMinutes,
        int unpaidBreakDeductionMinutes)
    {
        var prep = preparationMinutes > 0 ? preparationMinutes : 0;
        var followUp = followUpMinutes > 0 ? followUpMinutes : 0;
        var part1Work = ComputePart(part1Rows, prep, followUp, 0);
        var part2Work = ComputePart(part2Rows, prep, followUp, unpaidBreakDeductionMinutes);

        var part1Driving = GetDrivingRows(OrderRows(part1Rows));
        var part2Driving = GetDrivingRows(OrderRows(part2Rows));
        if (part1Driving.Count == 0 || part2Driving.Count == 0)
        {
            var fallbackDuration = part1Work.ServiceDurationMinutes + part2Work.ServiceDurationMinutes;
            var fallbackPay = part1Work.PayMinutes + part2Work.PayMinutes;
            return new DutyTemplateStats
            {
                ServiceDurationMinutes = fallbackDuration,
                PayMinutes = fallbackPay,
                BreakMinutes = part1Work.BreakMinutes + part2Work.BreakMinutes,
                DrivingMinutes = part1Work.DrivingMinutes + part2Work.DrivingMinutes,
                UnpaidBreakDeductedMinutes = unpaidBreakDeductionMinutes,
                WorkPreparationMinutes = prep * 2,
                WorkFollowUpMinutes = followUp * 2
            };
        }

        var spansMidnight = SpansOperatingDayMidnight(part1Driving.Concat(part2Driving).ToList());
        var part1StartKey = GetOperatingDayStartKey(part1Driving, spansMidnight);
        var part1EndKey = GetOperatingDayEndKey(part1Driving, spansMidnight);
        var part2StartKey = GetOperatingDayStartKey(part2Driving, spansMidnight);
        var part2EndKey = GetOperatingDayEndKey(part2Driving, spansMidnight);
        if (part1StartKey is null || part2EndKey is null || part1EndKey is null || part2StartKey is null)
        {
            return new DutyTemplateStats
            {
                ServiceDurationMinutes = part1Work.ServiceDurationMinutes + part2Work.ServiceDurationMinutes,
                PayMinutes = part1Work.PayMinutes + part2Work.PayMinutes,
                BreakMinutes = part1Work.BreakMinutes + part2Work.BreakMinutes,
                DrivingMinutes = part1Work.DrivingMinutes + part2Work.DrivingMinutes,
                UnpaidBreakDeductedMinutes = unpaidBreakDeductionMinutes,
                WorkPreparationMinutes = prep * 2,
                WorkFollowUpMinutes = followUp * 2
            };
        }

        var dutyStartKey = part1StartKey.Value - prep;
        var dutyEndKey = part2EndKey.Value + followUp;
        if (dutyEndKey < dutyStartKey)
        {
            dutyEndKey += 24 * 60;
        }

        var serviceDuration = Math.Max(0, dutyEndKey - dutyStartKey);
        var pay = unpaidBreakDeductionMinutes > 0
            ? Math.Max(0, serviceDuration - unpaidBreakDeductionMinutes)
            : serviceDuration;

        var part1SegmentEndKey = part1EndKey.Value + followUp;
        var part2SegmentStartKey = part2StartKey.Value - prep;
        if (part2SegmentStartKey < part1SegmentEndKey)
        {
            part2SegmentStartKey += 24 * 60;
        }

        var gapMinutes = Math.Max(0, part2SegmentStartKey - part1SegmentEndKey);

        return new DutyTemplateStats
        {
            ServiceDurationMinutes = serviceDuration,
            PayMinutes = pay,
            BreakMinutes = part1Work.BreakMinutes + part2Work.BreakMinutes + gapMinutes,
            DrivingMinutes = part1Work.DrivingMinutes + part2Work.DrivingMinutes,
            UnpaidBreakDeductedMinutes = unpaidBreakDeductionMinutes,
            WorkPreparationMinutes = prep * 2,
            WorkFollowUpMinutes = followUp * 2
        };
    }

    private static DutyTemplateStats ComputeSingle(
        IReadOnlyList<DutyTemplateRow> rows,
        int preparationMinutes,
        int followUpMinutes,
        int unpaidBreakDeductionMinutes)
    {
        var ordered = OrderRows(rows);
        var drivingRows = GetDrivingRows(ordered);
        if (drivingRows.Count == 0)
        {
            var total = preparationMinutes + followUpMinutes;
            var emptyPay = unpaidBreakDeductionMinutes > 0
                ? Math.Max(0, total - unpaidBreakDeductionMinutes)
                : total;

            return new DutyTemplateStats
            {
                ServiceDurationMinutes = total,
                PayMinutes = emptyPay,
                DrivingMinutes = 0,
                UnpaidBreakDeductedMinutes = unpaidBreakDeductionMinutes,
                WorkPreparationMinutes = preparationMinutes,
                WorkFollowUpMinutes = followUpMinutes
            };
        }

        var spansMidnight = SpansOperatingDayMidnight(drivingRows);
        var segmentMinutes = 0;
        var breakMinutes = 0;

        for (var i = 0; i < drivingRows.Count; i++)
        {
            var row = drivingRows[i];
            var from = ParseMinutes(row.FromTime);
            var to = ParseMinutes(row.ToTime);
            if (from is null || to is null)
            {
                continue;
            }

            var fromKey = ToOperatingDaySortKey(from.Value, spansMidnight);
            var toKey = ToOperatingDaySortKey(to.Value, spansMidnight);
            if (toKey < fromKey)
            {
                toKey += 24 * 60;
            }

            segmentMinutes += Math.Max(0, toKey - fromKey);

            if (i > 0)
            {
                var prevTo = ParseMinutes(drivingRows[i - 1].ToTime);
                if (prevTo is not null)
                {
                    var prevEndKey = ToOperatingDaySortKey(prevTo.Value, spansMidnight);
                    var gap = fromKey - prevEndKey;
                    if (gap > 0)
                    {
                        breakMinutes += gap;
                    }
                }
            }
        }

        var envelope = ComputeEnvelopeMinutes(ordered);
        if (envelope == 0 && segmentMinutes > 0)
        {
            envelope = segmentMinutes;
        }

        var serviceDuration = ComputeServiceDurationMinutes(ordered, preparationMinutes, followUpMinutes);
        if (serviceDuration == 0 && envelope > 0)
        {
            serviceDuration = envelope + preparationMinutes + followUpMinutes;
        }

        var pay = serviceDuration;
        if (unpaidBreakDeductionMinutes > 0)
        {
            pay = Math.Max(0, pay - unpaidBreakDeductionMinutes);
        }

        return new DutyTemplateStats
        {
            ServiceDurationMinutes = serviceDuration,
            PayMinutes = pay,
            BreakMinutes = breakMinutes,
            DrivingMinutes = segmentMinutes,
            UnpaidBreakDeductedMinutes = unpaidBreakDeductionMinutes,
            WorkPreparationMinutes = preparationMinutes,
            WorkFollowUpMinutes = followUpMinutes
        };
    }

    public static int ComputeEnvelopeMinutes(IReadOnlyList<DutyTemplateRow> rows)
    {
        var drivingRows = GetDrivingRows(rows);
        if (drivingRows.Count == 0)
        {
            return 0;
        }

        var spansMidnight = SpansOperatingDayMidnight(drivingRows);
        var envelopeStart = GetOperatingDayStartKey(drivingRows, spansMidnight);
        var envelopeEnd = GetOperatingDayEndKey(drivingRows, spansMidnight);
        if (envelopeStart is null || envelopeEnd is null)
        {
            return 0;
        }

        return Math.Max(0, envelopeEnd.Value - envelopeStart.Value);
    }

    /// <summary>
    /// Fahrten mit ab/an-Zeiten, inkl. Leerfahrten; ohne Leerzeilen.
    /// </summary>
    public static bool IsDrivingRow(DutyTemplateRow row) =>
        !DutyTemplateRemarkHelper.IsLeerzeile(row.Remark)
        && ParseMinutes(row.FromTime) is not null
        && ParseMinutes(row.ToTime) is not null;

    private static List<DutyTemplateRow> GetDrivingRows(IReadOnlyList<DutyTemplateRow> rows) =>
        OrderRows(rows).Where(IsDrivingRow).ToList();

    public static int ResolvePreparationMinutes(int minutes) =>
        minutes > 0 ? minutes : DefaultWorkPreparationMinutes;

    public static int ResolveFollowUpMinutes(int minutes) =>
        minutes > 0 ? minutes : DefaultWorkFollowUpMinutes;

    public static int ResolveUnpaidBreakDeductionMinutes(DutyTemplate template, int part = 1)
    {
        var custom = Math.Max(0, template.CustomUnpaidBreakDeductionMinutes);
        var useStandard = part switch
        {
            2 => template.SubtractUnpaidBreak30MinutesPart2,
            3 => template.SubtractUnpaidBreak30MinutesPart3,
            _ => template.SubtractUnpaidBreak30Minutes
        };
        var standard = useStandard ? UnpaidBreakDeductionMinutes : 0;
        return standard + custom;
    }

    public static int ResolveUnpaidBreakDeductionMinutes(DutyTemplate template) =>
        ResolveUnpaidBreakDeductionMinutes(template, 1);

    public static int ParseNonNegativeMinutes(string? text, int fallback)
    {
        return int.TryParse(text?.Trim(), out var minutes) && minutes >= 0
            ? minutes
            : fallback;
    }

    public static List<DutyTemplateRow> OrderRows(IEnumerable<DutyTemplateRow> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return list;
        }

        var spansMidnight = SpansOperatingDayMidnight(list);
        return list
            .OrderBy(row => ToOperatingDaySortKey(ParseMinutes(row.FromTime) ?? int.MaxValue, spansMidnight))
            .ThenBy(row => ToOperatingDaySortKey(ParseMinutes(row.ToTime) ?? int.MaxValue, spansMidnight))
            .ThenBy(row => DutyTemplateErsatzfahrplanParser.CompareTripNumberSortKey(row.TripNumber))
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static int ToOperatingDaySortKey(int minutes, bool spansOperatingDayMidnight)
    {
        if (spansOperatingDayMidnight && minutes < OperatingDayStartMinutes)
        {
            return minutes + 24 * 60;
        }

        return minutes;
    }

    public static bool SpansOperatingDayMidnight(IReadOnlyList<DutyTemplateRow> rows)
    {
        var fromTimes = rows
            .Select(row => ParseMinutes(row.FromTime))
            .Where(minutes => minutes.HasValue)
            .Select(minutes => minutes!.Value)
            .ToList();

        return SpansOperatingDayMidnight(fromTimes);
    }

    public static bool SpansOperatingDayMidnight(IReadOnlyList<int> minutesFromMidnight)
    {
        if (minutesFromMidnight.Count == 0)
        {
            return false;
        }

        return minutesFromMidnight.Any(minutes => minutes >= EveningTripThresholdMinutes)
               && minutesFromMidnight.Any(minutes => minutes < OperatingDayStartMinutes);
    }

    public static int? ParseMinutes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = TimeRegex.Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (hours is < 0 or > 47 || minutes is < 0 or > 59)
        {
            return null;
        }

        return hours * 60 + minutes;
    }

    public static string FormatMinutes(int totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return "0:00";
        }

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"{hours}:{minutes:00}";
    }

    public static string FormatClockDisplay(int minutes)
    {
        minutes = ((minutes % (24 * 60)) + (24 * 60)) % (24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    public static string? GetServiceStartDisplay(IEnumerable<DutyTemplateRow> rows, int preparationMinutes = 0)
    {
        var drivingRows = GetDrivingRows(rows.ToList());
        if (drivingRows.Count == 0)
        {
            return null;
        }

        var spansMidnight = SpansOperatingDayMidnight(drivingRows);
        var startKey = GetOperatingDayStartKey(drivingRows, spansMidnight);
        if (startKey is null)
        {
            return null;
        }

        var dutyStartKey = startKey.Value - (preparationMinutes > 0 ? preparationMinutes : 0);
        return FormatClockDisplay(dutyStartKey);
    }

    public static string? GetServiceEndDisplay(IEnumerable<DutyTemplateRow> rows, int followUpMinutes = 0)
    {
        var drivingRows = GetDrivingRows(rows.ToList());
        if (drivingRows.Count == 0)
        {
            return null;
        }

        var spansMidnight = SpansOperatingDayMidnight(drivingRows);
        var endKey = GetOperatingDayEndKey(drivingRows, spansMidnight);
        if (endKey is null)
        {
            return null;
        }

        var dutyEndKey = endKey.Value + (followUpMinutes > 0 ? followUpMinutes : 0);
        return FormatClockDisplay(dutyEndKey);
    }

    private static int? GetOperatingDayStartKey(IReadOnlyList<DutyTemplateRow> ordered, bool spansMidnight)
    {
        int? minKey = null;
        foreach (var row in ordered)
        {
            var from = ParseMinutes(row.FromTime);
            if (from is null)
            {
                continue;
            }

            var key = ToOperatingDaySortKey(from.Value, spansMidnight);
            minKey = minKey is null ? key : Math.Min(minKey.Value, key);
        }

        return minKey;
    }

    private static int? GetOperatingDayEndKey(IReadOnlyList<DutyTemplateRow> ordered, bool spansMidnight)
    {
        int? maxKey = null;
        foreach (var row in ordered)
        {
            var to = ParseMinutes(row.ToTime);
            if (to is null)
            {
                continue;
            }

            var toKey = ToOperatingDaySortKey(to.Value, spansMidnight);
            var from = ParseMinutes(row.FromTime);
            if (from is not null)
            {
                var fromKey = ToOperatingDaySortKey(from.Value, spansMidnight);
                if (toKey < fromKey)
                {
                    toKey += 24 * 60;
                }
            }

            maxKey = maxKey is null ? toKey : Math.Max(maxKey.Value, toKey);
        }

        return maxKey;
    }
}
