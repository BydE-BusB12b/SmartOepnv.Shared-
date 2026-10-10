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
            Console.WriteLine(
                $"{i,2}. {t.Direction,-4} {t.TripNumber,-6} {t.StartTime}-{t.EndTime}  " +
                $"start=[{t.StartStopHint}] end=[{t.EndStopHint}]  {tps}");
        }

        return 0;
    }
}
