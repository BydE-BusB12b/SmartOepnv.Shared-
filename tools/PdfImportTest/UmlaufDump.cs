using UglyToad.PdfPig;
using SmartOepnv.Core.RoutePackage;

namespace PdfImportTest;

internal static class UmlaufDump
{
    public static int Run(string path)
    {
        var r = UmlaufPdfReader.Read(path);
        Console.WriteLine($"PDF: {path}");
        Console.WriteLine($"Trips={r.Trips.Count} Line={r.LineCourse} Aus={r.AusfahrtTime} Ein={r.EinfahrtTime}");
        Console.WriteLine($"Warn={r.Warning}");
        var i = 0;
        foreach (var t in r.Trips)
        {
            i++;
            if (i > 80)
            {
                break;
            }

            var tps = string.Join("; ", t.TimingPoints.Select(p => $"{p.StopHint}={p.Time}"));
            var mark = t.MarkColorR is byte mr && t.MarkColorG is byte mg && t.MarkColorB is byte mb
                ? $" rgb({mr},{mg},{mb})"
                : string.Empty;
            Console.WriteLine(
                $"{i,2}. {t.Direction,-4} {t.TripNumber,-6} {t.LineCourseHint,-7} {t.StartTime}-{t.EndTime}  " +
                $"start=[{t.StartStopHint}] end=[{t.EndStopHint}]{mark}  {tps}");
        }

        if (argsHasNums(path))
        {
            DumpTripTokens(path);
        }

        if (Environment.GetCommandLineArgs().Any(a =>
                string.Equals(a, "--gap", StringComparison.OrdinalIgnoreCase)))
        {
            DumpMorningGap(path);
        }

        if (Environment.GetCommandLineArgs().Any(a =>
                string.Equals(a, "--rows", StringComparison.OrdinalIgnoreCase)))
        {
            DumpRows(path);
        }

        return 0;
    }

    private static bool argsHasNums(string _) =>
        Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, "--nums", StringComparison.OrdinalIgnoreCase));

    private static void DumpMorningGap(string path)
    {
        using var doc = PdfDocument.Open(path);
        var words = doc.GetPages()
            .SelectMany(p => p.GetWords())
            .Select(w => (
                Text: (w.Text ?? string.Empty).Trim(),
                Y: (w.BoundingBox.Bottom + w.BoundingBox.Top) / 2,
                X: (w.BoundingBox.Left + w.BoundingBox.Right) / 2))
            .Where(w => w.Text.Length > 0)
            .ToList();
        Console.WriteLine("--- rows y 400-545 ---");
        foreach (var grp in words
                     .Where(w => w.Y >= 400 && w.Y <= 545)
                     .GroupBy(w => (int)Math.Round(w.Y / 4.0))
                     .OrderBy(g => g.Key))
        {
            var line = string.Join(" | ", grp.OrderBy(w => w.X).Select(w => $"{w.Text}@{w.X:0}"));
            Console.WriteLine($"  y~{grp.Average(w => w.Y):0}  {line}");
        }

        Console.WriteLine("--- times 7:00-7:59 ---");
        foreach (var w in words
                     .Where(w => System.Text.RegularExpressions.Regex.IsMatch(w.Text, @"^7[:.]\d{2}$"))
                     .OrderBy(w => w.Y)
                     .ThenBy(w => w.X))
        {
            Console.WriteLine($"  {w.Text,-6} x={w.X:0.0} y={w.Y:0.0}");
        }
    }

    private static void DumpRows(string path)
    {
        using var doc = PdfDocument.Open(path);
        var pageNo = 0;
        foreach (var page in doc.GetPages())
        {
            pageNo++;
            Console.WriteLine($"--- page {pageNo} ---");
            var words = page.GetWords()
                .Select(w => (
                    Text: (w.Text ?? string.Empty).Trim(),
                    Y: w.BoundingBox.Bottom,
                    X: (w.BoundingBox.Left + w.BoundingBox.Right) / 2))
                .Where(w => w.Text.Length > 0)
                .ToList();
            foreach (var grp in words
                         .GroupBy(w => (int)Math.Round(w.Y / 2.5))
                         .OrderByDescending(g => g.Key))
            {
                var line = string.Join("  ", grp.OrderBy(w => w.X).Select(w => $"{w.Text}@{w.X:0}"));
                Console.WriteLine($"p{pageNo} y~{grp.Average(w => w.Y):0}  {line}");
            }
        }
    }

    private static void DumpTripTokens(string path)
    {
        using var doc = PdfDocument.Open(path);
        var nums = doc.GetPages()
            .SelectMany(p => p.GetWords())
            .Select(w => (Text: w.Text.Trim(), Y: (w.BoundingBox.Bottom + w.BoundingBox.Top) / 2, X: (w.BoundingBox.Left + w.BoundingBox.Right) / 2))
            .Where(w => w.Text.Length is >= 3 and <= 4 && w.Text.All(char.IsDigit))
            .OrderByDescending(w => w.Y)
            .ThenBy(w => w.X)
            .Take(80);
        Console.WriteLine("--- trip-like tokens (Y desc) ---");
        foreach (var n in nums)
        {
            Console.WriteLine($"  {n.Text}  y={n.Y:0.0} x={n.X:0.0}");
        }
    }
}
