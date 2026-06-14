using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Liest Zellwerte aus der ersten Excel-Arbeitsmappe (inkl. verbundener Zellen).</summary>
internal static class DutyTemplateExcelSheetReader
{
    private static readonly Regex CellReferenceRegex = new(
        @"^(?<col>[A-Z]+)(?<row>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ExcelSheetData ReadFirstSheet(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart
            ?? throw new InvalidOperationException("Excel-Datei enthält keine Arbeitsmappe.");

        var worksheetPart = workbookPart.WorksheetParts.FirstOrDefault()
            ?? throw new InvalidOperationException("Excel-Datei enthält kein Arbeitsblatt.");

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault()
            ?? throw new InvalidOperationException("Excel-Arbeitsblatt ist leer.");

        var rawCells = new Dictionary<(int Row, int Col), string>();
        var maxRow = 0;
        var maxCol = 0;

        foreach (var row in sheetData.Elements<Row>())
        {
            var rowIndex = row.RowIndex is null
                ? 0
                : (int)row.RowIndex.Value;

            if (rowIndex <= 0)
            {
                continue;
            }

            foreach (var cell in row.Elements<Cell>())
            {
                var reference = cell.CellReference?.Value;
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                var (parsedRow, parsedCol) = ParseCellReference(reference);
                if (parsedRow <= 0 || parsedCol <= 0)
                {
                    continue;
                }

                var value = ReadCellValue(cell, sharedStrings);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                rawCells[(parsedRow, parsedCol)] = value.Trim();
                maxRow = Math.Max(maxRow, parsedRow);
                maxCol = Math.Max(maxCol, parsedCol);
            }
        }

        var mergedRanges = worksheetPart.Worksheet.Elements<MergeCells>()
            .SelectMany(group => group.Elements<MergeCell>())
            .Select(merge => merge.Reference?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(ParseRange!)
            .ToList();

        var resolved = new Dictionary<(int Row, int Col), string>(rawCells);
        foreach (var range in mergedRanges)
        {
            if (!rawCells.TryGetValue((range.TopRow, range.LeftCol), out var mergedValue))
            {
                continue;
            }

            for (var row = range.TopRow; row <= range.BottomRow; row++)
            {
                for (var col = range.LeftCol; col <= range.RightCol; col++)
                {
                    resolved[(row, col)] = mergedValue;
                }
            }
        }

        return new ExcelSheetData(resolved, maxRow, maxCol);
    }

    private static string ReadCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var text = cell.InnerText?.Trim() ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings is not null)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                var item = sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(index);
                return item?.InnerText?.Trim() ?? string.Empty;
            }
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText?.Trim() ?? string.Empty;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return FormatNumericCell(number);
        }

        return text;
    }

    internal static string FormatNumericCell(double number)
    {
        if (number is >= 0 and < 1)
        {
            var totalMinutes = (int)Math.Round(number * 24 * 60, MidpointRounding.AwayFromZero);
            totalMinutes = ((totalMinutes % (24 * 60)) + (24 * 60)) % (24 * 60);
            return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
        }

        if (Math.Abs(number - Math.Round(number)) < 0.000001)
        {
            return ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture);
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static (int Row, int Col) ParseCellReference(string reference)
    {
        var match = CellReferenceRegex.Match(reference.Trim().ToUpperInvariant());
        if (!match.Success)
        {
            return (0, 0);
        }

        var row = int.Parse(match.Groups["row"].Value, CultureInfo.InvariantCulture);
        var col = ColumnLettersToIndex(match.Groups["col"].Value);
        return (row, col);
    }

    private static ExcelCellRange ParseRange(string reference)
    {
        var parts = reference.Split(':', StringSplitOptions.TrimEntries);
        var start = ParseCellReference(parts[0]);
        var end = parts.Length > 1 ? ParseCellReference(parts[1]) : start;
        return new ExcelCellRange(
            Math.Min(start.Row, end.Row),
            Math.Max(start.Row, end.Row),
            Math.Min(start.Col, end.Col),
            Math.Max(start.Col, end.Col));
    }

    private static int ColumnLettersToIndex(string letters)
    {
        var index = 0;
        foreach (var ch in letters)
        {
            index = index * 26 + (ch - 'A' + 1);
        }

        return index;
    }

    internal sealed record ExcelSheetData(
        IReadOnlyDictionary<(int Row, int Col), string> Cells,
        int MaxRow,
        int MaxCol)
    {
        public string GetCell(int row, int col) =>
            Cells.TryGetValue((row, col), out var value) ? value.Trim() : string.Empty;

        public IEnumerable<string> RowTexts(int row, int fromCol, int toCol)
        {
            for (var col = fromCol; col <= toCol; col++)
            {
                var value = GetCell(row, col);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private sealed record ExcelCellRange(int TopRow, int BottomRow, int LeftCol, int RightCol);
}
