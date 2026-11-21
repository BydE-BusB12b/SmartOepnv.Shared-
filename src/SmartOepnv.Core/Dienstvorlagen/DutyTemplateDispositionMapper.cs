using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Überträgt Zeiten aus einer Dienstvorlage auf ein Kalenderdatum für die Fahrerdisposition.</summary>
public static class DutyTemplateDispositionMapper
{
    public sealed record MappedPartShift(
        int PartIndex,
        DateTime StartLocal,
        DateTime EndLocal,
        string DutyNumber,
        DateTime? Part1EndLocal = null,
        DateTime? Part2StartLocal = null)
    {
        public bool IsSplitShift =>
            Part1EndLocal.HasValue
            && Part2StartLocal.HasValue
            && Part2StartLocal.Value > Part1EndLocal.Value;
    }

    public static string ResolveDutyNumberForPart(DutyTemplate template, int partIndex) =>
        template.IsSplitShift
            ? template.DutyNumber.Trim()
            : partIndex switch
            {
                1 => template.DutyNumber.Trim(),
                2 => template.DutyNumberPart2.Trim(),
                3 => template.DutyNumberPart3.Trim(),
                _ => string.Empty
            };

    public static string ResolveDutyNumberDisplay(DutyTemplate template)
    {
        var part1 = template.DutyNumber.Trim();
        if (part1.Length == 0)
        {
            return string.Empty;
        }

        if (template.IsSplitShift && template.Part2Rows.Count > 0)
        {
            return part1;
        }

        var part2 = template.DutyNumberPart2.Trim();
        var part3 = template.DutyNumberPart3.Trim();
        if (part3.Length > 0)
        {
            return $"{part1} + {part2} + {part3}";
        }

        if (part2.Length > 0)
        {
            return $"{part1} + {part2}";
        }

        return part1;
    }

    public static int CountDispatchParts(DutyTemplate template)
    {
        if (template.IsSplitShift && template.Part2Rows.Count > 0 && template.Rows.Count > 0)
        {
            return 1;
        }

        var count = 0;
        if (template.Rows.Count > 0)
        {
            count++;
        }

        if (template.Part2Rows.Count > 0)
        {
            count++;
        }

        if (template.Part3Rows.Count > 0)
        {
            count++;
        }

        return count;
    }

    public static MappedPartShift? TryMapPart(DutyTemplate template, DateTime dutyDate, int partIndex)
    {
        if (template.IsSplitShift && partIndex == 1)
        {
            return TryMapSplitShift(template, dutyDate);
        }

        if (!TryResolvePart(template, partIndex, out var rows, out var dutyNumber, out var preparationMinutes, out var followUpMinutes))
        {
            return null;
        }

        var startStr = DutyTemplateCalculator.GetServiceStartDisplay(rows, preparationMinutes);
        var endStr = DutyTemplateCalculator.GetServiceEndDisplay(rows, followUpMinutes);
        if (startStr is null || endStr is null)
        {
            return null;
        }

        var start = ToLocalDateTime(dutyDate, startStr, rows);
        var end = ToLocalDateTime(dutyDate, endStr, rows, minAfter: start);
        if (end <= start)
        {
            return null;
        }

        return new MappedPartShift(partIndex, start, end, dutyNumber);
    }

    public static MappedPartShift? TryMapSplitShift(DutyTemplate template, DateTime dutyDate)
    {
        if (!template.IsSplitShift || template.Rows.Count == 0 || template.Part2Rows.Count == 0)
        {
            return null;
        }

        var preparationMinutes = DutyTemplateCalculator.ResolvePreparationMinutes(template.WorkPreparationMinutes);
        var followUpMinutes = DutyTemplateCalculator.ResolveFollowUpMinutes(template.WorkFollowUpMinutes);
        var dutyNumber = template.DutyNumber.Trim();
        if (dutyNumber.Length == 0)
        {
            return null;
        }

        var part1StartStr = DutyTemplateCalculator.GetServiceStartDisplay(template.Rows, preparationMinutes);
        var part1EndStr = DutyTemplateCalculator.GetServiceEndDisplay(template.Rows, followUpMinutes);
        var part2StartStr = DutyTemplateCalculator.GetServiceStartDisplay(template.Part2Rows, preparationMinutes);
        var part2EndStr = DutyTemplateCalculator.GetServiceEndDisplay(template.Part2Rows, followUpMinutes);
        if (part1StartStr is null || part1EndStr is null || part2StartStr is null || part2EndStr is null)
        {
            return null;
        }

        var contextRows = template.Rows.Concat(template.Part2Rows).ToList();
        var part1Start = ToLocalDateTime(dutyDate, part1StartStr, contextRows);
        var part1End = ToLocalDateTime(dutyDate, part1EndStr, contextRows, minAfter: part1Start);
        var part2Start = ToLocalDateTime(dutyDate, part2StartStr, contextRows, minAfter: part1End);
        var part2End = ToLocalDateTime(dutyDate, part2EndStr, contextRows, minAfter: part2Start);
        if (part2End <= part1Start || part2Start <= part1End)
        {
            return null;
        }

        return new MappedPartShift(
            1,
            part1Start,
            part2End,
            dutyNumber,
            part1End,
            part2Start);
    }

    public static bool TryValidateSplitShiftStructure(DutyTemplate template, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!template.IsSplitShift || template.Part2Rows.Count == 0)
        {
            return true;
        }

        var mapped = TryMapSplitShift(template, DateTime.Today);
        if (mapped is null)
        {
            errorMessage = "Zeiten für geteilten Dienst unvollständig oder ungültig.";
            return false;
        }

        return SplitShiftCompliance.TryValidateStructure(
            new DateTimeOffset(mapped.StartLocal).ToUnixTimeMilliseconds(),
            new DateTimeOffset(mapped.Part1EndLocal!.Value).ToUnixTimeMilliseconds(),
            new DateTimeOffset(mapped.Part2StartLocal!.Value).ToUnixTimeMilliseconds(),
            new DateTimeOffset(mapped.EndLocal).ToUnixTimeMilliseconds(),
            out errorMessage);
    }

    public static DutyTemplateStats? TryGetPartStats(DutyTemplate template, int partIndex)
    {
        if (template.IsSplitShift && partIndex == 1)
        {
            var preparationMinutes = DutyTemplateCalculator.ResolvePreparationMinutes(template.WorkPreparationMinutes);
            var followUpMinutes = DutyTemplateCalculator.ResolveFollowUpMinutes(template.WorkFollowUpMinutes);
            var unpaidBreak = DutyTemplateCalculator.ResolveUnpaidBreakDeductionMinutes(template, 1);
            return DutyTemplateCalculator.ComputeSplitShiftSummary(
                template.Rows,
                template.Part2Rows,
                preparationMinutes,
                followUpMinutes,
                unpaidBreak);
        }

        if (!TryResolvePart(template, partIndex, out var rows, out _, out var preparationMinutesPart, out var followUpMinutesPart))
        {
            return null;
        }

        var unpaidBreakPart = DutyTemplateCalculator.ResolveUnpaidBreakDeductionMinutes(template, partIndex);
        return DutyTemplateCalculator.ComputePart(rows, preparationMinutesPart, followUpMinutesPart, unpaidBreakPart);
    }

    public static int? TryGetPartDrivingMinutes(DutyTemplate template, int partIndex) =>
        TryGetPartStats(template, partIndex)?.DrivingMinutes;

    public static IReadOnlyList<MappedPartShift> TryMapAllParts(DutyTemplate template, DateTime dutyDate)
    {
        if (template.IsSplitShift && template.Part2Rows.Count > 0)
        {
            var mapped = TryMapSplitShift(template, dutyDate);
            return mapped is null ? [] : [mapped];
        }

        var parts = new List<MappedPartShift>();
        foreach (var partIndex in EnumeratePartIndexes(template))
        {
            var mapped = TryMapPart(template, dutyDate, partIndex);
            if (mapped is not null)
            {
                parts.Add(mapped);
            }
        }

        return parts;
    }

    public static IEnumerable<int> EnumeratePartIndexes(DutyTemplate template)
    {
        if (template.IsSplitShift && template.Part2Rows.Count > 0 && template.Rows.Count > 0)
        {
            yield return 1;
            yield break;
        }

        if (template.Rows.Count > 0)
        {
            yield return 1;
        }

        if (template.Part2Rows.Count > 0)
        {
            yield return 2;
        }

        if (template.Part3Rows.Count > 0)
        {
            yield return 3;
        }
    }

    private static bool TryResolvePart(
        DutyTemplate template,
        int partIndex,
        out IReadOnlyList<DutyTemplateRow> rows,
        out string dutyNumber,
        out int preparationMinutes,
        out int followUpMinutes)
    {
        rows = [];
        dutyNumber = string.Empty;
        preparationMinutes = 0;
        followUpMinutes = 0;

        if (template.IsSplitShift)
        {
            return false;
        }

        switch (partIndex)
        {
            case 1 when template.Rows.Count > 0:
                rows = template.Rows;
                dutyNumber = template.DutyNumber.Trim();
                preparationMinutes = template.WorkPreparationMinutes;
                followUpMinutes = IsLastPart(template, 1) ? template.WorkFollowUpMinutes : 0;
                return rows.Count > 0;

            case 2 when template.Part2Rows.Count > 0:
                rows = template.Part2Rows;
                dutyNumber = template.DutyNumberPart2.Trim();
                followUpMinutes = IsLastPart(template, 2) ? template.WorkFollowUpMinutes : 0;
                return rows.Count > 0;

            case 3 when template.Part3Rows.Count > 0:
                rows = template.Part3Rows;
                dutyNumber = template.DutyNumberPart3.Trim();
                followUpMinutes = template.WorkFollowUpMinutes;
                return rows.Count > 0;

            default:
                return false;
        }
    }

    private static bool IsLastPart(DutyTemplate template, int partIndex) =>
        partIndex switch
        {
            1 => template.Part2Rows.Count == 0 && template.Part3Rows.Count == 0,
            2 => template.Part3Rows.Count == 0,
            3 => true,
            _ => false
        };

    private static DateTime ToLocalDateTime(
        DateTime dutyDate,
        string clockDisplay,
        IReadOnlyList<DutyTemplateRow> contextRows,
        DateTime? minAfter = null)
    {
        var minutes = DutyTemplateCalculator.ParseMinutes(clockDisplay) ?? 0;
        var spansMidnight = DutyTemplateCalculator.SpansOperatingDayMidnight(contextRows);
        var dt = dutyDate.Date.AddMinutes(minutes);
        if (spansMidnight && minutes < DutyTemplateCalculator.OperatingDayStartMinutes)
        {
            dt = dt.AddDays(1);
        }

        if (minAfter is not null && dt <= minAfter.Value)
        {
            dt = dt.AddDays(1);
        }

        return dt;
    }
}
