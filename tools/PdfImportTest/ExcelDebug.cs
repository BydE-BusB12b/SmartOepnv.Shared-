using SmartOepnv.Core.Dienstvorlagen;

namespace PdfImportTest;

internal static class ExcelDebug
{
    public static void Dump(string filePath)
    {
        var sheet = DutyTemplateExcelSheetReader.ReadFirstSheet(filePath);
        Console.WriteLine($"Sheet: {sheet.MaxRow} rows, {sheet.MaxCol} cols");
        Console.WriteLine();

        for (var row = 1; row <= sheet.MaxRow; row++)
        {
            var rowText = string.Join(" | ", sheet.RowTexts(row, 1, Math.Max(sheet.MaxCol, 25)));
            if (rowText.Contains("3071", StringComparison.Ordinal) ||
                rowText.Contains("Fahrtnr", StringComparison.OrdinalIgnoreCase) ||
                rowText.Contains("Ersatzhaltestelle", StringComparison.OrdinalIgnoreCase) ||
                rowText.Contains("->", StringComparison.Ordinal) ||
                rowText.Contains('→') ||
                rowText.Contains("Erkrath", StringComparison.OrdinalIgnoreCase) ||
                rowText.Contains("Düsseldorf Hbf", StringComparison.OrdinalIgnoreCase) ||
                rowText.Contains("Vohwinkel", StringComparison.OrdinalIgnoreCase) ||
                rowText.Contains("Mettmann", StringComparison.OrdinalIgnoreCase) ||
                row <= 6)
            {
                Console.WriteLine($"R{row}: {rowText}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== R23 cells ===");
        for (var col = 5; col <= 14; col++)
        {
            Console.WriteLine($"  col {col}: '{sheet.GetCell(23, col)}'");
        }

        Console.WriteLine();
        Console.WriteLine("=== Blocks ===");
        var blocks = DutyTemplateExcelParser.ExtractDirectionBlocks(sheet);
        for (var b = 0; b < blocks.Count; b++)
        {
            var block = blocks[b];
            Console.WriteLine($"Block {b + 1}: {block.RouteLabel}");
            Console.WriteLine($"  Fahrtnr: {string.Join(", ", block.FahrtNumbers)}");
            Console.WriteLine($"  Columns: {string.Join(", ", block.CourseColumns)}");
            Console.WriteLine($"  Rows: {block.Rows.Count}");
            if (block.Rows.Count > 0)
            {
                Console.WriteLine($"  Row0 times: [{string.Join(", ", block.Rows[0].Times.Select(t => string.IsNullOrWhiteSpace(t) ? "-" : t))}]");
            }
            foreach (var tableRow in block.Rows)
            {
                var idx3071 = block.FahrtNumbers.IndexOf("3071");
                var time3071 = idx3071 >= 0 && idx3071 < tableRow.Times.Count ? tableRow.Times[idx3071] : "-";
                if (!string.IsNullOrWhiteSpace(time3071) || tableRow.LongStop.Contains("Erkrath", StringComparison.OrdinalIgnoreCase) ||
                    tableRow.LongStop.Contains("Düsseldorf", StringComparison.OrdinalIgnoreCase) ||
                    tableRow.LongStop.Contains("Vohwinkel", StringComparison.OrdinalIgnoreCase) ||
                    tableRow.LongStop.Contains("Mettmann", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"    {tableRow.Direction,2} {time3071,5} | {tableRow.LongStop}");
                }
            }
        }
    }
}
