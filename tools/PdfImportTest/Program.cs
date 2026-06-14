using SmartOepnv.Core.Dienstvorlagen;

var path = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "test_ersatzfahrplan.pdf");
if (!File.Exists(path))
{
    Console.Error.WriteLine($"Datei nicht gefunden: {path}");
    return 1;
}

if (args.Contains("--debug"))
{
    PdfImportTest.ExcelDebug.Dump(path);
    return 0;
}

Console.WriteLine($"PDF: {path}");
Console.WriteLine();

var rows = DutyTemplateFahrplanParser.ParseFileWithHints(path);
Console.WriteLine($"Import-Abschnitte: {rows.Rows.Count}");
Console.WriteLine($"Linie: {rows.Hints.Line}, Route: {rows.Hints.Route}, Bus: {rows.Hints.VehicleNumber}");
Console.WriteLine($"Gültigkeit: {rows.Hints.Validity}");
foreach (var row in rows.Rows.Take(24))
{
    Console.WriteLine($"  [{row.TripNumber}] {row.LineCourse} · {row.Remark} · {row.Preview}");
}

var templateRows = rows.Rows.Select(r => r.ToTemplateRow()).ToList();
var ordered = DutyTemplateCalculator.OrderRows(templateRows);
var stats = DutyTemplateCalculator.Compute(new DutyTemplate { Rows = templateRows });
Console.WriteLine();
Console.WriteLine($"Betriebstag-Sortierung: {string.Join(" > ", ordered.Select(r => r.TripNumber))}");
Console.WriteLine($"Dienstanfang: {DutyTemplateCalculator.GetServiceStartDisplay(templateRows)}");
Console.WriteLine($"Dienstende: {DutyTemplateCalculator.GetServiceEndDisplay(templateRows)}");
Console.WriteLine($"Dienstlänge: {stats.ServiceDurationDisplay}, Lohn: {stats.PayHoursDisplay}, Pausen: {stats.BreaksDisplay}");

if (rows.Rows.Count > 24)
{
    Console.WriteLine($"  ... und {rows.Rows.Count - 24} weitere");
}

return rows.Rows.Count > 0 ? 0 : 2;
