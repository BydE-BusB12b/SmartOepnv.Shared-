using System.Globalization;
using System.Text.RegularExpressions;
using SmartOepnv.Core.Dienstvorlagen;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Tokens;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Liest Solingen-Umlaufkarten-PDFs spaltenbasiert (PdfPig-Wortpositionen).
/// Jede Zeitsäule ≈ eine Fahrt; Fahrtnummern stehen typisch an Wendepunkten.
/// </summary>
public static class UmlaufPdfReader
{
    private static readonly Regex DutyHeaderRegex = new(
        @"(?<line>\d{2,4}\s*/\s*\d{1,3})\s*Betr\.?\s*Hof\s+(?<aus>\d{1,2}[:.]\d{2})\s*[-–]\s*(?<ein>\d{1,2}[:.]\d{2})\s*Betr\.?\s*Hof",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CompactDutyRegex = new(
        @"Betr\.?\s*Hof\s+(?<aus>\d{1,2}[:.]\d{2})\s*[-–]\s*(?<ein>\d{1,2}[:.]\d{2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LineCourseOnlyRegex = new(
        @"\b(?<line>\d{2,4}\s*/\s*\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TimeTokenRegex = new(
        @"^\d{1,2}:\d{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TripTokenRegex = new(
        @"^\d{3,4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const double ColumnClusterTolerance = 12.0;
    private const double RowTolerance = 2.5;

    public sealed class ParsedTrip
    {
        public UmlaufImportPlanner.Direction Direction { get; init; }
        public string TripNumber { get; init; } = string.Empty;
        public string StartTime { get; init; } = string.Empty;
        public string? EndTime { get; init; }
        public string? LineCourseHint { get; init; }
        public string? SectionHint { get; init; }
        /// <summary>Erste bediente Haltestelle aus der PDF (z. B. Graf-Wilh.-Pl.).</summary>
        public string? StartStopHint { get; init; }
        /// <summary>Letzte bediente Haltestelle aus der PDF.</summary>
        public string? EndStopHint { get; init; }
        /// <summary>Alle Planzeiten (Haltestellenname-Hinweis → Uhrzeit) dieser Fahrt.</summary>
        public IReadOnlyList<TimingPoint> TimingPoints { get; init; } = [];
        /// <summary>PDF-Farbmarkierung (Highlight), falls vorhanden – für UI und Linienzuordnung.</summary>
        public byte? MarkColorR { get; init; }
        public byte? MarkColorG { get; init; }
        public byte? MarkColorB { get; init; }
    }

    /// <summary>Große Highlight-Bänder (gelb/grün) zur Linienzuordnung 681/682.</summary>
    private sealed record LineMarkBand(
        byte R,
        byte G,
        byte B,
        double Top,
        double Bottom,
        string LineCourse);

    public sealed record TimingPoint(string StopHint, string Time);

    public sealed class ParseResult
    {
        public string? SourcePath { get; init; }
        public string? LineCourse { get; init; }
        public string? AusfahrtTime { get; init; }
        public string? EinfahrtTime { get; init; }
        public IReadOnlyList<DutyOperatingDay> OperatingDays { get; init; } = [];
        public IReadOnlyList<ParsedTrip> Trips { get; init; } = [];
        public IReadOnlyList<string> RawLines { get; init; } = [];
        public string? Warning { get; init; }
        public bool HasTextLayer => RawLines.Count > 0;
    }

    private sealed record PdfWord(string Text, double MidX, double Y);

    private sealed record TimeCell(string Time, double MidX, double Y, string RowLabel);

    public static ParseResult Read(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF nicht gefunden.", pdfPath);
        }

        var words = ExtractWords(pdfPath);
        var lineTexts = BuildRowTexts(words);

        if (words.Count == 0)
        {
            return new ParseResult
            {
                SourcePath = pdfPath,
                Warning =
                    "In der PDF wurde kein Text gefunden (vermutlich Scan/Bild). " +
                    "Bitte Fahrten im Dialog manuell anlegen."
            };
        }

        var joined = string.Join(" ", lineTexts);
        ParseHeader(joined, out var lineCourse, out var ausfahrt, out var einfahrt);
        var days = ParseOperatingDays(joined);

        var markBands = ExtractLineMarkBands(pdfPath, lineCourse);
        var trips = ExtractTripsFromColumns(words, lineCourse, markBands);
        string? warning = null;
        if (trips.Count == 0)
        {
            // Fallback: alte Zeilenheuristik
            trips = ExtractTripsLegacy(lineTexts, lineCourse);
        }

        if (trips.Count == 0)
        {
            warning =
                "Kopfdaten teilweise gelesen, aber keine Fahrten automatisch erkannt. " +
                "Bitte Fahrten unten ergänzen oder korrigieren.";
        }
        else
        {
            // Wiederholte PDF-Nummern (438, 444, …) → 1438, 2438 …; Einzelnummern (652, 15) bleiben
            trips = AssignSequentialTripNumbers(trips);
        }

        return new ParseResult
        {
            SourcePath = pdfPath,
            LineCourse = lineCourse,
            AusfahrtTime = ausfahrt,
            EinfahrtTime = einfahrt,
            OperatingDays = days,
            Trips = trips,
            RawLines = lineTexts,
            Warning = warning
        };
    }

    /// <summary>
    /// Gleiche Fahrtnummer in mehreren Runden: 1. → 1438, 2. → 2438, …
    /// Nummern, die nur einmal vorkommen (652, 15), unverändert.
    /// </summary>
    private static List<ParsedTrip> AssignSequentialTripNumbers(IReadOnlyList<ParsedTrip> trips)
    {
        if (trips.Count == 0)
        {
            return trips is List<ParsedTrip> list ? list : trips.ToList();
        }

        // Chronologisch (wie in der Karte / UI)
        var ordered = trips
            .OrderBy(t => ParseMinutes(t.StartTime))
            .ThenBy(t => t.TripNumber.StartsWith('?'))
            .ThenBy(t => t.TripNumber, StringComparer.Ordinal)
            .ToList();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in ordered)
        {
            var key = TripNumberSequenceKey(t.TripNumber);
            if (key is null)
            {
                continue;
            }

            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<ParsedTrip>(ordered.Count);
        foreach (var trip in ordered)
        {
            var key = TripNumberSequenceKey(trip.TripNumber);
            if (key is null || !counts.TryGetValue(key, out var total) || total <= 1)
            {
                result.Add(trip);
                continue;
            }

            var round = seen.TryGetValue(key, out var prev) ? prev + 1 : 1;
            seen[key] = round;
            result.Add(CloneTripWithNumber(trip, $"{round}{key}"));
        }

        return result;
    }

    /// <summary>Nur reine Ziffern-Nummern sequenzieren; Platzhalter (?…) unverändert.</summary>
    private static string? TripNumberSequenceKey(string? tripNumber)
    {
        if (string.IsNullOrWhiteSpace(tripNumber))
        {
            return null;
        }

        var t = tripNumber.Trim();
        if (t.StartsWith('?'))
        {
            return null;
        }

        return t.All(char.IsDigit) ? t : null;
    }

    private static ParsedTrip CloneTripWithNumber(ParsedTrip trip, string tripNumber) =>
        new()
        {
            Direction = trip.Direction,
            TripNumber = tripNumber,
            StartTime = trip.StartTime,
            EndTime = trip.EndTime,
            LineCourseHint = trip.LineCourseHint,
            SectionHint = trip.SectionHint,
            StartStopHint = trip.StartStopHint,
            EndStopHint = trip.EndStopHint,
            TimingPoints = trip.TimingPoints,
            MarkColorR = trip.MarkColorR,
            MarkColorG = trip.MarkColorG,
            MarkColorB = trip.MarkColorB
        };

    private static List<PdfWord> ExtractWords(string pdfPath)
    {
        var result = new List<PdfWord>();
        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                var text = (word.Text ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                result.Add(new PdfWord(
                    text,
                    (word.BoundingBox.Left + word.BoundingBox.Right) / 2.0,
                    word.BoundingBox.Bottom));
            }
        }

        return result;
    }

    /// <summary>
    /// PDF-Highlight-Bänder: gelb = 681 (Hästen), grün = 682 (Brockenberg).
    /// Kleine Markierungen an Fahrtnr. ignorieren.
    /// </summary>
    private static List<LineMarkBand> ExtractLineMarkBands(string pdfPath, string? defaultLineCourse)
    {
        var raw = new List<(byte R, byte G, byte B, double Top, double Bottom)>();
        try
        {
            using var document = PdfDocument.Open(pdfPath);
            foreach (var page in document.GetPages())
            {
                foreach (var ann in page.ExperimentalAccess.GetAnnotations())
                {
                    if (ann.Type != AnnotationType.Highlight)
                    {
                        continue;
                    }

                    var rect = ann.Rectangle;
                    // Nur große Flächenbänder, keine einzelnen Fahrtnr.-Kästchen
                    if (rect.Height < 40 || rect.Width < 80)
                    {
                        continue;
                    }

                    if (!TryGetAnnotationRgb(ann, out var r, out var g, out var b))
                    {
                        continue;
                    }

                    raw.Add((r, g, b, rect.Top, rect.Bottom));
                }
            }
        }
        catch
        {
            return [];
        }

        if (raw.Count == 0)
        {
            return [];
        }

        // Nach Farbe clustern (Toleranz für PDF-Floats)
        var clusters = new List<(byte R, byte G, byte B, double Top, double Bottom)>();
        foreach (var band in raw.OrderByDescending(b => (b.Top + b.Bottom) / 2.0))
        {
            var match = -1;
            for (var i = 0; i < clusters.Count; i++)
            {
                if (ColorNear(clusters[i].R, clusters[i].G, clusters[i].B, band.R, band.G, band.B))
                {
                    match = i;
                    break;
                }
            }

            if (match < 0)
            {
                clusters.Add(band);
            }
            else
            {
                var c = clusters[match];
                clusters[match] = (
                    c.R,
                    c.G,
                    c.B,
                    Math.Max(c.Top, band.Top),
                    Math.Min(c.Bottom, band.Bottom));
            }
        }

        if (clusters.Count == 0)
        {
            return [];
        }

        var courseSuffix = ExtractCourseSuffix(defaultLineCourse);
        var line681 = BuildLineCourseWithSuffix("681", courseSuffix);
        var line682 = BuildLineCourseWithSuffix("682", courseSuffix);

        // Farbe entscheidet (nicht Y/Dateiname) – sonst 681/682 vertauscht
        var ordered = clusters.OrderByDescending(c => (c.Top + c.Bottom) / 2.0).ToList();
        var result = new List<LineMarkBand>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var c = ordered[i];
            string line;
            if (IsYellowLineMark(c.R, c.G, c.B))
            {
                line = line681;
            }
            else if (IsGreenLineMark(c.R, c.G, c.B))
            {
                line = line682;
            }
            else
            {
                // Unklare Farbe: oben Hästen=681, unten Brockenberg=682
                line = i == 0 || ordered.Count == 1 ? line681 : line682;
            }

            result.Add(new LineMarkBand(c.R, c.G, c.B, c.Top, c.Bottom, line));
        }

        return result;
    }

    /// <summary>Gelb/ocker Highlight → Linie 681 (Hästen).</summary>
    private static bool IsYellowLineMark(byte r, byte g, byte b) =>
        r >= 200 && g >= 180 && b <= 170 && r + 20 >= g;

    /// <summary>Grün Highlight → Linie 682 (Brockenberg).</summary>
    private static bool IsGreenLineMark(byte r, byte g, byte b) =>
        g >= 180 && g > r + 30 && b <= 170;

    private static bool TryGetAnnotationRgb(Annotation ann, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (!ann.AnnotationDictionary.TryGet(NameToken.C, out var token) ||
            token is not ArrayToken arr ||
            arr.Length < 3)
        {
            return false;
        }

        static double ToUnit(IToken t) =>
            t switch
            {
                NumericToken n => n.Double,
                _ => double.NaN
            };

        var rv = ToUnit(arr[0]);
        var gv = ToUnit(arr[1]);
        var bv = ToUnit(arr[2]);
        if (double.IsNaN(rv) || double.IsNaN(gv) || double.IsNaN(bv))
        {
            return false;
        }

        r = (byte)Math.Clamp((int)Math.Round(rv * 255), 0, 255);
        g = (byte)Math.Clamp((int)Math.Round(gv * 255), 0, 255);
        b = (byte)Math.Clamp((int)Math.Round(bv * 255), 0, 255);
        return true;
    }

    private static bool ColorNear(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2) =>
        Math.Abs(r1 - r2) <= 40 && Math.Abs(g1 - g2) <= 40 && Math.Abs(b1 - b2) <= 40;

    private static ParsedTrip ApplyLineMark(
        ParsedTrip trip,
        IReadOnlyList<TimeCell> times,
        IReadOnlyList<LineMarkBand> bands,
        string? fallbackLine)
    {
        var band = FindBandForTimes(times, bands);
        var line = band?.LineCourse ?? fallbackLine ?? trip.LineCourseHint;
        if (band is null &&
            string.Equals(line, trip.LineCourseHint, StringComparison.Ordinal) &&
            trip.MarkColorR is null)
        {
            return trip;
        }

        return new ParsedTrip
        {
            Direction = trip.Direction,
            TripNumber = trip.TripNumber,
            StartTime = trip.StartTime,
            EndTime = trip.EndTime,
            LineCourseHint = line,
            SectionHint = trip.SectionHint,
            StartStopHint = trip.StartStopHint,
            EndStopHint = trip.EndStopHint,
            TimingPoints = trip.TimingPoints,
            MarkColorR = band?.R ?? trip.MarkColorR,
            MarkColorG = band?.G ?? trip.MarkColorG,
            MarkColorB = band?.B ?? trip.MarkColorB
        };
    }

    private static LineMarkBand? FindBandForTimes(
        IReadOnlyList<TimeCell> times,
        IReadOnlyList<LineMarkBand> bands)
    {
        if (times.Count == 0 || bands.Count == 0)
        {
            return null;
        }

        var midY = times.Average(t => t.Y);
        LineMarkBand? best = null;
        var bestDist = double.MaxValue;
        foreach (var band in bands)
        {
            var pad = 8.0;
            if (midY <= band.Top + pad && midY >= band.Bottom - pad)
            {
                var center = (band.Top + band.Bottom) / 2.0;
                var dist = Math.Abs(midY - center);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = band;
                }
            }
        }

        return best ?? bands.OrderBy(b => Math.Abs(midY - (b.Top + b.Bottom) / 2.0)).First();
    }

    private static List<string> BuildRowTexts(IReadOnlyList<PdfWord> words) =>
        words
            .GroupBy(w => (int)Math.Round(w.Y / RowTolerance))
            .OrderByDescending(g => g.Key)
            .Select(g => string.Join(" ", g.OrderBy(w => w.MidX).Select(w => w.Text)))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

    private static void ParseHeader(
        string joined,
        out string? lineCourse,
        out string? ausfahrt,
        out string? einfahrt)
    {
        lineCourse = null;
        ausfahrt = null;
        einfahrt = null;

        var header = DutyHeaderRegex.Match(joined);
        if (header.Success)
        {
            lineCourse = NormalizeLineCourse(header.Groups["line"].Value);
            ausfahrt = NormalizeTime(header.Groups["aus"].Value);
            einfahrt = NormalizeTime(header.Groups["ein"].Value);
            return;
        }

        var compact = CompactDutyRegex.Match(joined);
        if (compact.Success)
        {
            ausfahrt = NormalizeTime(compact.Groups["aus"].Value);
            einfahrt = NormalizeTime(compact.Groups["ein"].Value);
        }

        // Senkrechter Kopf / abweichende Abstände: „Betr.Hof … 4:11 – 20:23 … Betr.Hof“
        if (string.IsNullOrWhiteSpace(ausfahrt) || string.IsNullOrWhiteSpace(einfahrt))
        {
            var loose = Regex.Match(
                joined,
                @"(?:Betr\.?\s*Hof|Betriebshof).{0,40}?(?<aus>\d{1,2}[:.]\d{2})\s*[-–]\s*(?<ein>\d{1,2}[:.]\d{2})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (loose.Success)
            {
                ausfahrt ??= NormalizeTime(loose.Groups["aus"].Value);
                einfahrt ??= NormalizeTime(loose.Groups["ein"].Value);
            }
        }

        // Letzter Fallback: erstes Zeitpaar nach Linienangabe „681/02 … 4:11 - 20:23“
        if (string.IsNullOrWhiteSpace(ausfahrt) || string.IsNullOrWhiteSpace(einfahrt))
        {
            var pair = Regex.Match(
                joined,
                @"(?<aus>\d{1,2}[:.]\d{2})\s*[-–]\s*(?<ein>\d{1,2}[:.]\d{2})",
                RegexOptions.CultureInvariant);
            if (pair.Success)
            {
                var a = NormalizeTime(pair.Groups["aus"].Value);
                var e = NormalizeTime(pair.Groups["ein"].Value);
                // Plausible Dienstspanne (Ausfahrt früher als Einfahrt, mind. 1 h)
                if (ParseMinutes(a) < ParseMinutes(e) && ParseMinutes(e) - ParseMinutes(a) >= 60)
                {
                    ausfahrt ??= a;
                    einfahrt ??= e;
                }
            }
        }

        var lineMatch = LineCourseOnlyRegex.Match(joined);
        if (lineMatch.Success)
        {
            lineCourse = NormalizeLineCourse(lineMatch.Groups["line"].Value);
        }
    }

    private static List<ParsedTrip> ExtractTripsFromColumns(
        IReadOnlyList<PdfWord> words,
        string? defaultLineCourse,
        IReadOnlyList<LineMarkBand> markBands)
    {
        var rows = words
            .GroupBy(w => (int)Math.Round(w.Y / RowTolerance))
            .OrderByDescending(g => g.Key)
            .Select(g => g.OrderBy(w => w.MidX).ToList())
            .ToList();

        // Doppelzeilen (GWP an/ab + ab/an): Name oft nur in der 1. Zeile – 2. Zeile erbt den Namen
        var timeCells = BuildTimeCellsWithInheritedStopNames(rows);

        if (timeCells.Count < 3)
        {
            return [];
        }

        var columns = ClusterColumns(timeCells.Select(c => c.MidX).Concat(
            words.Where(w => IsTripToken(w.Text)).Select(w => w.MidX)));
        if (columns.Count == 0)
        {
            return [];
        }

        // Rand-/Kopftzeiten (z. B. senkrechtes „Betr.Hof 4:11“) nicht als Fahrtsäule werten
        var minColX = columns[0] - ColumnClusterTolerance;
        timeCells = timeCells
            .Where(c => c.MidX >= minColX)
            .Where(c => !c.RowLabel.Contains("Betr.Hof", StringComparison.OrdinalIgnoreCase) &&
                        !c.RowLabel.Contains("Betriebshof", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Fahrtnummern je Spalte (bevorzugt Zeilen, die fast nur Nummern enthalten)
        var tripNumbersByColumn = new Dictionary<int, List<(string Number, double Y)>>();
        foreach (var row in rows)
        {
            var tripWords = row.Where(w => IsTripToken(w.Text)).ToList();
            var timeWords = row.Where(w => IsTimeToken(w.Text)).ToList();
            if (tripWords.Count == 0)
            {
                continue;
            }

            // Reine Fahrtnummern-Zeile oder gemischt mit wenig Text
            var other = row.Count - tripWords.Count - timeWords.Count;
            if (tripWords.Count < 2 && other > 3)
            {
                // Einzelnummer neben Haltestellenname (z. B. „… an/ab 642“) – trotzdem nutzen
                if (tripWords.Count != 1)
                {
                    continue;
                }
            }

            foreach (var tw in tripWords)
            {
                if (LooksLikeClockDigits(tw.Text))
                {
                    continue;
                }

                var col = NearestColumn(columns, tw.MidX);
                if (col < 0)
                {
                    continue;
                }

                if (!tripNumbersByColumn.TryGetValue(col, out var list))
                {
                    list = [];
                    tripNumbersByColumn[col] = list;
                }

                list.Add((NormalizeTripNumber(tw.Text), tw.Y));
            }
        }

        // Am Hbf splitten (681/682): zwei Fahrten pro Zeitsäule
        var splitY = FindHbfSplitY(rows, timeCells);
        var courseSuffix = ExtractCourseSuffix(defaultLineCourse);
        // Geographie der Karte: oben Hästen=681, unten Brockenberg=682 (nicht Dateiname-primär)
        var upperLine = BuildLineCourseWithSuffix("681", courseSuffix);
        var lowerLine = BuildLineCourseWithSuffix("682", courseSuffix);
        if (markBands.Count >= 2)
        {
            var byY = markBands.OrderByDescending(b => (b.Top + b.Bottom) / 2.0).ToList();
            upperLine = byY[0].LineCourse;
            lowerLine = byY[^1].LineCourse;
        }
        else if (markBands.Count == 1)
        {
            var only = markBands[0];
            if (only.LineCourse.StartsWith("681", StringComparison.Ordinal))
            {
                upperLine = only.LineCourse;
            }
            else
            {
                lowerLine = only.LineCourse;
            }
        }

        // Jede Zeit genau einer Spalte (nächste) – sonst übernehmen Fahrt 2 die Zeiten von Fahrt 3
        var timesByColumn = AssignTimeCellsToColumns(timeCells, columns);

        var trips = new List<ParsedTrip>();
        for (var colIndex = 0; colIndex < columns.Count; colIndex++)
        {
            if (!timesByColumn.TryGetValue(colIndex, out var colTimes) || colTimes.Count == 0)
            {
                continue;
            }

            List<TimeCell> upperTimes;
            List<TimeCell> lowerTimes;
            if (splitY is double sy)
            {
                // Zeiten auf der Nr.-Zeile: Hbf darunter → unten, sonst oben
                upperTimes = colTimes.Where(c => c.Y > sy).ToList();
                lowerTimes = colTimes.Where(c => c.Y < sy).ToList();
                foreach (var onSplit in colTimes.Where(c => Math.Abs(c.Y - sy) < 0.01))
                {
                    if (IsHauptbahnhofSplitAnchor(onSplit.RowLabel))
                    {
                        // Hbf auf der Split-Linie: Ankunft eher oben, Abfahrt eher unten –
                        // bei Gleichstand zur unteren Hälfte (Start nach Fahrtwechsel)
                        if (!lowerTimes.Contains(onSplit))
                        {
                            lowerTimes.Add(onSplit);
                        }
                    }
                }
            }
            else
            {
                // Kein Hbf-Fahrtwechsel (z. B. 683): Spalte nur von oben nach unten = eine Fahrt
                // (Hin und Rück in derselben Spalte, Wendung z. B. an Voh.-Bahnhof)
                upperTimes = colTimes;
                lowerTimes = [];
            }

            if (IsUsableHalf(upperTimes))
            {
                var upperTrip = BuildTripFromHalf(
                    upperTimes,
                    tripNumbersByColumn,
                    colIndex,
                    preferUpperNumbers: true,
                    lineCourseHint: upperLine,
                    sectionFallback: "Hästen/GWP↔Hbf");
                // Obere Hälfte: Start GWP/…, nicht Hbf (Ende ist Hbf)
                upperTrip = ForceUpperHalfStartBeforeHbf(upperTrip, upperTimes);
                trips.Add(ApplyLineMark(upperTrip, upperTimes, markBands, upperLine));
            }

            if (IsUsableHalf(lowerTimes))
            {
                var lowerTrip = BuildTripFromHalf(
                    lowerTimes,
                    tripNumbersByColumn,
                    colIndex,
                    preferUpperNumbers: false,
                    lineCourseHint: lowerLine ?? upperLine,
                    sectionFallback: "Hbf↔Brockenberg");
                // Untere Hälfte Hbf→Brockenberg: Start Hbf (4:45), nicht Brockenberg 5:38 der Folgespalte
                lowerTrip = ForceLowerHalfStartAtHbf(lowerTrip, lowerTimes);
                trips.Add(ApplyLineMark(lowerTrip, lowerTimes, markBands, lowerLine ?? upperLine));
            }
        }

        return trips
            .Where(t => !string.IsNullOrWhiteSpace(t.StartTime) &&
                        !string.IsNullOrWhiteSpace(t.EndTime) &&
                        !string.Equals(t.StartTime, t.EndTime, StringComparison.Ordinal))
            .OrderBy(t => ParseMinutes(t.StartTime))
            .ThenBy(t => t.TripNumber.StartsWith('?'))
            .ThenBy(t => t.TripNumber, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsUsableHalf(List<TimeCell> times)
    {
        if (times.Count == 0)
        {
            return false;
        }

        var distinct = times.Select(t => t.Time).Where(t => t.Length > 0).Distinct().Count();
        if (distinct >= 2)
        {
            return true;
        }

        // Einzelzeit nur mit klarem Wendepunkt (Ab/An Terminus)
        return times.Any(c => IsTerminusAbLabel(c.RowLabel) || IsTerminusAnLabel(c.RowLabel));
    }

    /// <summary>
    /// Zeitzellen bauen. Doppelzeile: Name nur von an/ab → ab/an derselben Hst erben –
    /// nie über Fahrtnummern/Hbf hinweg (sonst GWP-Zeiten am Hbf).
    /// Orphan-Zeiten: Label oft knapp darunter („Hästen ab“ unter 7:14).
    /// </summary>
    private static List<TimeCell> BuildTimeCellsWithInheritedStopNames(List<List<PdfWord>> rows)
    {
        var timeCells = new List<TimeCell>();
        string? lastStopName = null;
        var lastWasAnAb = false;

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var tripOnly = row.Count > 0 &&
                           row.Count(w => IsTripToken(w.Text)) >= 3 &&
                           row.Count(w => IsTimeToken(w.Text)) == 0;
            if (tripOnly)
            {
                // Fahrtwechsel-Nr. (444) zwischen Hbf an/ab und namensloser 4:45-Zeile:
                // Name behalten. Sonst Vererbung trennen (kein GWP über fremde Blöcke).
                if (!lastWasAnAb)
                {
                    lastStopName = null;
                    lastWasAnAb = false;
                }

                continue;
            }

            var rawLabel = NormalizeRoleSlashes(string.Join(
                " ",
                row.Where(w => !IsTimeToken(w.Text) && !IsTripToken(w.Text)).Select(w => w.Text)));
            var times = row.Where(w => IsTimeToken(w.Text)).ToList();
            var stopName = CleanStopHint(rawLabel);

            if (times.Count == 0)
            {
                // PDF-Artefakte („1“, „2“, „:“) dürfen Hbf-Namen nicht überschreiben
                if (!string.IsNullOrWhiteSpace(stopName) && !IsStopNameNoise(stopName))
                {
                    lastStopName = stopName;
                    lastWasAnAb = IsAnAbLabel(rawLabel);
                }

                continue;
            }

            var label = rawLabel;
            if (string.IsNullOrWhiteSpace(stopName))
            {
                // Zeitzeile ohne Name: Label oft 0,5–3 pt darunter (Hästen ab / Hbf)
                var below = FindStopLabelJustBelow(rows, rowIndex, times[0].Y);
                if (!string.IsNullOrWhiteSpace(below))
                {
                    label = below!;
                    stopName = CleanStopHint(label);
                }
                else if (!string.IsNullOrWhiteSpace(lastStopName) &&
                         lastWasAnAb &&
                         (IsAbAnLabel(rawLabel) ||
                          string.IsNullOrWhiteSpace(rawLabel) ||
                          LooksLikeOrphanTimeRoleRow(rawLabel)))
                {
                    // Nur direkte Folgezeile ab/an zur vorherigen an/ab
                    label = $"{lastStopName} ab/an";
                }
            }

            foreach (var w in times)
            {
                timeCells.Add(new TimeCell(
                    NormalizeTime(w.Text),
                    w.MidX,
                    w.Y,
                    label));
            }

            var resolvedName = CleanStopHint(label);
            if (!string.IsNullOrWhiteSpace(resolvedName))
            {
                lastStopName = resolvedName;
                lastWasAnAb = IsAnAbLabel(label);
            }
            else
            {
                lastStopName = null;
                lastWasAnAb = false;
            }
        }

        return timeCells;
    }

    /// <summary>
    /// PDF trennt oft Zeit und Haltname um &lt; RowTolerance (z. B. 7:14 @523.8, „Hästen ab“ @523.3).
    /// </summary>
    private static string? FindStopLabelJustBelow(
        IReadOnlyList<List<PdfWord>> rows,
        int timeRowIndex,
        double timeY)
    {
        for (var i = timeRowIndex + 1; i < rows.Count && i <= timeRowIndex + 3; i++)
        {
            var row = rows[i];
            if (row.Count == 0)
            {
                continue;
            }

            if (row.Any(w => IsTimeToken(w.Text)))
            {
                // Nächste Zeitzeile → zu weit
                break;
            }

            if (row.Count(w => IsTripToken(w.Text)) >= 3)
            {
                continue;
            }

            var y = row.Average(w => w.Y);
            // Rows sind top→bottom (Y absteigend): Label darunter = kleineres Y
            var dy = timeY - y;
            if (dy < -0.5 || dy > RowTolerance * 3)
            {
                if (dy > RowTolerance * 3)
                {
                    break;
                }

                continue;
            }

            var raw = NormalizeRoleSlashes(string.Join(
                " ",
                row.Where(w => !IsTimeToken(w.Text) && !IsTripToken(w.Text)).Select(w => w.Text)));
            var name = CleanStopHint(raw);
            if (string.IsNullOrWhiteSpace(name) || IsStopNameNoise(name))
            {
                continue;
            }

            return raw.Trim().Length > 0 ? raw.Trim() : name;
        }

        return null;
    }

    private static bool LooksLikeOrphanTimeRoleRow(string label)
    {
        var t = label.Trim();
        if (t.Length == 0)
        {
            return true;
        }

        // Reste wie „/“ oder einzelne Tokens ohne Haltestellenwort
        return t.Length <= 3 && !char.IsLetter(t[0]);
    }

    /// <summary>Einzelziffern/Satzzeichen aus senkrechtem PDF-Text – kein Haltestellenname.</summary>
    private static bool IsStopNameNoise(string? stopName)
    {
        if (string.IsNullOrWhiteSpace(stopName))
        {
            return true;
        }

        var t = stopName.Trim();
        if (t.Length <= 2 && t.All(c => !char.IsLetter(c)))
        {
            return true;
        }

        // „1 hauptbahnhof“ / „- 61“: führende Artefakte, Kern trotzdem nutzbar → kein Noise
        return false;
    }

    private static string NormalizeRoleSlashes(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // PDF nutzt manchmal andere Schrägstriche in „an/ab“
        return text
            .Replace('⁄', '/')
            .Replace('∕', '/')
            .Replace('／', '/')
            .Replace('\\', '/');
    }

    private static ParsedTrip BuildTripFromHalf(
        List<TimeCell> halfTimes,
        Dictionary<int, List<(string Number, double Y)>> tripNumbersByColumn,
        int colIndex,
        bool preferUpperNumbers,
        string? lineCourseHint,
        string sectionFallback)
    {
        var colTripNumbers = tripNumbersByColumn.TryGetValue(colIndex, out var tn)
            ? tn
            : [];

        // Hbf mit Fahrtwechsel (rote Nr. z. B. 444): Ankunft und Abfahrt gehören zu zwei Fahrten
        halfTimes = FilterHbfCellsForFahrtwechsel(halfTimes, colTripNumbers, preferUpperNumbers);

        // Regel: bei an+ab nur „ab“; Ausnahme Endhaltestelle → „an“.
        var departureCells = FilterToDepartureTimes(halfTimes);
        var endArrivalCells = FilterToEndArrivalTimes(halfTimes);

        var terminusAb = departureCells.Where(c => IsTerminusAbLabel(c.RowLabel)).ToList();
        var terminusAn = endArrivalCells.Where(c => IsTerminusAnLabel(c.RowLabel)).ToList();
        var chronoAb = departureCells
            .Where(c => !string.IsNullOrWhiteSpace(c.Time))
            .OrderBy(c => ParseMinutes(c.Time))
            .ToList();
        var chronoAn = endArrivalCells
            .Where(c => !string.IsNullOrWhiteSpace(c.Time))
            .OrderBy(c => ParseMinutes(c.Time))
            .ToList();

        string startTime;
        string endTime;

        if (terminusAb.Count > 0 && terminusAn.Count > 0)
        {
            startTime = terminusAb.OrderBy(c => ParseMinutes(c.Time)).First().Time;
            endTime = terminusAn.OrderBy(c => ParseMinutes(c.Time)).Last().Time;
            // Plain-Endhalt nach Terminus-Ankunft (z. B. Mangenberg nach GWP) behalten
            var laterPassenger = LatestPassengerCell(chronoAb, chronoAn);
            if (laterPassenger is not null &&
                ParseMinutes(laterPassenger.Time) > ParseMinutes(endTime))
            {
                endTime = laterPassenger.Time;
            }
        }
        else if (terminusAb.Count > 0)
        {
            startTime = terminusAb.OrderBy(c => ParseMinutes(c.Time)).First().Time;
            // Letzte Fahrgastzeit inkl. Plain-Halte (Mangenberg), nicht nur an/ab (sonst Ende = GWP)
            endTime = LatestPassengerCell(chronoAb, chronoAn)?.Time ?? startTime;
        }
        else if (terminusAn.Count > 0)
        {
            endTime = terminusAn.OrderBy(c => ParseMinutes(c.Time)).Last().Time;
            startTime = chronoAb.Count > 0 ? chronoAb[0].Time : endTime;
            var laterPassenger = LatestPassengerCell(chronoAb, chronoAn);
            if (laterPassenger is not null &&
                ParseMinutes(laterPassenger.Time) > ParseMinutes(endTime))
            {
                endTime = laterPassenger.Time;
            }
        }
        else
        {
            if (chronoAb.Count == 0)
            {
                chronoAb = halfTimes
                    .Where(c => !string.IsNullOrWhiteSpace(c.Time))
                    .OrderBy(c => ParseMinutes(c.Time))
                    .ToList();
            }

            startTime = chronoAb[0].Time;
            var hbfAb = departureCells
                .Where(c => IsHauptbahnhofSplitAnchor(c.RowLabel))
                .OrderBy(c => ParseMinutes(c.Time))
                .ToList();
            var hbfAn = endArrivalCells
                .Where(c => IsHauptbahnhofSplitAnchor(c.RowLabel))
                .OrderBy(c => ParseMinutes(c.Time))
                .ToList();

            if (hbfAb.Count > 0 && ParseMinutes(chronoAb[0].Time) > ParseMinutes(hbfAb[0].Time))
            {
                startTime = hbfAb[0].Time;
                endTime = LatestPassengerCell(chronoAb, chronoAn)?.Time ?? chronoAb[^1].Time;
            }
            else if (hbfAn.Count > 0 || hbfAb.Count > 0)
            {
                // Ende am Hbf: Ankunft bevorzugen, sonst ab/an-Zeit
                endTime = hbfAn.Count > 0 ? hbfAn[^1].Time : hbfAb[^1].Time;
            }
            else
            {
                endTime = LatestPassengerCell(chronoAb, chronoAn)?.Time ?? chronoAb[^1].Time;
            }

            if (string.Equals(startTime, endTime, StringComparison.Ordinal) && chronoAb.Count > 1)
            {
                endTime = LatestPassengerCell(chronoAb, chronoAn)?.Time ?? chronoAb[^1].Time;
            }
        }

        if (ParseMinutes(endTime) < ParseMinutes(startTime))
        {
            (startTime, endTime) = (endTime, startTime);
        }

        var section = GuessSectionLabel(halfTimes);
        if (string.IsNullOrWhiteSpace(section))
        {
            section = sectionFallback;
        }

        // Haltestelle zur gewählten Start-/Endzeit (leere Zeilen darüber = nicht bedient)
        var startStopHint = CleanStopHint(
            departureCells.FirstOrDefault(c =>
                    string.Equals(c.Time, startTime, StringComparison.Ordinal))
                ?.RowLabel
            ?? chronoAb.FirstOrDefault()?.RowLabel);

        // Doppel-GWP (an/ab + ab/an): Abfahrt = zweite Zeile / ab/an – nicht am Hbf mit Fahrtwechsel
        startTime = PreferDepartureTimeForStop(
            halfTimes, startStopHint, startTime, colTripNumbers, preferUpperNumbers);

        // Endhalt zur gewählten Endzeit – zuerst Abfahrt/Plain (Mangenberg), dann Ankunftszeilen
        var endStopHint = CleanStopHint(
            chronoAb.LastOrDefault(c =>
                    string.Equals(c.Time, endTime, StringComparison.Ordinal))
                ?.RowLabel
            ?? endArrivalCells.LastOrDefault(c =>
                    string.Equals(c.Time, endTime, StringComparison.Ordinal))
                ?.RowLabel
            ?? LatestPassengerCell(chronoAb, chronoAn)?.RowLabel
            ?? chronoAn.LastOrDefault()?.RowLabel
            ?? chronoAb.LastOrDefault()?.RowLabel);

        // Endhalt: bei Doppelzeile Ankunft = an/ab (erste); Hbf+Fahrtwechsel: Zeit oberhalb der Nr.
        endTime = PreferArrivalTimeForStop(
            halfTimes, endStopHint, endTime, colTripNumbers, preferUpperNumbers);

        // Fahrtnr. nahe am Starthalt (unten am Brockenberg = 648, oben am Hbf = 444/438)
        var startAnchorY = ResolveStartAnchorY(halfTimes, startStopHint, startTime);
        var tripNo = ResolveTripNumberForHalf(
            tripNumbersByColumn,
            colIndex,
            halfTimes,
            preferUpperNumbers,
            startAnchorY) ?? $"?{colIndex + 1}{(preferUpperNumbers ? "a" : "b")}";

        // Nach Hbf-Split: obere Spaltenhälfte = Hin (→ Hbf), untere = Rück (Hbf →).
        var direction = preferUpperNumbers
            ? UmlaufImportPlanner.Direction.Hin
            : UmlaufImportPlanner.Direction.Rueck;

        var timingPoints = ExtractTimingPoints(
            halfTimes, endTime, endStopHint, colTripNumbers, preferUpperNumbers);

        return new ParsedTrip
        {
            Direction = direction,
            TripNumber = tripNo,
            StartTime = startTime,
            EndTime = endTime,
            LineCourseHint = lineCourseHint,
            SectionHint = section,
            StartStopHint = startStopHint,
            EndStopHint = endStopHint,
            TimingPoints = timingPoints
        };
    }

    /// <summary>
    /// Obere Hälfte nur bei Fahrt zum Hbf: Start = früheste Nicht-Hbf-Zeit (GWP 4:20), Ende Hbf.
    /// Nicht anfassen bei Hbf→Hästen (Hbf-Abfahrt früher als Folgehälte).
    /// </summary>
    private static ParsedTrip ForceUpperHalfStartBeforeHbf(ParsedTrip trip, IReadOnlyList<TimeCell> upperTimes)
    {
        var hbfEnd = upperTimes
            .Where(c => !string.IsNullOrWhiteSpace(c.Time) && IsHauptbahnhofSplitAnchor(c.RowLabel))
            .OrderByDescending(c => c.Y)
            .FirstOrDefault();
        if (hbfEnd is null)
        {
            return trip;
        }

        var hbfTime = NormalizeTime(hbfEnd.Time);
        var first = upperTimes
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.Time) &&
                !IsHauptbahnhofSplitAnchor(c.RowLabel) &&
                !string.IsNullOrWhiteSpace(CleanStopHint(c.RowLabel)) &&
                !IsStopNameNoise(CleanStopHint(c.RowLabel)))
            .OrderBy(c => ParseMinutes(c.Time))
            .FirstOrDefault();
        if (first is null)
        {
            return trip;
        }

        // Nur wenn Nicht-Hbf vor Hbf-Zeit liegt (GWP→Hbf). Hbf 6:20 → Merscheid 6:26: lassen.
        if (ParseMinutes(first.Time) >= ParseMinutes(hbfTime))
        {
            return trip;
        }

        var startHint = CleanStopHint(first.RowLabel);
        var startTime = NormalizeTime(first.Time);
        if (string.IsNullOrWhiteSpace(startHint))
        {
            return trip;
        }

        if (!IsHauptbahnhofHint(trip.StartStopHint) &&
            string.Equals(trip.StartTime, startTime, StringComparison.Ordinal) &&
            string.Equals(trip.EndTime, hbfTime, StringComparison.Ordinal))
        {
            return trip;
        }

        return new ParsedTrip
        {
            Direction = trip.Direction,
            TripNumber = trip.TripNumber,
            StartTime = startTime,
            EndTime = hbfTime,
            LineCourseHint = trip.LineCourseHint,
            SectionHint = trip.SectionHint,
            StartStopHint = startHint,
            EndStopHint = CleanStopHint(hbfEnd.RowLabel) ?? trip.EndStopHint,
            TimingPoints = trip.TimingPoints
        };
    }

    /// <summary>
    /// Untere Hälfte nach Fahrtwechsel: bei Fahrt Hbf→Brockenberg Start = Hbf (z. B. 4:45).
    /// Nicht anfassen bei Fahrt zum Hbf (Brockenberg/GWP … → Hbf, z. B. erste Fahrt 4:20–4:46) —
    /// sonst Start=Ende=Hbf und die Fahrt fällt weg.
    /// </summary>
    private static ParsedTrip ForceLowerHalfStartAtHbf(ParsedTrip trip, IReadOnlyList<TimeCell> lowerTimes)
    {
        var hasTerminusAb = lowerTimes.Any(c =>
            !string.IsNullOrWhiteSpace(c.Time) &&
            IsTerminusAbLabel(c.RowLabel));
        if (hasTerminusAb)
        {
            return trip;
        }

        var hbf = lowerTimes
            .Where(c => !string.IsNullOrWhiteSpace(c.Time) && IsHauptbahnhofSplitAnchor(c.RowLabel))
            .OrderBy(c => ParseMinutes(c.Time))
            .ThenBy(c => c.Y)
            .FirstOrDefault();
        if (hbf is null)
        {
            return trip;
        }

        var hbfTime = NormalizeTime(hbf.Time);

        // Früheste Nicht-Hbf-Zeit vor Hbf → Hin zum Hbf (GWP/Brockenberg→Hbf), Start nicht überschreiben
        var firstNonHbf = lowerTimes
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.Time) &&
                !IsHauptbahnhofSplitAnchor(c.RowLabel) &&
                !string.IsNullOrWhiteSpace(CleanStopHint(c.RowLabel)) &&
                !IsStopNameNoise(CleanStopHint(c.RowLabel)))
            .OrderBy(c => ParseMinutes(c.Time))
            .FirstOrDefault();
        if (firstNonHbf is not null &&
            ParseMinutes(firstNonHbf.Time) < ParseMinutes(hbfTime))
        {
            return trip;
        }

        var hbfHint = CleanStopHint(hbf.RowLabel) ?? trip.StartStopHint;
        if (string.Equals(trip.StartTime, hbfTime, StringComparison.Ordinal) &&
            IsHauptbahnhofHint(trip.StartStopHint))
        {
            return trip;
        }

        return new ParsedTrip
        {
            Direction = trip.Direction,
            TripNumber = trip.TripNumber,
            StartTime = hbfTime,
            EndTime = trip.EndTime,
            LineCourseHint = trip.LineCourseHint,
            SectionHint = trip.SectionHint,
            StartStopHint = hbfHint,
            EndStopHint = trip.EndStopHint,
            TimingPoints = trip.TimingPoints
        };
    }

    /// <summary>
    /// Hbf-Doppelzeile mit Fahrtwechsel (rote Nr. z. B. 444 zwischen den Zeiten):
    /// nur die Zeit dieser Fahrt behalten. Ohne Nr. dazwischen → keine Trennung
    /// (GWP-artige an/ab+ab/an bleibt unangetastet – gilt nur am Hbf mit Fahrtnr.).
    /// </summary>
    private static List<TimeCell> FilterHbfCellsForFahrtwechsel(
        List<TimeCell> halfTimes,
        IReadOnlyList<(string Number, double Y)> tripNumbers,
        bool preferUpperHalf)
    {
        var hbfCells = halfTimes
            .Where(c => !string.IsNullOrWhiteSpace(c.Time) && IsHauptbahnhofSplitAnchor(c.RowLabel))
            .OrderByDescending(c => c.Y)
            .ToList();
        if (hbfCells.Count < 2 || tripNumbers.Count == 0)
        {
            return halfTimes;
        }

        var topY = hbfCells[0].Y;
        var botY = hbfCells[^1].Y;

        // Nur echte Fahrtnr. streng zwischen den Hbf-Zeilen (nicht GWP, nicht außerhalb)
        var changeMarkers = tripNumbers
            .Where(t => IsYStrictlyBetween(t.Y, topY, botY))
            .Select(t => t.Y)
            .ToList();
        if (changeMarkers.Count == 0)
        {
            return halfTimes;
        }

        var splitY = changeMarkers.Average();
        // Clamp: Split muss zwischen den Hbf-Zeiten liegen, sonst verschwinden beide
        var lo = botY + 0.25;
        var hi = topY - 0.25;
        if (hi <= lo)
        {
            return halfTimes;
        }

        splitY = Math.Clamp(splitY, lo, hi);

        var filtered = halfTimes
            .Where(c =>
            {
                if (!IsHauptbahnhofSplitAnchor(c.RowLabel) || string.IsNullOrWhiteSpace(c.Time))
                {
                    return true;
                }

                // PdfPig: größeres Y = weiter oben auf der Seite
                return preferUpperHalf ? c.Y >= splitY : c.Y <= splitY;
            })
            .ToList();

        // Sicherheit: mindestens eine Hbf-Zeit behalten
        var keptHbf = filtered.Count(c =>
            !string.IsNullOrWhiteSpace(c.Time) && IsHauptbahnhofSplitAnchor(c.RowLabel));
        return keptHbf > 0 ? filtered : halfTimes;
    }

    /// <summary>
    /// Planzeiten in Leserichtung (oben → unten).
    /// Doppelzeile eng beieinander (GWP an/ab+ab/an) = ein Halt; zweiter Besuch derselben Hst
    /// weiter unten (Rundkurs 683) = eigener TimingPoint.
    /// </summary>
    private static IReadOnlyList<TimingPoint> ExtractTimingPoints(
        IReadOnlyList<TimeCell> halfTimes,
        string endTime,
        string? endStopHint,
        IReadOnlyList<(string Number, double Y)> tripNumbers,
        bool preferUpperHalf)
    {
        // PdfPig: größeres Y = weiter oben → absteigend = Leserichtung
        var cells = halfTimes
            .Where(c => !string.IsNullOrWhiteSpace(c.Time))
            .Select(c => (Hint: CleanStopHint(c.RowLabel), Cell: c))
            .Where(x => !string.IsNullOrWhiteSpace(x.Hint))
            .OrderByDescending(x => x.Cell.Y)
            .ToList();

        const double doubleLineGapPt = 28.0;
        var points = new List<TimingPoint>();
        var i = 0;
        while (i < cells.Count)
        {
            var hint = cells[i].Hint!;
            var cluster = new List<TimeCell> { cells[i].Cell };
            var j = i + 1;
            while (j < cells.Count &&
                   string.Equals(cells[j].Hint, hint, StringComparison.OrdinalIgnoreCase) &&
                   cells[j - 1].Cell.Y - cells[j].Cell.Y < doubleLineGapPt)
            {
                cluster.Add(cells[j].Cell);
                j++;
            }

            TimeCell chosen;
            if (cluster.Count == 1)
            {
                chosen = cluster[0];
            }
            else if (IsHauptbahnhofHint(hint) &&
                     HasTripNumberBetween(cluster.Max(c => c.Y), cluster.Min(c => c.Y), tripNumbers))
            {
                chosen = PreferHbfCellForHalf(
                    cluster.OrderByDescending(c => c.Y).First(),
                    cluster.OrderBy(c => c.Y).First(),
                    preferUpperHalf);
            }
            else if (IsHauptbahnhofHint(hint))
            {
                chosen = cluster.OrderBy(c => ParseMinutes(c.Time)).First();
            }
            else
            {
                // Doppelzeile: Abfahrt = spätere Uhrzeit
                chosen = cluster.OrderByDescending(c => ParseMinutes(c.Time)).First();
            }

            points.Add(new TimingPoint(hint, NormalizeTime(chosen.Time)));
            i = j;
        }

        if (!string.IsNullOrWhiteSpace(endTime) &&
            !string.IsNullOrWhiteSpace(endStopHint) &&
            RouteScheduleTimeCalculator.TryParseTime(endTime, out _))
        {
            var endNorm = NormalizeTime(endTime);
            if (points.Count == 0)
            {
                points.Add(new TimingPoint(endStopHint, endNorm));
            }
            else if (string.Equals(points[^1].StopHint, endStopHint, StringComparison.OrdinalIgnoreCase))
            {
                points[^1] = new TimingPoint(endStopHint, endNorm);
            }
            else if (IsHauptbahnhofHint(endStopHint) ||
                     !points.Any(p =>
                         string.Equals(p.StopHint, endStopHint, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(p.Time, endNorm, StringComparison.Ordinal)))
            {
                points.Add(new TimingPoint(endStopHint, endNorm));
            }
        }

        return points;
    }

    /// <summary>
    /// Doppelzeile derselben Hst (nicht Hbf-Fahrtwechsel): immer die spätere Uhrzeit.
    /// GWP Hin: :13/:15 → :15; Rück: :48/:50 → :50.
    /// </summary>
    private static TimeCell PreferDepartureCell(TimeCell a, TimeCell b) =>
        ParseMinutes(b.Time) >= ParseMinutes(a.Time) ? b : a;

    private static TimeCell PreferHbfCellForHalf(TimeCell a, TimeCell b, bool preferUpperHalf) =>
        preferUpperHalf
            ? (a.Y >= b.Y ? a : b)
            : (a.Y <= b.Y ? a : b);

    private static string PreferDepartureTimeForStop(
        IReadOnlyList<TimeCell> halfTimes,
        string? stopHint,
        string currentTime,
        IReadOnlyList<(string Number, double Y)> tripNumbers,
        bool preferUpperHalf)
    {
        if (string.IsNullOrWhiteSpace(stopHint))
        {
            return currentTime;
        }

        var cells = halfTimes
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.Time) &&
                string.Equals(CleanStopHint(c.RowLabel), stopHint, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (cells.Count < 2)
        {
            return currentTime;
        }

        // Hbf + Fahrtwechsel: Startzeit = untere Hbf-Zeile (unter der roten Fahrtnummer)
        if (IsHauptbahnhofHint(stopHint) &&
            HasTripNumberBetween(cells.Max(c => c.Y), cells.Min(c => c.Y), tripNumbers))
        {
            var cell = preferUpperHalf
                ? cells.OrderByDescending(c => c.Y).First()
                : cells.OrderBy(c => c.Y).First();
            return NormalizeTime(cell.Time);
        }

        TimeCell? preferred = null;
        foreach (var cell in cells)
        {
            preferred = preferred is null ? cell : PreferDepartureCell(preferred, cell);
        }

        return preferred is null ? currentTime : NormalizeTime(preferred.Time);
    }

    private static string PreferArrivalTimeForStop(
        IReadOnlyList<TimeCell> halfTimes,
        string? stopHint,
        string currentTime,
        IReadOnlyList<(string Number, double Y)> tripNumbers,
        bool preferUpperHalf)
    {
        if (string.IsNullOrWhiteSpace(stopHint))
        {
            return currentTime;
        }

        var cells = halfTimes
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.Time) &&
                string.Equals(CleanStopHint(c.RowLabel), stopHint, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (cells.Count < 2)
        {
            return currentTime;
        }

        // Hbf + Fahrtwechsel: Endzeit = obere Hbf-Zeile (über der roten Fahrtnummer)
        if (IsHauptbahnhofHint(stopHint) &&
            HasTripNumberBetween(cells.Max(c => c.Y), cells.Min(c => c.Y), tripNumbers))
        {
            var cell = preferUpperHalf
                ? cells.OrderByDescending(c => c.Y).First()
                : cells.OrderBy(c => c.Y).First();
            return NormalizeTime(cell.Time);
        }

        // Endhalt (z. B. GWP): an/ab bzw. frühere Zeit
        var anAb = cells.Where(c => IsAnAbLabel(c.RowLabel)).ToList();
        if (anAb.Count > 0)
        {
            return NormalizeTime(anAb.OrderBy(c => ParseMinutes(c.Time)).First().Time);
        }

        return NormalizeTime(cells.OrderBy(c => ParseMinutes(c.Time)).First().Time);
    }

    private static bool HasTripNumberBetween(
        double y1,
        double y2,
        IReadOnlyList<(string Number, double Y)> tripNumbers) =>
        tripNumbers.Any(t => IsYStrictlyBetween(t.Y, Math.Max(y1, y2), Math.Min(y1, y2)));

    /// <summary>Y liegt streng zwischen oberer und unterer Hbf-Zeile (rote Fahrtnr.).</summary>
    private static bool IsYStrictlyBetween(double y, double topY, double botY)
    {
        var lo = Math.Min(topY, botY);
        var hi = Math.Max(topY, botY);
        return hi - lo > 0.5 && y > lo && y < hi;
    }

    private static bool IsHauptbahnhofLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        return label.Contains("Hauptbahnhof", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("Hbf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHauptbahnhofHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return false;
        }

        var h = hint.Trim().ToLowerInvariant();
        return h.Contains("hauptbahnhof", StringComparison.Ordinal) ||
               h.Contains("hbf", StringComparison.Ordinal) ||
               (h.Contains("solingen", StringComparison.Ordinal) &&
                h.Contains("bahnhof", StringComparison.Ordinal));
    }

    private static readonly Regex AbAnFlexibleRegex = new(
        @"\bab\s*/\s*an\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AnAbFlexibleRegex = new(
        @"\ban\s*/\s*ab\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsAbAnLabel(string? label) =>
        !string.IsNullOrWhiteSpace(label) && AbAnFlexibleRegex.IsMatch(label);

    private static bool IsAnAbLabel(string? label) =>
        !string.IsNullOrWhiteSpace(label) && AnAbFlexibleRegex.IsMatch(label);

    /// <summary>Haltestellenname aus PDF-Zeilenlabel (ohne an/ab).</summary>
    public static string? CleanStopHint(string? rowLabel)
    {
        if (string.IsNullOrWhiteSpace(rowLabel))
        {
            return null;
        }

        var key = NormalizeStopKey(rowLabel);
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    /// <summary>
    /// Betriebszeiten (Start/Zwischen): nur Abfahrt.
    /// Wenn für dieselbe Hst getrennte an- und ab-Zeilen existieren → nur ab.
    /// Eine kombinierte an/ab- bzw. ab/an-Zeile (eine Zeit) zählt als Abfahrt.
    /// </summary>
    private static List<TimeCell> FilterToDepartureTimes(IReadOnlyList<TimeCell> cells)
    {
        var result = new List<TimeCell>();
        foreach (var group in cells.GroupBy(c => NormalizeStopKey(c.RowLabel)))
        {
            var list = group.ToList();
            var pureAb = list.Where(c => IsPureDepartureLabel(c.RowLabel)).ToList();
            var pureAn = list.Where(c => IsPureArrivalLabel(c.RowLabel)).ToList();
            var combined = list.Where(c => IsCombinedAnAbLabel(c.RowLabel)).ToList();
            var plain = list.Where(c =>
                    !IsPureDepartureLabel(c.RowLabel) &&
                    !IsPureArrivalLabel(c.RowLabel) &&
                    !IsCombinedAnAbLabel(c.RowLabel))
                .ToList();

            if (pureAb.Count > 0 && pureAn.Count > 0)
            {
                // an und ab vorhanden → nur ab
                result.AddRange(pureAb);
            }
            else if (pureAb.Count > 0)
            {
                result.AddRange(pureAb);
            }
            else if (combined.Count > 0)
            {
                // Doppelzeile (GWP): spätere Uhrzeit. Am Hbf zuvor per Fahrtnr. getrennt.
                result.Add(combined.OrderByDescending(c => ParseMinutes(c.Time)).First());
            }
            else if (plain.Count > 0)
            {
                result.Add(plain.OrderByDescending(c => ParseMinutes(c.Time)).First());
            }
        }

        return result;
    }

    /// <summary>
    /// Endhaltestelle: Ankunft. Wenn an und ab vorhanden → nur an.
    /// Kombinierte Zeile ohne getrenntes Paar: an/ab bevorzugen, sonst ab/an.
    /// Plain-Halte (ohne an/ab) zählen mit – sonst fehlt z. B. Mangenberg als Endhalt.
    /// Am Hbf keine GWP-Spätzeit – frühere/obere Zeit (Ankunft vor Fahrtwechsel).
    /// </summary>
    private static List<TimeCell> FilterToEndArrivalTimes(IReadOnlyList<TimeCell> cells)
    {
        var result = new List<TimeCell>();
        foreach (var group in cells.GroupBy(c => NormalizeStopKey(c.RowLabel)))
        {
            var list = group.ToList();
            var pureAb = list.Where(c => IsPureDepartureLabel(c.RowLabel)).ToList();
            var pureAn = list.Where(c => IsPureArrivalLabel(c.RowLabel)).ToList();
            var combined = list.Where(c => IsCombinedAnAbLabel(c.RowLabel)).ToList();
            var plain = list.Where(c =>
                    !IsPureDepartureLabel(c.RowLabel) &&
                    !IsPureArrivalLabel(c.RowLabel) &&
                    !IsCombinedAnAbLabel(c.RowLabel) &&
                    !IsEinfahrtOrDepotLabel(c.RowLabel))
                .ToList();

            if (pureAb.Count > 0 && pureAn.Count > 0)
            {
                result.AddRange(pureAn);
            }
            else if (pureAn.Count > 0)
            {
                result.AddRange(pureAn);
            }
            else if (combined.Count > 0)
            {
                var anFirst = combined.Where(c => IsAnAbLabel(c.RowLabel)).ToList();
                result.AddRange(anFirst.Count > 0 ? anFirst : combined);
            }
            else if (plain.Count > 0)
            {
                result.Add(plain.OrderByDescending(c => ParseMinutes(c.Time)).First());
            }
        }

        return result;
    }

    /// <summary>Späteste Fahrgast-Zeit (Abfahrt/Plain/Ankunft), ohne Einfahrt/Betr.Hof.</summary>
    private static TimeCell? LatestPassengerCell(
        IReadOnlyList<TimeCell> chronoAb,
        IReadOnlyList<TimeCell> chronoAn)
    {
        TimeCell? best = null;
        foreach (var c in chronoAb.Concat(chronoAn))
        {
            if (string.IsNullOrWhiteSpace(c.Time) || IsEinfahrtOrDepotLabel(c.RowLabel))
            {
                continue;
            }

            if (best is null || ParseMinutes(c.Time) > ParseMinutes(best.Time))
            {
                best = c;
            }
        }

        return best;
    }

    private static bool IsEinfahrtOrDepotLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var l = label.Trim();
        return l.Contains("Einfahrt", StringComparison.OrdinalIgnoreCase) ||
               l.Contains("Ausfahrt", StringComparison.OrdinalIgnoreCase) ||
               l.Contains("Betr.Hof", StringComparison.OrdinalIgnoreCase) ||
               l.Contains("Betriebshof", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStopKey(string? label)
    {
        var l = (label ?? string.Empty).Trim().ToLowerInvariant();
        l = AnAbFlexibleRegex.Replace(l, " ");
        l = AbAnFlexibleRegex.Replace(l, " ");
        l = Regex.Replace(l, @"\b(an|ab)\b", " ");
        // PDF: „/ Graf - Wilh. - Pl. ab/an“ und „Graf - Wilh. - Pl. an/ab“ → gleicher Halt
        // (sonst TimingPoints doppelt: :13 und :15, Anzeige nimmt oft die Ankunft)
        l = Regex.Replace(l, @"^[\s/|.\-–—]+", "");
        l = Regex.Replace(l, @"[\s/|.\-–—]+$", "");
        l = Regex.Replace(l, @"\s+", " ").Trim();
        // PdfPig-Artefakte: „2 hauptbahnhof“, „e graf…“, „r . rathaus“, „f heresbachstr“, „4 wald…“
        for (var n = 0; n < 3; n++)
        {
            var next = Regex.Replace(l, @"^\d+\s+", "");
            next = Regex.Replace(next, @"^[a-z]\s*\.\s*", "");
            next = Regex.Replace(next, @"^[a-z]\s+", "");
            next = Regex.Replace(next, @"\s+", " ").Trim();
            if (next.Equals(l, StringComparison.Ordinal))
            {
                break;
            }

            l = next;
        }

        // Umlaufkarte: Brockenberg = Höhscheid-Brockenberg
        if (l.Equals("brockenberg", StringComparison.Ordinal))
        {
            return "höhscheid-brockenberg";
        }

        return l;
    }

    private static bool IsCombinedAnAbLabel(string? label) =>
        IsAnAbLabel(label) || IsAbAnLabel(label);

    private static bool IsPureDepartureLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || IsCombinedAnAbLabel(label))
        {
            return false;
        }

        var l = label.Trim().ToLowerInvariant();
        return Regex.IsMatch(l, @"\bab\b") && !Regex.IsMatch(l, @"\ban\b");
    }

    private static bool IsPureArrivalLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || IsCombinedAnAbLabel(label))
        {
            return false;
        }

        var l = label.Trim().ToLowerInvariant();
        return Regex.IsMatch(l, @"\ban\b") && !Regex.IsMatch(l, @"\bab\b");
    }

    /// <summary>
    /// Y der roten Fahrtnummern streng zwischen Hbf-Ankunft und Hbf-Abfahrt (444).
    /// Nicht die GWP-Nummernzeile (642) – die liegt oberhalb des Hbf.
    /// </summary>
    private static double? FindHbfSplitY(
        List<List<PdfWord>> rows,
        List<TimeCell> timeCells)
    {
        var hbfYs = timeCells
            .Where(c => !string.IsNullOrWhiteSpace(c.Time) && IsHauptbahnhofSplitAnchor(c.RowLabel))
            .Select(c => c.Y)
            .ToList();
        if (hbfYs.Count < 2)
        {
            return null;
        }

        // Ankunft-/Abfahrt-Paar = größte Lücke zwischen benachbarten Hbf-Ys
        // (eng, typisch wenige Punkte – nicht die Spanne über die ganze Seite)
        var sorted = hbfYs.Distinct().OrderByDescending(y => y).ToList();
        var bestGap = 0.0;
        var pairTop = sorted[0];
        var pairBot = sorted[^1];
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var gap = sorted[i] - sorted[i + 1];
            // Zu große Lücken = verschiedene Seitenbereiche, ignorieren
            if (gap > bestGap && gap < 80)
            {
                bestGap = gap;
                pairTop = sorted[i];
                pairBot = sorted[i + 1];
            }
        }

        if (bestGap < 0.3)
        {
            pairTop = sorted[0];
            pairBot = sorted.Count > 1 ? sorted[1] : sorted[0];
            bestGap = pairTop - pairBot;
        }

        var pairMid = (pairTop + pairBot) / 2.0;

        (double Y, int Count)? bestTripRow = null;
        foreach (var row in rows)
        {
            var tripWords = row.Where(w => IsTripToken(w.Text)).ToList();
            if (tripWords.Count < 3)
            {
                continue;
            }

            var label = string.Join(
                " ",
                row.Where(w => !IsTimeToken(w.Text) && !IsTripToken(w.Text)).Select(w => w.Text));
            if (label.Length > 12 && tripWords.Count < 5)
            {
                continue;
            }

            if (IsGrafWilhelmLabel(label) || IsHauptbahnhofSplitAnchor(label))
            {
                continue;
            }

            var y = tripWords.Average(w => w.Y);
            // Streng zwischen dem Hbf-Paar – kein „nah an Mitte“ bis zum GWP (642)
            var between = pairTop > pairBot
                ? y < pairTop && y > pairBot
                : Math.Abs(y - pairMid) <= RowTolerance * 2;
            if (!between || IsNearGrafWilhelmTimes(y, timeCells))
            {
                continue;
            }

            if (bestTripRow is null || tripWords.Count > bestTripRow.Value.Count)
            {
                bestTripRow = (y, tripWords.Count);
            }
        }

        return bestTripRow?.Y ?? (bestGap >= 0.3 ? pairMid : null);
    }

    private static bool IsGrafWilhelmLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var l = label.Trim().ToLowerInvariant();
        return l.Contains("graf", StringComparison.Ordinal) &&
               (l.Contains("wilh", StringComparison.Ordinal) ||
                l.Contains("wilhelm", StringComparison.Ordinal) ||
                l.Contains("gwp", StringComparison.Ordinal));
    }

    private static bool IsNearGrafWilhelmTimes(double y, IReadOnlyList<TimeCell> timeCells) =>
        timeCells.Any(c =>
            !string.IsNullOrWhiteSpace(c.Time) &&
            IsGrafWilhelmLabel(c.RowLabel) &&
            Math.Abs(c.Y - y) < 18.0);

    /// <summary>Hbf-Zeile als Split-Anker; kein Zieltext wie „Hbf &gt; GWP“.</summary>
    private static bool IsHauptbahnhofSplitAnchor(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        if (label.Contains('>') ||
            label.Contains('→') ||
            label.Contains("->", StringComparison.Ordinal))
        {
            return false;
        }

        return label.Contains("Hauptbahnhof", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(label, @"\bHbf\b", RegexOptions.IgnoreCase);
    }

    private static string? ResolveTripNumberForHalf(
        Dictionary<int, List<(string Number, double Y)>> tripNumbersByColumn,
        int colIndex,
        List<TimeCell> halfTimes,
        bool preferUpperNumbers,
        double? startAnchorY = null)
    {
        if (!tripNumbersByColumn.TryGetValue(colIndex, out var list) || list.Count == 0)
        {
            return null;
        }

        var halfMinY = halfTimes.Min(c => c.Y) - 8;
        var halfMaxY = halfTimes.Max(c => c.Y) + 8;
        var inHalf = list.Where(x => x.Y >= halfMinY && x.Y <= halfMaxY).ToList();
        var pool = inHalf.Count > 0 ? inHalf : list;

        // Nummer am Starthalt: Brockenberg unten → 648; Hbf/Hästen oben → 444/438/646
        var anchorY = startAnchorY ?? (preferUpperNumbers ? halfMaxY : halfMinY);
        return pool
            .OrderBy(x => Math.Abs(x.Y - anchorY))
            .ThenBy(x => preferUpperNumbers ? -x.Y : x.Y)
            .First()
            .Number;
    }

    private static double? ResolveStartAnchorY(
        IReadOnlyList<TimeCell> halfTimes,
        string? startStopHint,
        string startTime)
    {
        if (halfTimes.Count == 0)
        {
            return null;
        }

        var byHint = halfTimes
            .Where(c => !string.IsNullOrWhiteSpace(startStopHint) &&
                        FindStopNameMatch(c.RowLabel, startStopHint))
            .ToList();
        if (byHint.Count > 0)
        {
            return byHint.Average(c => c.Y);
        }

        var byTime = halfTimes
            .Where(c => string.Equals(c.Time, startTime, StringComparison.Ordinal))
            .ToList();
        if (byTime.Count > 0)
        {
            return byTime.Average(c => c.Y);
        }

        return null;
    }

    private static bool FindStopNameMatch(string? rowLabel, string? hint)
    {
        var a = CleanStopHint(rowLabel);
        var b = CleanStopHint(hint);
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
               b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractCourseSuffix(string? lineCourse)
    {
        if (string.IsNullOrWhiteSpace(lineCourse))
        {
            return null;
        }

        var parts = lineCourse.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : null;
    }

    private static string BuildLineCourseWithSuffix(string line, string? courseSuffix) =>
        string.IsNullOrWhiteSpace(courseSuffix) ? line : $"{line}/{courseSuffix}";

    /// <summary>681/02 → 682/02 (Hbf-Verknüpfung), sonst unverändert.</summary>
    private static string? BuildSisterLineCourse(string? lineCourse, string? courseSuffix)
    {
        if (string.IsNullOrWhiteSpace(lineCourse))
        {
            return null;
        }

        var normalized = RouteDisplayHelper.NormalizeLineCourse(lineCourse);
        if (normalized.StartsWith("681", StringComparison.Ordinal))
        {
            return BuildLineCourseWithSuffix("682", courseSuffix);
        }

        if (normalized.StartsWith("682", StringComparison.Ordinal))
        {
            return BuildLineCourseWithSuffix("681", courseSuffix);
        }

        return normalized;
    }

    private static string? ResolveTripNumberForColumn(
        Dictionary<int, List<(string Number, double Y)>> tripNumbersByColumn,
        int colIndex,
        List<TimeCell> colTimes)
    {
        if (!tripNumbersByColumn.TryGetValue(colIndex, out var list) || list.Count == 0)
        {
            return null;
        }

        // Häufigste Nummer in der Spalte
        var grouped = list.GroupBy(x => x.Number).OrderByDescending(g => g.Count()).ToList();
        if (grouped[0].Count() > 1)
        {
            return grouped[0].Key;
        }

        // Sonst Nummer nahe am Terminus (unten auf der Karte = kleinere Y in PdfPig oft Wendepunkt)
        var preferLowY = colTimes.Any(c => IsTerminusAbLabel(c.RowLabel) || IsTerminusAnLabel(c.RowLabel));
        return (preferLowY ? list.OrderBy(x => x.Y) : list.OrderByDescending(x => x.Y))
            .First()
            .Number;
    }

    /// <summary>
    /// Echte Terminus-Abfahrt („Brockenberg ab“), nicht „Graf-Wilh.-Pl. ab/an“.
    /// </summary>
    private static bool IsTerminusAbLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var l = label.Trim().ToLowerInvariant();
        if (l.Contains("an/ab", StringComparison.Ordinal) ||
            l.Contains("ab/an", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Regex.IsMatch(l, @"\bab\b"))
        {
            return false;
        }

        return IsKnownTerminusLabel(l);
    }

    private static bool IsTerminusAnLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var l = label.Trim().ToLowerInvariant();
        if (l.Contains("an/ab", StringComparison.Ordinal) ||
            l.Contains("ab/an", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Regex.IsMatch(l, @"\ban\b"))
        {
            return false;
        }

        return IsKnownTerminusLabel(l);
    }

    private static bool IsKnownTerminusLabel(string lowerLabel) =>
        lowerLabel.Contains("brockenberg", StringComparison.Ordinal) ||
        lowerLabel.Contains("hästen", StringComparison.Ordinal) ||
        lowerLabel.Contains("hasten", StringComparison.Ordinal) ||
        lowerLabel.Contains("kannenhof", StringComparison.Ordinal) ||
        lowerLabel.Contains("eschbach", StringComparison.Ordinal) ||
        lowerLabel.Contains("obenitter", StringComparison.Ordinal) ||
        lowerLabel.Contains("rüden", StringComparison.Ordinal) ||
        lowerLabel.Contains("ruden", StringComparison.Ordinal) ||
        lowerLabel.Contains("widdert", StringComparison.Ordinal) ||
        // Linie 683 u. a.
        (lowerLabel.Contains("burger", StringComparison.Ordinal) &&
         lowerLabel.Contains("bahnhof", StringComparison.Ordinal)) ||
        ((lowerLabel.Contains("voh", StringComparison.Ordinal) ||
          lowerLabel.Contains("vohwinkel", StringComparison.Ordinal)) &&
         lowerLabel.Contains("bahnhof", StringComparison.Ordinal)) ||
        lowerLabel.Contains("krähenhöhe", StringComparison.Ordinal) ||
        lowerLabel.Contains("krahenhöhe", StringComparison.Ordinal) ||
        lowerLabel.Contains("krahenhoehe", StringComparison.Ordinal) ||
        lowerLabel.Contains("mangenberg", StringComparison.Ordinal);

    private static string GuessSectionLabel(IReadOnlyList<TimeCell> colTimes)
    {
        foreach (var c in colTimes)
        {
            var l = c.RowLabel;
            if (l.Contains("Brockenberg", StringComparison.OrdinalIgnoreCase))
            {
                return "Höhscheid-Brockenberg";
            }

            if (l.Contains("Hästen", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Hasten", StringComparison.OrdinalIgnoreCase))
            {
                return "Hästen";
            }

            if (l.Contains("Kannenhof", StringComparison.OrdinalIgnoreCase))
            {
                return "Kannenhof";
            }

            if (l.Contains("Eschbach", StringComparison.OrdinalIgnoreCase))
            {
                return "Eschbach";
            }

            if (l.Contains("Rüden", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Ruden", StringComparison.OrdinalIgnoreCase))
            {
                return "Rüden";
            }
        }

        return string.Empty;
    }

    private static List<double> ClusterColumns(IEnumerable<double> xs)
    {
        var sorted = xs.OrderBy(x => x).ToList();
        if (sorted.Count == 0)
        {
            return [];
        }

        var clusters = new List<List<double>> { new() { sorted[0] } };
        for (var i = 1; i < sorted.Count; i++)
        {
            var x = sorted[i];
            if (Math.Abs(x - clusters[^1][^1]) <= ColumnClusterTolerance)
            {
                clusters[^1].Add(x);
            }
            else
            {
                clusters.Add([x]);
            }
        }

        // Nur Spalten mit genug Treffern (echte Zeitsäulen)
        return clusters
            .Where(c => c.Count >= 2)
            .Select(c => c.Average())
            .OrderBy(x => x)
            .ToList();
    }

    /// <summary>Jede Zeitzelle genau der nächstgelegenen Spalte zuordnen (keine Überlappung).</summary>
    private static Dictionary<int, List<TimeCell>> AssignTimeCellsToColumns(
        IReadOnlyList<TimeCell> timeCells,
        IReadOnlyList<double> columns)
    {
        var map = new Dictionary<int, List<TimeCell>>();
        foreach (var cell in timeCells)
        {
            var col = NearestColumn(columns, cell.MidX);
            if (col < 0)
            {
                continue;
            }

            if (!map.TryGetValue(col, out var list))
            {
                list = [];
                map[col] = list;
            }

            list.Add(cell);
        }

        return map;
    }

    private static int NearestColumn(IReadOnlyList<double> columns, double x)
    {
        if (columns.Count == 0)
        {
            return -1;
        }

        var best = 0;
        var bestDist = Math.Abs(columns[0] - x);
        for (var i = 1; i < columns.Count; i++)
        {
            var d = Math.Abs(columns[i] - x);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        // Nur echte Ausreißer verwerfen; Zuordnung immer eindeutig zur nächsten Spalte
        var maxDist = ColumnClusterTolerance * 2.5;
        if (columns.Count >= 2)
        {
            var minGap = columns[1] - columns[0];
            for (var i = 2; i < columns.Count; i++)
            {
                minGap = Math.Min(minGap, columns[i] - columns[i - 1]);
            }

            maxDist = Math.Max(maxDist, minGap * 0.55);
        }

        return bestDist <= maxDist ? best : -1;
    }

    private static List<ParsedTrip> ExtractTripsLegacy(
        IReadOnlyList<string> lines,
        string? defaultLineCourse)
    {
        var trips = new List<ParsedTrip>();
        var directionToggle = UmlaufImportPlanner.Direction.Hin;
        var timeRegex = new Regex(@"\b\d{1,2}:\d{2}\b", RegexOptions.CultureInvariant);
        var tripRegex = new Regex(@"\b\d{3,4}\b", RegexOptions.CultureInvariant);

        foreach (var line in lines)
        {
            var times = timeRegex.Matches(line)
                .Select(m => NormalizeTime(m.Value))
                .Where(t => t.Length > 0)
                .Distinct()
                .ToList();
            if (times.Count == 0)
            {
                continue;
            }

            var tripNumbers = tripRegex.Matches(line)
                .Select(m => m.Value)
                .Where(n => !LooksLikeClockDigits(n))
                .Select(NormalizeTripNumber)
                .Distinct()
                .ToList();
            if (tripNumbers.Count == 0)
            {
                continue;
            }

            var tripNo = tripNumbers[0];
            if (trips.Any(t =>
                    string.Equals(t.TripNumber, tripNo, StringComparison.Ordinal) &&
                    string.Equals(t.StartTime, times[0], StringComparison.Ordinal)))
            {
                continue;
            }

            trips.Add(new ParsedTrip
            {
                Direction = directionToggle,
                TripNumber = tripNo,
                StartTime = times[0],
                EndTime = times.Count > 1 ? times[^1] : null,
                LineCourseHint = defaultLineCourse
            });
            directionToggle = directionToggle == UmlaufImportPlanner.Direction.Hin
                ? UmlaufImportPlanner.Direction.Rueck
                : UmlaufImportPlanner.Direction.Hin;
        }

        return trips;
    }

    private static IReadOnlyList<DutyOperatingDay> ParseOperatingDays(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("montag bis freitag", StringComparison.Ordinal) ||
            lower.Contains("mo-fr", StringComparison.Ordinal) ||
            lower.Contains("mo – fr", StringComparison.Ordinal))
        {
            return
            [
                DutyOperatingDay.Monday,
                DutyOperatingDay.Tuesday,
                DutyOperatingDay.Wednesday,
                DutyOperatingDay.Thursday,
                DutyOperatingDay.Friday
            ];
        }

        return DutyOperatingDayHelper.AllDays.Select(d => d.Day).ToList();
    }

    private static bool IsTimeToken(string text) => TimeTokenRegex.IsMatch(text.Trim());

    private static bool IsTripToken(string text)
    {
        var t = text.Trim();
        return TripTokenRegex.IsMatch(t) && !LooksLikeClockDigits(t);
    }

    private static bool LooksLikeClockDigits(string n)
    {
        if (n.Length is < 3 or > 4 || !n.All(char.IsDigit))
        {
            return false;
        }

        // 4:20 als „420“ kommt in dieser PDF nicht vor; 3-stellig oft Fahrtnr.
        // Nur klare Uhrzeit-Kompaktformen 0hmm / hhmm mit gültigen Minuten aussortieren,
        // wenn Stunde plausibel und Nummer wie Depotzeit (411, 2023) – eher selten als Fahrt.
        if (n.Length == 4)
        {
            var hour = int.Parse(n[..2], CultureInfo.InvariantCulture);
            var min = int.Parse(n[2..], CultureInfo.InvariantCulture);
            // Fahrtnummern 1000+ unüblich; 0444 wäre mit führender 0
            if (hour is >= 0 and <= 23 && min is >= 0 and <= 59 && hour >= 10)
            {
                // z. B. 2023 – eher keine Fahrtnummer auf SWS-Karten
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTripNumber(string raw)
    {
        var trimmed = raw.Trim().TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    private static string NormalizeLineCourse(string raw) =>
        RouteDisplayHelper.NormalizeLineCourse(raw.Replace(" ", string.Empty, StringComparison.Ordinal));

    private static string NormalizeTime(string raw)
    {
        var t = raw.Trim().Replace('.', ':');
        return RouteScheduleTimeCalculator.TryParseTime(t, out _)
            ? RouteScheduleTimeCalculator.NormalizeTimeInput(t)
            : string.Empty;
    }

    private static int ParseMinutes(string time)
    {
        if (!RouteScheduleTimeCalculator.TryParseTime(time, out var t))
        {
            return int.MaxValue;
        }

        return t.Hour * 60 + t.Minute;
    }
}
