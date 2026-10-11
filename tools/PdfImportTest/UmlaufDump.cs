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
            if (i > 8)
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

        return 0;
    }

    private static bool argsHasNums(string _) =>
        Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, "--nums", StringComparison.OrdinalIgnoreCase));

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
