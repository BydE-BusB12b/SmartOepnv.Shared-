namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Überträgt Zeiten aus einer Dienstvorlage auf ein Kalenderdatum für die Fahrerdisposition.</summary>
public static class DutyTemplateDispositionMapper
{
    public sealed record MappedPartShift(
        int PartIndex,
        DateTime StartLocal,
        DateTime EndLocal,
        string DutyNumber);

    public static string ResolveDutyNumberForPart(DutyTemplate template, int partIndex) =>
        partIndex switch
        {
            1 => template.DutyNumber.Trim(),
            2 => template.DutyNumberPart2.Trim(),
            3 => template.DutyNumberPart3.Trim(),
            _ => string.Empty
        };

    public static string ResolveDutyNumberDisplay(DutyTemplate template)
    {
        var part1 = template.DutyNumber.Trim();
        var part2 = template.DutyNumberPart2.Trim();
        var part3 = template.DutyNumberPart3.Trim();
        if (part1.Length == 0)
        {
            return string.Empty;
        }

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

    public static IReadOnlyList<MappedPartShift> TryMapAllParts(DutyTemplate template, DateTime dutyDate)
    {
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
