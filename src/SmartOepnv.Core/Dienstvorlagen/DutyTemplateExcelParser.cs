using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>
/// Parser für SEV-/Ersatzfahrpläne direkt aus Excel (.xlsx):
/// Zeilen = Haltestellen, Spalten = Kurse mit ab/an-Zeiten.
/// </summary>
public static class DutyTemplateExcelParser
{
    private static readonly Regex TimeTokenRegex = new(@"\b(\d{1,2})[.:](\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex FahrtnrRegex = new(@"\b(\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex BusPrefixRegex = new(@"^Bus\s*(\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ValidityRegex = new(@"Gültig\s+vom\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static DutyTemplateImportResult ParseWithHints(string filePath)
    {
        var sheet = DutyTemplateExcelSheetReader.ReadFirstSheet(filePath);
        var blocks = ExtractDirectionBlocks(sheet);
        return DutyTemplateErsatzfahrplanParser.BuildImportResultFromBlocks(blocks, filePath, ExtractValidity(sheet));
    }

    internal static List<DutyTemplateErsatzfahrplanParser.DirectionBlock> ExtractDirectionBlocks(
        DutyTemplateExcelSheetReader.ExcelSheetData sheet)
    {
        var blocks = new List<DutyTemplateErsatzfahrplanParser.DirectionBlock>();
        DutyTemplateErsatzfahrplanParser.DirectionBlock? current = null;
        List<int> courseColumns = [];

        for (var row = 1; row <= sheet.MaxRow; row++)
        {
            var rowText = string.Join(" ", sheet.RowTexts(row, 1, Math.Max(sheet.MaxCol, 23)));
            if (string.IsNullOrWhiteSpace(rowText))
            {
                continue;
            }

            if (IsRouteHeaderRow(rowText))
            {
                if (current is { Rows.Count: > 0 })
                {
                    blocks.Add(current);
                }

                current = new DutyTemplateErsatzfahrplanParser.DirectionBlock
                {
                    RouteLabel = rowText.Trim()
                };
                courseColumns = [];
                continue;
            }

            if (IsFahrtnrHeaderRow(sheet, row, out var fahrtNumbers, out courseColumns))
            {
                if (current is { Rows.Count: > 0 })
                {
                    blocks.Add(current);
                    current = new DutyTemplateErsatzfahrplanParser.DirectionBlock
                    {
                        VehicleNumber = current.VehicleNumber
                    };
                }

                current ??= new DutyTemplateErsatzfahrplanParser.DirectionBlock();
                current.FahrtNumbers = fahrtNumbers;
                current.CourseColumns = courseColumns.ToList();
                var vehicle = ExtractVehicleNumber(sheet, row);
                if (!string.IsNullOrWhiteSpace(vehicle))
                {
                    current.VehicleNumber = vehicle;
                }

                continue;
            }

            var tableRow = TryParseTableRow(sheet, row, courseColumns);
            if (tableRow is null)
            {
                continue;
            }

            current ??= new DutyTemplateErsatzfahrplanParser.DirectionBlock();
            if (current.FahrtNumbers.Count == 0)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(current.VehicleNumber))
            {
                var vehicle = ExtractVehicleNumber(sheet, row);
                if (!string.IsNullOrWhiteSpace(vehicle))
                {
                    current.VehicleNumber = vehicle;
                }
            }

            current.Rows.Add(tableRow with { SourceLineNumber = row });
        }

        if (current is { Rows.Count: > 0 })
        {
            blocks.Add(current);
        }

        return blocks;
    }

    private static string ExtractValidity(DutyTemplateExcelSheetReader.ExcelSheetData sheet)
    {
        for (var row = 1; row <= Math.Min(sheet.MaxRow, 6); row++)
        {
            for (var col = 1; col <= Math.Max(sheet.MaxCol, 20); col++)
            {
                var cell = sheet.GetCell(row, col);
                if (string.IsNullOrWhiteSpace(cell))
                {
                    continue;
                }

                var match = ValidityRegex.Match(cell);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }

                if (cell.Contains("Gültig vom", StringComparison.OrdinalIgnoreCase))
                {
                    return cell.Replace("Gültig vom", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractVehicleNumber(DutyTemplateExcelSheetReader.ExcelSheetData sheet, int row)
    {
        for (var scanRow = row; scanRow >= Math.Max(1, row - 6); scanRow--)
        {
            var busCell = sheet.GetCell(scanRow, 1);
            var match = BusPrefixRegex.Match(busCell);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return string.Empty;
    }

    private static bool IsRouteHeaderRow(string line)
    {
        if (!ContainsRouteArrow(line))
        {
            return false;
        }

        if (line.Contains("(H)", StringComparison.Ordinal))
        {
            return false;
        }

        return !Regex.IsMatch(line, @"\b(ab|an)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsRouteArrow(string line) =>
        line.Contains('→', StringComparison.Ordinal)
        || line.Contains("->", StringComparison.OrdinalIgnoreCase);

    private static bool IsFahrtnrHeaderRow(
        DutyTemplateExcelSheetReader.ExcelSheetData sheet,
        int row,
        out List<string> fahrtNumbers,
        out List<int> courseColumns)
    {
        fahrtNumbers = [];
        courseColumns = [];
        var rowText = string.Join(" ", sheet.RowTexts(row, 1, Math.Max(sheet.MaxCol, 23)));
        if (!rowText.Contains("Ersatzhaltestelle", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var col = 5; col <= Math.Max(sheet.MaxCol, 23); col++)
        {
            var value = sheet.GetCell(row, col);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var match = FahrtnrRegex.Match(value);
            if (!match.Success)
            {
                continue;
            }

            fahrtNumbers.Add(match.Groups[1].Value);
            courseColumns.Add(col);
        }

        if (fahrtNumbers.Count == 0)
        {
            return false;
        }

        ExpandCourseColumns(sheet, row, ref fahrtNumbers, ref courseColumns);
        return true;
    }

    private static void ExpandCourseColumns(
        DutyTemplateExcelSheetReader.ExcelSheetData sheet,
        int row,
        ref List<string> fahrtNumbers,
        ref List<int> courseColumns)
    {
        var firstCol = courseColumns[0];
        var lastCol = courseColumns[^1];
        var expandedNumbers = new List<string>();
        var expandedColumns = new List<int>();

        for (var col = firstCol; col <= lastCol; col++)
        {
            expandedColumns.Add(col);
            var value = sheet.GetCell(row, col);
            if (string.IsNullOrWhiteSpace(value))
            {
                expandedNumbers.Add(string.Empty);
                continue;
            }

            var match = FahrtnrRegex.Match(value);
            expandedNumbers.Add(match.Success ? match.Groups[1].Value : string.Empty);
        }

        fahrtNumbers = expandedNumbers;
        courseColumns = expandedColumns;
    }

    private static DutyTemplateErsatzfahrplanParser.ErsatzfahrplanTableRow? TryParseTableRow(
        DutyTemplateExcelSheetReader.ExcelSheetData sheet,
        int row,
        IReadOnlyList<int> courseColumns)
    {
        var shortStop = sheet.GetCell(row, 2);
        var longStop = sheet.GetCell(row, 3);
        var direction = sheet.GetCell(row, 4).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(shortStop) && string.IsNullOrWhiteSpace(longStop))
        {
            return null;
        }

        if (!IsDirectionToken(direction))
        {
            return null;
        }

        var times = new List<string>();
        if (courseColumns.Count > 0)
        {
            foreach (var col in courseColumns)
            {
                var value = sheet.GetCell(row, col);
                times.Add(TimeTokenRegex.IsMatch(value) ? NormalizeTime(value) : string.Empty);
            }
        }
        else
        {
            for (var col = 5; col <= Math.Max(sheet.MaxCol, 23); col++)
            {
                var value = sheet.GetCell(row, col);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (TimeTokenRegex.IsMatch(value))
                {
                    times.Add(NormalizeTime(value));
                }
            }
        }

        if (times.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        if (IsGarbageStopRow(shortStop, longStop))
        {
            return null;
        }

        var rawLine = $"{shortStop} | {longStop} | {direction} | {string.Join(' ', times)}";
        return new DutyTemplateErsatzfahrplanParser.ErsatzfahrplanTableRow(
            row,
            rawLine,
            shortStop,
            longStop,
            direction,
            times);
    }

    private static string NormalizeTime(string value)
    {
        var match = TimeTokenRegex.Match(value.Trim());
        if (!match.Success)
        {
            return value.Trim();
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = match.Groups[2].Value;
        return $"{hours:00}:{minutes}";
    }

    private static bool IsDirectionToken(string value) =>
        value.Equals("ab", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("an", StringComparison.OrdinalIgnoreCase);

    private static bool IsGarbageStopRow(string shortStop, string longStop)
    {
        var combined = $"{shortStop} {longStop}".Trim();
        if (combined.Length == 0)
        {
            return true;
        }

        if (combined.Contains("Bahnhof", StringComparison.OrdinalIgnoreCase) &&
            combined.Contains("Ersatzhaltestelle", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return combined.Contains("Haltestelle", StringComparison.OrdinalIgnoreCase) &&
               !TimeTokenRegex.IsMatch(combined);
    }
}
