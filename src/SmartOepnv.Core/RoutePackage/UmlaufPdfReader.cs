using System.Globalization;
using System.Text.RegularExpressions;
using SmartOepnv.Core.Dienstvorlagen;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Liest Solingen-Umlaufkarten-PDFs spaltenbasiert (PdfPig-Wortpositionen).
/// Jede Zeitsäule ≈ eine Fahrt; Fahrtnummern stehen typisch an Wendepunkten.
/// </summary>
public static class UmlaufPdfReader
{
    private static readonly Regex DutyHeaderRegex = new(
        @"(?<line>(?:NE\s*)?\d{2,4}\s*/\s*\d{1,3})\s*Betr\.?\s*Hof\s+(?<aus>\d{1,2}[:.]\d{2})\s*[-–]\s*(?<ein>\d{1,2}[:.]\d{2})\s*Betr\.?\s*Hof",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CompactDutyRegex = new(
        @"Betr\.?\s*Hof\s+(?<aus>\d{1,2}[:.]\d{2})\s*[-–]\s*(?<ein>\d{1,2}[:.]\d{2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LineCourseOnlyRegex = new(
        @"\b(?<line>\d{2,4}\s*/\s*\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Dateiname z. B. „Mo 683-01“, „683_01“, „683/01“.</summary>
    private static readonly Regex FileNameLineCourseRegex = new(
        @"(?<line>\d{2,4})\s*[-_/]\s*(?<course>\d{1,3})(?:\D|$)",
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

    private sealed record PdfWord(string Text, double MidX, double Y, bool IsRed = false);

    private sealed record TimeCell(string Time, double MidX, double Y, string RowLabel);

    private sealed record BuiltHalf(
        ParsedTrip Trip,
        int ColIndex,
        bool Upper,
        IReadOnlyList<TimeCell> Times);

    public static ParseResult Read(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF nicht gefunden.", pdfPath);
        }

        // Wichtig: Seiten getrennt parsen – gleiche Y-Koordinaten über Seiten
        // würden Morgen- und Abendzeiten in einer Spalte vermischen (z. B. 21:15 + 06:46).
        var allWords = new List<PdfWord>();
        var trips = new List<ParsedTrip>();
        string? lineCourse = null;
        string? ausfahrt = null;
        string? einfahrt = null;
        IReadOnlyList<DutyOperatingDay> days = [];

        using (var document = PdfDocument.Open(pdfPath))
        {
            var markBands = ExtractLineMarkBands(pdfPath, null);
            foreach (var page in document.GetPages())
            {
                var pageWords = ExtractWordsFromPage(page);
                if (pageWords.Count == 0)
                {
                    continue;
                }

                allWords.AddRange(pageWords);
                var pageTexts = BuildRowTexts(pageWords);
                var joinedPage = string.Join(" ", pageTexts);
                if (lineCourse is null)
                {
                    ParseHeader(joinedPage, out lineCourse, out ausfahrt, out einfahrt);
                    lineCourse = ResolveLineCourseFromHeaderAndFileName(lineCourse, pdfPath);
                    days = ParseOperatingDays(joinedPage);
                    // Bänder erneut mit bekanntem Linienhinweis (681/682)
                    markBands = ExtractLineMarkBands(pdfPath, lineCourse);
                }

                var pageTrips = ExtractTripsFromColumns(
                    pageWords,
                    lineCourse,
                    markBands,
                    ExtractGrafWilhelmContinuationArrows(page));
                trips.AddRange(pageTrips);
            }
        }

        var lineTexts = BuildRowTexts(allWords);

        if (allWords.Count == 0)
        {
            return new ParseResult
            {
                SourcePath = pdfPath,
                Warning =
                    "In der PDF wurde kein Text gefunden (vermutlich Scan/Bild). " +
                    "Bitte Fahrten im Dialog manuell anlegen."
            };
        }

        if (lineCourse is null)
        {
            var joined = string.Join(" ", lineTexts);
            ParseHeader(joined, out lineCourse, out ausfahrt, out einfahrt);
            lineCourse = ResolveLineCourseFromHeaderAndFileName(lineCourse, pdfPath);
            days = ParseOperatingDays(joined);
        }

        string? warning = null;
        if (trips.Count == 0)
        {
            // Fallback: alte Zeilenheuristik über alle Seiten
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

        // Nachtkarten (nur 23:xx und 0:xx–5:xx): Abendfahrt zuerst, damit 671 um 23:30 die erste Runde ist.
        var nightCard = trips.All(t =>
        {
            var m = ParseMinutes(t.StartTime);
            return m >= 20 * 60 || m < 6 * 60;
        });
        var ordered = trips
            .OrderBy(t => nightCard ? NightCardMinutes(t.StartTime) : ParseMinutes(t.StartTime))
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

    private static List<PdfWord> ExtractWordsFromPage(Page page)
    {
        var result = new List<PdfWord>();
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
                word.BoundingBox.Bottom,
                IsRedWord(word)));
        }

        return result;
    }

    /// <summary>Rote Schrift = Richtungs-/Fahrtwechsel (Hin ↔ Rück) auf Solingen-Umlaufkarten.</summary>
    private static bool IsRedWord(Word word)
    {
        try
        {
            foreach (var letter in word.Letters)
            {
                if (IsRedishColor(letter.Color) || IsRedishColor(letter.FillColor))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Farbe nicht lesbar → nicht als rot werten
        }

        return false;
    }

    private static bool IsRedishColor(IColor? color)
    {
        if (color is null)
        {
            return false;
        }

        try
        {
            var (r, g, b) = color.ToRGBValues();
            // Kräftiges Rot (Fahrtnummern), kein Orange/Braun (Werte 0..1)
            return r >= 0.55 && g <= 0.40 && b <= 0.40 && r > g + 0.15 && r > b + 0.15;
        }
        catch
        {
            return false;
        }
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

        // Farbbänder gelb/grün nur für 681/682-Umlaufkarten (Hästen/Brockenberg)
        if (!IsHbfPairLine(defaultLineCourse))
        {
            return [];
        }

        var courseSuffix = ExtractCourseSuffix(defaultLineCourse);
        var line681 = BuildLineCourseWithSuffix("681", courseSuffix);
        var line682 = BuildLineCourseWithSuffix("682", courseSuffix);

        // Farbe entscheidet (nicht Y) – sonst 681/682 vertauscht
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
        IReadOnlyList<LineMarkBand> markBands,
        IReadOnlyList<(double Left, double Right, double Y)> continuationArrows)
    {
        if (TryExtractNeNightTrips(words, defaultLineCourse, out var neTrips))
        {
            return neTrips;
        }

        // Spaltenfahrplan L.685 / L.691: jede Spalte von oben nach unten, nächste Spalte wieder oben.
        if (TryExtractLineColumnTrips(words, defaultLineCourse, out var lineColumnTrips))
        {
            return lineColumnTrips;
        }

        // Mix-Karten: „Linie 692“ / „Linie 696“ / „Linie 698“ – Abschnitte getrennt, Kurs vom Kopf.
        // Steht vor dem Spalten-Sonderplan: Seite 3 der 696 hat rechts zusätzlich die 693
        // mit eigenen Haltestellennamen. Das sind keine nebeneinanderstehenden 682-Spalten.
        var mixSections = FindMixLineSections(words);
        if (mixSections.Count >= 2)
        {
            var courseSuffix = ExtractCourseSuffix(defaultLineCourse);
            var mixTrips = new List<ParsedTrip>();
            foreach (var section in mixSections)
            {
                var sectionLine = BuildLineCourseWithSuffix(section.LineNumber, courseSuffix);
                var sectionWords = words
                    .Where(w => w.Y <= section.TopY + 1.0 && w.Y > section.BottomY)
                    .Where(w => !TimeBelongsToBandAbove(words, w, section.TopY))
                    .ToList();
                // Graf-Wilhelm der 693 steht auf der Zeile „Linie 696“ und bleibt beim oberen Band.
                foreach (var time in words)
                {
                    if (!TimeBelongsToBandAbove(words, time, section.BottomY))
                    {
                        continue;
                    }

                    if (!sectionWords.Contains(time))
                    {
                        sectionWords.Add(time);
                    }

                    foreach (var labelWord in words)
                    {
                        if (Math.Abs(labelWord.Y - time.Y) > 2.5 ||
                            labelWord.MidX >= time.MidX ||
                            IsTimeToken(labelWord.Text) ||
                            sectionWords.Contains(labelWord))
                        {
                            continue;
                        }

                        sectionWords.Add(labelWord);
                    }
                }

                foreach (var number in words)
                {
                    if (!IsTripToken(number.Text) || sectionWords.Contains(number))
                    {
                        continue;
                    }

                    // 411 steht knapp über „Linie 692“.
                    if (number.Y > section.TopY + 1.0 && number.Y <= section.TopY + 16.0)
                    {
                        sectionWords.Add(number);
                        continue;
                    }

                    // 410 unter der 693, 636 auf der Zeile „Linie 698“ in der 697-Spalte.
                    var onNextMarker = Math.Abs(number.Y - section.BottomY) <= 4.0;
                    var justBelow = number.Y <= section.BottomY - 4.0 &&
                                    number.Y > section.BottomY - 18.0;
                    if (!onNextMarker && !justBelow)
                    {
                        continue;
                    }

                    var nearUpperTime = sectionWords.Any(t =>
                        IsTimeToken(t.Text) && Math.Abs(t.MidX - number.MidX) < 22);
                    var nearLowerTime = words.Any(t =>
                        IsTimeToken(t.Text) &&
                        Math.Abs(t.Y - number.Y) < 4 &&
                        Math.Abs(t.MidX - number.MidX) < 22);
                    if (nearUpperTime && !nearLowerTime)
                    {
                        sectionWords.Add(number);
                    }
                }
                if (sectionWords.Count < 4)
                {
                    continue;
                }

                // Kein 681/682-Umbiegen: Abschnitt sagt explizit z. B. 682 → Kurs vom Kopf bleibt
                mixTrips.AddRange(
                    ExtractTripsFromColumnsCore(
                        sectionWords,
                        sectionLine,
                        markBands,
                        remapHbfPairLines: false,
                        continuationArrows));
            }

            return mixTrips
                .Where(t => !string.IsNullOrWhiteSpace(t.StartTime) &&
                            !string.IsNullOrWhiteSpace(t.EndTime) &&
                            !string.Equals(t.StartTime, t.EndTime, StringComparison.Ordinal))
                .OrderBy(t => ParseMinutes(t.StartTime))
                .ThenBy(t => t.TripNumber.StartsWith('?'))
                .ThenBy(t => t.TripNumber, StringComparer.Ordinal)
                .ToList();
        }

        // Sonderplan (Sa/So): jede Zeitsäule hat links ihren eigenen Haltestellennamen.
        // Der Normalplan 681/682 hat die Namen nur links – den nicht anfassen.
        var sideBySide = TryExtractSideBySideColumnTrips(words, defaultLineCourse);
        if (sideBySide is not null)
        {
            return sideBySide;
        }

        return ExtractTripsFromColumnsCore(
            words,
            defaultLineCourse,
            markBands,
            remapHbfPairLines: true,
            continuationArrows);
    }

    /// <summary>
    /// Abschnitte einer Mix-Umlaufkarte anhand „Linie 691“ / „Line 682“ (Y von Marker bis nächster Marker).
    /// </summary>
    private static List<MixLineSection> FindMixLineSections(IReadOnlyList<PdfWord> words)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var rows = words
            .GroupBy(w => (int)Math.Round(w.Y / RowTolerance))
            .OrderByDescending(g => g.Key)
            .Select(g => g.OrderBy(w => w.MidX).ToList())
            .ToList();

        var markers = new List<(string Line, double Y)>();
        foreach (var row in rows)
        {
            var label = row.FirstOrDefault(w =>
                w.Text.Equals("Linie", StringComparison.OrdinalIgnoreCase) ||
                w.Text.Equals("Line", StringComparison.OrdinalIgnoreCase));
            if (label is null)
            {
                continue;
            }

            var num = row
                .Where(w =>
                    w.Text.Length is >= 2 and <= 4 &&
                    w.Text.All(char.IsDigit) &&
                    Math.Abs(w.MidX - label.MidX) < 90)
                .OrderBy(w => Math.Abs(w.MidX - label.MidX))
                .FirstOrDefault();
            if (num is null)
            {
                continue;
            }

            // Keine Fahrtnummern (662) – Linien typisch 2–4 Ziffern links am Rand
            if (num.MidX > 130)
            {
                continue;
            }

            markers.Add((num.Text, label.Y));
        }

        if (markers.Count < 2)
        {
            return [];
        }

        // Nahe doppelte Marker verwerfen
        var distinct = new List<(string Line, double Y)>();
        foreach (var m in markers.OrderByDescending(x => x.Y))
        {
            if (distinct.Count > 0 && Math.Abs(distinct[^1].Y - m.Y) < 12)
            {
                continue;
            }

            distinct.Add(m);
        }

        if (distinct.Count < 2)
        {
            return [];
        }

        var pageMinY = words.Min(w => w.Y) - 8;
        var result = new List<MixLineSection>();
        for (var i = 0; i < distinct.Count; i++)
        {
            var top = distinct[i].Y;
            var bottom = i + 1 < distinct.Count ? distinct[i + 1].Y : pageMinY;
            result.Add(new MixLineSection(distinct[i].Line, top, bottom));
        }

        return result;
    }

    private sealed record MixLineSection(string LineNumber, double TopY, double BottomY);

    /// <summary>
    /// Sonderumlauf mit Namen direkt links an jeder Zeitsäule (z. B. Sa 682-32).
    /// Null, wenn das nicht dieses Muster ist – dann gilt der Normalplan-Parser.
    /// </summary>
    private static List<ParsedTrip>? TryExtractSideBySideColumnTrips(
        IReadOnlyList<PdfWord> words,
        string? defaultLineCourse)
    {
        if (words.Count < 8)
        {
            return null;
        }

        var times = words
            .Where(w => IsTimeToken(w.Text) && !IsDepotHeaderClock(words, w))
            .ToList();
        var columns = ClusterColumns(times.Select(t => t.MidX));
        if (columns.Count < 2)
        {
            return null;
        }

        var labelRowCount = new int[columns.Count];
        var seenRows = new HashSet<(int Col, int Bucket)>();
        foreach (var time in times)
        {
            var col = NearestColumn(columns, time.MidX);
            if (col < 0)
            {
                continue;
            }

            var hint = CleanStopHint(JoinSideLabel(words, times, columns, time));
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            if (seenRows.Add((col, (int)Math.Round(time.Y / 4.0))))
            {
                labelRowCount[col]++;
            }
        }

        var labeledColumns = 0;
        var laterColumnLabeled = false;
        for (var i = 0; i < columns.Count; i++)
        {
            if (labelRowCount[i] < 4)
            {
                continue;
            }

            labeledColumns++;
            if (i > 0)
            {
                laterColumnLabeled = true;
            }
        }

        // Normalplan: nur die linke Namensspalte. Sonderplan: auch Spalte 2, 3, …
        if (!laterColumnLabeled || labeledColumns < 2)
        {
            return null;
        }

        var line = string.IsNullOrWhiteSpace(defaultLineCourse)
            ? null
            : NormalizeLineCourse(defaultLineCourse);
        var trips = new List<ParsedTrip>();
        for (var col = 0; col < columns.Count; col++)
        {
            var colTimes = times
                .Where(t => NearestColumn(columns, t.MidX) == col)
                .ToList();
            if (colTimes.Count < 2)
            {
                continue;
            }

            var points = new List<(string Hint, string Time, double Y)>();
            foreach (var time in colTimes.OrderByDescending(t => t.Y))
            {
                var hint = CleanStopHint(JoinSideLabel(words, times, columns, time));
                if (string.IsNullOrWhiteSpace(hint) || IsStopNameNoise(hint))
                {
                    continue;
                }

                points.Add((hint, NormalizeTime(time.Text), time.Y));
            }

            if (points.Count < 2)
            {
                continue;
            }

            var start = points[0];
            var end = points[^1];
            if (string.Equals(start.Time, end.Time, StringComparison.Ordinal))
            {
                continue;
            }

            var topY = colTimes.Max(t => t.Y);
            var numberWord = words
                .Where(w =>
                    IsTripToken(w.Text) &&
                    Math.Abs(w.MidX - columns[col]) <= 22 &&
                    w.Y > topY - 1 &&
                    w.Y < topY + 36)
                .OrderBy(w => w.Y)
                .FirstOrDefault();
            var tripNo = numberWord is null
                ? $"?{col + 1}"
                : NormalizeTripNumber(numberWord.Text);

            var towardHbf = IsHauptbahnhofHint(end.Hint) && !IsHauptbahnhofHint(start.Hint);
            var fromHbf = IsHauptbahnhofHint(start.Hint) && !IsHauptbahnhofHint(end.Hint);
            var direction = towardHbf
                ? UmlaufImportPlanner.Direction.Hin
                : fromHbf
                    ? UmlaufImportPlanner.Direction.Rueck
                    : UmlaufImportPlanner.Direction.Hin;

            trips.Add(new ParsedTrip
            {
                Direction = direction,
                TripNumber = tripNo,
                StartTime = start.Time,
                EndTime = end.Time,
                LineCourseHint = line,
                SectionHint = $"{start.Hint} → {end.Hint}",
                StartStopHint = start.Hint,
                EndStopHint = end.Hint,
                TimingPoints = points
                    .Select(p => new TimingPoint(p.Hint, p.Time))
                    .ToList()
            });
        }

        if (trips.Count == 0)
        {
            return null;
        }

        return trips
            .OrderBy(t => ParseMinutes(t.StartTime))
            .ThenBy(t => t.TripNumber, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsDepotHeaderClock(IReadOnlyList<PdfWord> words, PdfWord time) =>
        words.Any(w =>
            Math.Abs(w.Y - time.Y) <= 3.5 &&
            (w.Text.Contains("Betr", StringComparison.OrdinalIgnoreCase) ||
             w.Text.Equals("Betriebshof", StringComparison.OrdinalIgnoreCase)));

    private static string JoinSideLabel(
        IReadOnlyList<PdfWord> words,
        IReadOnlyList<PdfWord> times,
        IReadOnlyList<double> columns,
        PdfWord time)
    {
        const double yTol = 3.6;
        var parts = new List<PdfWord>();
        foreach (var word in words)
        {
            if (Math.Abs(word.Y - time.Y) > yTol || word.MidX < 34 || word.MidX >= time.MidX - 3)
            {
                continue;
            }

            // Name links der ersten Säule nicht der zweiten Säule zuschlagen (Normalplan).
            if (columns.Any(c => c > word.MidX + 8 && c < time.MidX - 8))
            {
                continue;
            }

            if (IsTimeToken(word.Text) || IsTripToken(word.Text) || IsSideLabelNoise(word.Text))
            {
                continue;
            }

            PdfWord? nearestRight = null;
            foreach (var other in times)
            {
                if (Math.Abs(other.Y - word.Y) > yTol || other.MidX <= word.MidX + 3)
                {
                    continue;
                }

                if (nearestRight is null || other.MidX < nearestRight.MidX)
                {
                    nearestRight = other;
                }
            }

            if (nearestRight is null ||
                Math.Abs(nearestRight.MidX - time.MidX) > 0.6 ||
                Math.Abs(nearestRight.Y - time.Y) > 0.6)
            {
                continue;
            }

            parts.Add(word);
        }

        return string.Join(" ", parts.OrderBy(p => p.MidX).Select(p => p.Text));
    }

    private static bool IsSideLabelNoise(string text)
    {
        var t = text.Trim();
        if (t is "/")
        {
            return false;
        }

        if (t.Length <= 1)
        {
            return true;
        }

        return t.All(char.IsDigit);
    }

    private static List<ParsedTrip> ExtractTripsFromColumnsCore(
        IReadOnlyList<PdfWord> words,
        string? defaultLineCourse,
        IReadOnlyList<LineMarkBand> markBands,
        bool remapHbfPairLines,
        IReadOnlyList<(double Left, double Right, double Y)> continuationArrows)
    {
        var rows = words
            .GroupBy(w => (int)Math.Round(w.Y / RowTolerance))
            .OrderByDescending(g => g.Key)
            .Select(g => g.OrderBy(w => w.MidX).ToList())
            .ToList();

        // Doppelzeilen (GWP an/ab + ab/an): Name oft nur in der 1. Zeile – 2. Zeile erbt den Namen
        var timeCells = BuildTimeCellsWithInheritedStopNames(rows);

        if (timeCells.Count < 2)
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

        // Fahrtwechsel an roter Fahrtnr. – gilt für alle Linien (Hbf, Vohwinkel, …)
        var fahrtwechsel = FindFahrtwechselSplit(rows, timeCells, defaultLineCourse);

        var courseSuffix = ExtractCourseSuffix(defaultLineCourse);
        string upperLine;
        string lowerLine;
        if (remapHbfPairLines && IsHbfPairLine(defaultLineCourse))
        {
            // Geographie nur für 681/682-Karten: oben Hästen, unten Brockenberg
            upperLine = BuildLineCourseWithSuffix("681", courseSuffix);
            lowerLine = BuildLineCourseWithSuffix("682", courseSuffix);
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
        }
        else if (!string.IsNullOrWhiteSpace(defaultLineCourse))
        {
            // Mix-Abschnitt oder andere Linie: Kopf-/Abschnittslinie inkl. Kurs behalten
            upperLine = NormalizeLineCourse(defaultLineCourse);
            lowerLine = upperLine;
        }
        else
        {
            upperLine = BuildLineCourseWithSuffix("681", courseSuffix);
            lowerLine = BuildLineCourseWithSuffix("682", courseSuffix);
        }

        // Sonderplan ohne Fahrtwechsel und ohne Farbbänder: die Kopfzeile gilt
        // (682 bleibt 682). Der Normalplan hat den Wechsel und bleibt 681/682.
        if (remapHbfPairLines &&
            fahrtwechsel is null &&
            markBands.Count < 2 &&
            !string.IsNullOrWhiteSpace(defaultLineCourse))
        {
            upperLine = NormalizeLineCourse(defaultLineCourse);
            lowerLine = upperLine;
        }

        // Jede Zeit genau einer Spalte (nächste) – sonst übernehmen Fahrt 2 die Zeiten von Fahrt 3
        var timesByColumn = AssignTimeCellsToColumns(timeCells, columns);

        var built = new List<BuiltHalf>();
        for (var colIndex = 0; colIndex < columns.Count; colIndex++)
        {
            if (!timesByColumn.TryGetValue(colIndex, out var colTimes) || colTimes.Count == 0)
            {
                continue;
            }

            List<TimeCell> upperTimes;
            List<TimeCell> lowerTimes;
            var columnUpperLine = upperLine;
            var columnLowerLine = lowerLine ?? upperLine;
            // 696-Karte Seite 3: rechts neben der 692 steht die 693 (Aufderhöhe), ohne „Linie 693“.
            if (ColumnLooksLike693(colTimes))
            {
                columnUpperLine = BuildLineCourseWithSuffix("693", courseSuffix);
                columnLowerLine = columnUpperLine;
            }

            var stackedBranch = false;
            var columnTurnsHere = fahrtwechsel is { } fw &&
                                  ColumnTurnsAtSplit(
                                      colTimes,
                                      tripNumbersByColumn.TryGetValue(colIndex, out var colNumbers)
                                          ? colNumbers
                                          : null,
                                      fw.SplitY,
                                      fw.TurnaroundHint);
            if (columnTurnsHere && fahrtwechsel is { } split)
            {
                var sy = split.SplitY;
                var turnHint = split.TurnaroundHint;
                // Zeiten oberhalb der Nr.-Zeile → obere Fahrt, darunter → untere
                upperTimes = colTimes.Where(c => c.Y > sy).ToList();
                lowerTimes = colTimes.Where(c => c.Y < sy).ToList();
                foreach (var onSplit in colTimes.Where(c => Math.Abs(c.Y - sy) < 0.01))
                {
                    if (!IsTurnaroundAnchor(onSplit.RowLabel, turnHint))
                    {
                        continue;
                    }

                    // Wendepunkt: Ende der oberen und Start der unteren Fahrt
                    if (!lowerTimes.Contains(onSplit))
                    {
                        lowerTimes.Add(onSplit);
                    }

                    if (!upperTimes.Contains(onSplit))
                    {
                        upperTimes.Add(onSplit);
                    }
                }
            }
            else if (TrySplit693Stacked(defaultLineCourse, colTimes, out var stackMid, out var lowerNo))
            {
                // 693 oben, unten 696 (Untenkatternberg) oder 698 (Kannenhof).
                // Wechsel an der zweiten Graf-Wilhelm-Zeile.
                upperTimes = colTimes.Where(c => c.Y > stackMid).ToList();
                lowerTimes = colTimes.Where(c => c.Y < stackMid).ToList();
                columnLowerLine = BuildLineCourseWithSuffix(lowerNo, courseSuffix);
                stackedBranch = true;
            }
            else
            {
                // Zweite Strecke auf derselben Karte (690: Eschbach neben Obenitter)
                // läuft durch die fremde Fahrtnummern-Zeile und bleibt eine Fahrt.
                upperTimes = colTimes;
                lowerTimes = [];
                if (TryOnlyBranchOn693Card(defaultLineCourse, colTimes, out var onlyNo))
                {
                    columnUpperLine = BuildLineCourseWithSuffix(onlyNo, courseSuffix);
                }
            }

            if (IsUsableHalf(upperTimes))
            {
                var turnHint = columnTurnsHere ? fahrtwechsel?.TurnaroundHint : null;
                var upperTrip = BuildTripFromHalf(
                    upperTimes,
                    tripNumbersByColumn,
                    colIndex,
                    preferUpperNumbers: true,
                    lineCourseHint: columnUpperLine,
                    sectionFallback: turnHint is null ? "Hin" : $"→{turnHint}",
                    turnaroundHint: turnHint);
                upperTrip = turnHint is null
                    ? ForceUpperHalfStartBeforeHbf(upperTrip, upperTimes)
                    : ForceUpperHalfEndAtTurnaround(upperTrip, upperTimes, turnHint);
                upperTrip = PreferOuterTerminus(upperTrip);
                upperTrip = PreferStartAtEarliestPlanPoint(upperTrip);
                built.Add(new BuiltHalf(
                    ApplyLineMark(upperTrip, upperTimes, markBands, columnUpperLine),
                    colIndex,
                    Upper: true,
                    upperTimes));
            }

            if (IsUsableHalf(lowerTimes))
            {
                var turnHint = columnTurnsHere ? fahrtwechsel?.TurnaroundHint : null;
                var lowerTrip = BuildTripFromHalf(
                    lowerTimes,
                    tripNumbersByColumn,
                    colIndex,
                    preferUpperNumbers: stackedBranch,
                    lineCourseHint: columnLowerLine,
                    sectionFallback: turnHint is null ? (stackedBranch ? "Hin" : "Rück") : $"{turnHint}→",
                    turnaroundHint: turnHint);
                lowerTrip = stackedBranch
                    ? lowerTrip
                    : turnHint is null
                        ? ForceLowerHalfStartAtHbf(lowerTrip, lowerTimes)
                        : ForceLowerHalfStartAtTurnaround(lowerTrip, lowerTimes, turnHint);
                lowerTrip = PreferOuterTerminus(lowerTrip);
                lowerTrip = PreferStartAtEarliestPlanPoint(lowerTrip);
                built.Add(new BuiltHalf(
                    ApplyLineMark(lowerTrip, lowerTimes, markBands, columnLowerLine),
                    colIndex,
                    Upper: false,
                    lowerTimes));
            }
        }

        var trips = Join695ArrowContinuations(built, columns, continuationArrows, defaultLineCourse);
        return trips
            .Where(t => !string.IsNullOrWhiteSpace(t.StartTime) &&
                        !string.IsNullOrWhiteSpace(t.EndTime) &&
                        !string.Equals(t.StartTime, t.EndTime, StringComparison.Ordinal))
            .OrderBy(t => ParseMinutes(t.StartTime))
            .ThenBy(t => t.TripNumber.StartsWith('?'))
            .ThenBy(t => t.TripNumber, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Linie 695: ein flacher Pfeil unter der GWP-Ankunft verbindet diese Spalte
    /// mit der Abfahrt am Graf-Wilhelm-Platz in der nächsten Spalte.
    /// Die Ankunft fällt weg, die Abfahrt (z. B. 6:30) läuft weiter bis Abteiweg.
    /// </summary>
    private static List<ParsedTrip> Join695ArrowContinuations(
        List<BuiltHalf> halves,
        IReadOnlyList<double> columns,
        IReadOnlyList<(double Left, double Right, double Y)> arrows,
        string? lineCourse)
    {
        if (halves.Count == 0 ||
            arrows.Count == 0 ||
            !string.Equals(ExtractLinePrefix(lineCourse), "695", StringComparison.Ordinal))
        {
            return halves.Select(h => h.Trip).ToList();
        }

        var consumed = new HashSet<int>();
        var trips = new List<ParsedTrip>(halves.Count);
        for (var i = 0; i < halves.Count; i++)
        {
            if (consumed.Contains(i))
            {
                continue;
            }

            var half = halves[i];
            if (!TryFind695Continuation(half, halves, columns, arrows, consumed, out var contIndex))
            {
                trips.Add(half.Trip);
                continue;
            }

            consumed.Add(contIndex);
            trips.Add(MergeArrivalIntoDeparture(half.Trip, halves[contIndex].Trip));
        }

        return trips;
    }

    private static bool TryFind695Continuation(
        BuiltHalf arrival,
        List<BuiltHalf> halves,
        IReadOnlyList<double> columns,
        IReadOnlyList<(double Left, double Right, double Y)> arrows,
        HashSet<int> consumed,
        out int continuationIndex)
    {
        continuationIndex = -1;
        var endTime = arrival.Trip.EndTime;
        if (string.IsNullOrWhiteSpace(endTime) ||
            !IsGrafWilhelmLabel(arrival.Trip.EndStopHint) ||
            arrival.ColIndex < 0 ||
            arrival.ColIndex + 1 >= columns.Count)
        {
            return false;
        }

        var endCell = arrival.Times.LastOrDefault(c =>
            string.Equals(c.Time, endTime, StringComparison.Ordinal) &&
            IsGrafWilhelmLabel(c.RowLabel));
        if (endCell is null)
        {
            return false;
        }

        var left = columns[arrival.ColIndex];
        var right = columns[arrival.ColIndex + 1];
        // Pfeil beginnt an der Ankunftsspalte und reicht zur nächsten Spalte.
        var arrowHere = arrows.Any(a =>
            a.Left >= left - 4 &&
            a.Left <= left + 16 &&
            a.Right > left + 12 &&
            a.Right < right + 12 &&
            Math.Abs(a.Y - endCell.Y) <= 20);
        if (!arrowHere)
        {
            return false;
        }

        var endMin = ParseMinutes(endTime);
        var bestGap = int.MaxValue;
        for (var i = 0; i < halves.Count; i++)
        {
            if (consumed.Contains(i))
            {
                continue;
            }

            var next = halves[i];
            if (next.ColIndex != arrival.ColIndex + 1 ||
                !IsGrafWilhelmLabel(next.Trip.StartStopHint) ||
                string.IsNullOrWhiteSpace(next.Trip.StartTime))
            {
                continue;
            }

            var gap = ParseMinutes(next.Trip.StartTime) - endMin;
            if (gap < 1 || gap > 20 || gap >= bestGap)
            {
                continue;
            }

            bestGap = gap;
            continuationIndex = i;
        }

        return continuationIndex >= 0;
    }

    private static ParsedTrip MergeArrivalIntoDeparture(ParsedTrip arrival, ParsedTrip departure)
    {
        var points = new List<TimingPoint>(arrival.TimingPoints);
        while (points.Count > 0 &&
               IsGrafWilhelmLabel(points[^1].StopHint) &&
               string.Equals(points[^1].Time, arrival.EndTime, StringComparison.Ordinal))
        {
            points.RemoveAt(points.Count - 1);
        }

        points.AddRange(departure.TimingPoints);
        return new ParsedTrip
        {
            Direction = arrival.Direction,
            TripNumber = arrival.TripNumber,
            StartTime = arrival.StartTime,
            EndTime = departure.EndTime,
            LineCourseHint = arrival.LineCourseHint,
            SectionHint = arrival.SectionHint,
            StartStopHint = arrival.StartStopHint,
            EndStopHint = departure.EndStopHint,
            TimingPoints = points,
            MarkColorR = arrival.MarkColorR,
            MarkColorG = arrival.MarkColorG,
            MarkColorB = arrival.MarkColorB
        };
    }

    /// <summary>
    /// Flacher Umriss unter der unteren Graf-Wilhelm-Zeile. Zeigt auf die Abfahrt
    /// in der rechten Nachbarspalte. Oben auf der Karte liegende Pfeile sind höher
    /// und werden über die Y-Nähe zur Ankunft ausgeschlossen.
    /// </summary>
    private static List<(double Left, double Right, double Y)> ExtractGrafWilhelmContinuationArrows(Page page)
    {
        var arrows = new List<(double Left, double Right, double Y)>();
        foreach (var path in page.ExperimentalAccess.Paths)
        {
            if (path.IsClipping || !path.IsStroked || path.IsFilled)
            {
                continue;
            }

            var box = path.GetBoundingRectangle();
            if (box is null)
            {
                continue;
            }

            var w = box.Value.Width;
            var h = box.Value.Height;
            if (w < 22 || w > 36 || h < 5 || h > 12)
            {
                continue;
            }

            arrows.Add((
                box.Value.Left,
                box.Value.Right,
                (box.Value.Bottom + box.Value.Top) / 2.0));
        }

        return arrows;
    }

    /// <summary>
    /// Die Fahrtnummern-Zeile trennt nur Spalten, die dort wirklich wenden
    /// (Nummer in der Zeile, oder derselbe Wendepunkt oberhalb und unterhalb).
    /// Eine zweite Strecke, die an der Zeile vorbeiläuft, bleibt ganz.
    /// </summary>
    private static bool ColumnTurnsAtSplit(
        IReadOnlyList<TimeCell> colTimes,
        IReadOnlyList<(string Number, double Y)>? tripNumbers,
        double splitY,
        string turnaroundHint)
    {
        var above = colTimes.Any(c => c.Y > splitY + 4);
        var below = colTimes.Any(c => c.Y < splitY - 4);
        if (!above || !below)
        {
            return false;
        }

        if (tripNumbers != null && tripNumbers.Any(n => Math.Abs(n.Y - splitY) < 22))
        {
            return true;
        }

        var turnAbove = colTimes.Any(c =>
            c.Y > splitY + 2 && IsTurnaroundAnchor(c.RowLabel, turnaroundHint));
        var turnBelow = colTimes.Any(c =>
            c.Y < splitY - 2 && IsTurnaroundAnchor(c.RowLabel, turnaroundHint));
        return turnAbove && turnBelow;
    }

    /// <summary>
    /// Samstagskarte 693: oben Aufderhöhe ↔ Graf-Wilhelm-Platz. Dieselbe Spalte
    /// geht unten weiter, der Wechsel ist die zweite Graf-Wilhelm-Zeile.
    /// 696 = Untenkatternberg (Stübchen, Gläßnerstraße), 698 = Kannenhof (Wupperstraße).
    /// Die Liniennummer steht nicht im PDF-Text.
    /// </summary>
    private static bool TrySplit693Stacked(
        string? lineCourse,
        IReadOnlyList<TimeCell> colTimes,
        out double splitY,
        out string lowerLine)
    {
        splitY = 0;
        lowerLine = string.Empty;
        if (!string.Equals(ExtractLinePrefix(lineCourse), "693", StringComparison.Ordinal))
        {
            return false;
        }

        var centers = GrafWilhelmRowCenters(colTimes);
        if (centers.Count < 2 || centers[0] - centers[1] <= 24)
        {
            return false;
        }

        var mid = (centers[0] + centers[1]) / 2.0;
        if (!colTimes.Any(c => c.Y > mid && Is693BranchStop(c.RowLabel)) ||
            !TryBranchLine(colTimes.Where(c => c.Y < mid), out lowerLine))
        {
            return false;
        }

        splitY = mid;
        return true;
    }

    /// <summary>Spalte nur mit 696- oder 698-Halten, Kopf der Karte ist trotzdem 693.</summary>
    private static bool TryOnlyBranchOn693Card(
        string? lineCourse,
        IReadOnlyList<TimeCell> colTimes,
        out string line)
    {
        line = string.Empty;
        if (!string.Equals(ExtractLinePrefix(lineCourse), "693", StringComparison.Ordinal) ||
            colTimes.Any(c => Is693BranchStop(c.RowLabel)))
        {
            return false;
        }

        return TryBranchLine(colTimes, out line);
    }

    /// <summary>696 oder 698, nicht beides in derselben Spalte.</summary>
    private static bool TryBranchLine(IEnumerable<TimeCell> cells, out string line)
    {
        var has696 = false;
        var has698 = false;
        foreach (var cell in cells)
        {
            has696 |= Is696BranchStop(cell.RowLabel);
            has698 |= Is698BranchStop(cell.RowLabel);
        }

        if (has696 == has698)
        {
            line = string.Empty;
            return false;
        }

        line = has696 ? "696" : "698";
        return true;
    }

    /// <summary>Eine Y-Mitte je Graf-Wilhelm-Zeile; An/Ab derselben Zeile bleibt ein Punkt.</summary>
    private static List<double> GrafWilhelmRowCenters(IReadOnlyList<TimeCell> colTimes)
    {
        var centers = new List<double>();
        foreach (var y in colTimes
                     .Where(c => !string.IsNullOrWhiteSpace(c.Time) && IsGrafWilhelmLabel(c.RowLabel))
                     .Select(c => c.Y)
                     .OrderByDescending(y => y))
        {
            if (centers.Count == 0 || centers[^1] - y > 12)
            {
                centers.Add(y);
            }
        }

        return centers;
    }

    /// <summary>Spalte fährt die 693 (Aufderhöhe), nicht die Linie des umgebenden Bandes.</summary>
    private static bool ColumnLooksLike693(IReadOnlyList<TimeCell> colTimes) =>
        colTimes.Any(c => Is693BranchStop(c.RowLabel)) &&
        !colTimes.Any(c => Is696BranchStop(c.RowLabel) || Is698BranchStop(c.RowLabel));

    private static bool Is693BranchStop(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var l = label.ToLowerInvariant();
        return l.Contains("aufderh", StringComparison.Ordinal) ||
               l.Contains("merscheid", StringComparison.Ordinal) ||
               l.Contains("siebels", StringComparison.Ordinal) ||
               l.Contains("humboldt", StringComparison.Ordinal) ||
               l.Contains("haaner", StringComparison.Ordinal);
    }

    private static bool Is696BranchStop(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var l = label.ToLowerInvariant();
        return l.Contains("untenkat", StringComparison.Ordinal) ||
               l.Contains("stüb", StringComparison.Ordinal) ||
               l.Contains("stueb", StringComparison.Ordinal) ||
               l.Contains("stubch", StringComparison.Ordinal) ||
               l.Contains("gläß", StringComparison.Ordinal) ||
               l.Contains("gläss", StringComparison.Ordinal) ||
               l.Contains("glaess", StringComparison.Ordinal) ||
               l.Contains("eggenweg", StringComparison.Ordinal) ||
               l.Contains("dickenbusch", StringComparison.Ordinal);
    }

    private static bool Is698BranchStop(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var l = label.ToLowerInvariant();
        return l.Contains("kannenhof", StringComparison.Ordinal) ||
               l.Contains("wupper", StringComparison.Ordinal);
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
    /// Fußzeile mit einer Uhrzeit im Fließtext, z. B. „fährt um 11:42 aus der Schleife“.
    /// Echte Haltzeilen haben den Namen links und die Zeiten in den Säulen, nicht Wörter links und rechts der Uhr.
    /// </summary>
    private static bool IsProseNoteRow(IReadOnlyList<PdfWord> row)
    {
        var times = row.Where(w => IsTimeToken(w.Text)).ToList();
        if (times.Count is 0 or > 2)
        {
            return false;
        }

        foreach (var time in times)
        {
            var left = 0;
            var right = 0;
            foreach (var word in row)
            {
                if (IsTimeToken(word.Text) ||
                    IsTripToken(word.Text) ||
                    IsRoleOrMarginWord(word.Text) ||
                    Math.Abs(word.MidX - time.MidX) > 48)
                {
                    continue;
                }

                if (word.MidX < time.MidX - 4)
                {
                    left++;
                }
                else if (word.MidX > time.MidX + 4)
                {
                    right++;
                }
            }

            if (left >= 1 && right >= 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>„Einfahrt“ rechts der letzten Zeit ist die Richtung der Zeile, nicht der Haltname.</summary>
    private static bool IsRightMarginDepotAnnotation(PdfWord word, double maxTimeX) =>
        word.MidX > maxTimeX + 12 && IsRoleOrMarginWord(word.Text);

    private static bool IsRoleOrMarginWord(string text)
    {
        var t = text.Trim().Trim('.', ',', ';', ':').ToLowerInvariant();
        return t is "an" or "ab" or "an/ab" or "ab/an" or "einfahrt" or "ausfahrt";
    }

    /// <summary>
    /// Zeit auf der Zeile eines „Linie …“-Markers, die noch zum Band darüber gehört
    /// (693 endet am Graf-Wilhelm-Platz auf der Zeile „Linie 696“).
    /// </summary>
    private static bool TimeBelongsToBandAbove(IReadOnlyList<PdfWord> words, PdfWord time, double markerY)
    {
        if (!IsTimeToken(time.Text) || Math.Abs(time.Y - markerY) > 4.0)
        {
            return false;
        }

        var row = words.Where(w => Math.Abs(w.Y - time.Y) <= 2.5).ToList();
        if (IsGrafWilhelmLabel(LabelImmediatelyLeftOf(row, time)))
        {
            return true;
        }

        // Weitere Zeiten derselben Zeile (20:56 neben 19:27) ohne eigenen Namen.
        PdfWord? anchor = null;
        foreach (var other in row)
        {
            if (!IsTimeToken(other.Text) || other.MidX >= time.MidX - 6)
            {
                continue;
            }

            if (IsGrafWilhelmLabel(LabelImmediatelyLeftOf(row, other)) &&
                (anchor is null || other.MidX < anchor.MidX))
            {
                anchor = other;
            }
        }

        if (anchor is null)
        {
            return false;
        }

        foreach (var between in row)
        {
            if (between.MidX <= anchor.MidX + 3 || between.MidX >= time.MidX - 3)
            {
                continue;
            }

            if (IsTimeToken(between.Text) || IsTripToken(between.Text))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(CleanStopHint(between.Text)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Wörter zwischen der linken Nachbarzeit und dieser Zeit. Der Name der 692
    /// bleibt bei ihren Zeiten, Aufderhöhe bei der 693 daneben.
    /// </summary>
    private static string LabelImmediatelyLeftOf(IReadOnlyList<PdfWord> row, PdfWord time)
    {
        var leftTimeX = double.NegativeInfinity;
        foreach (var w in row)
        {
            if (IsTimeToken(w.Text) && w.MidX < time.MidX - 6 && w.MidX > leftTimeX)
            {
                leftTimeX = w.MidX;
            }
        }

        var parts = new List<string>();
        var partWords = new List<PdfWord>();
        foreach (var w in row.OrderBy(x => x.MidX))
        {
            if (w.MidX <= leftTimeX + 4 || w.MidX >= time.MidX - 3)
            {
                continue;
            }

            if (IsTimeToken(w.Text) || IsTripToken(w.Text))
            {
                continue;
            }

            var text = w.Text.Trim();
            if (text.Equals("Linie", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Line", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Senkrechte Randbuchstaben der Kopfzeile, nicht der Haltestellenname.
            if (w.MidX < 30 && text.Length <= 1)
            {
                continue;
            }

            parts.Add(w.Text);
            partWords.Add(w);
        }

        // Fußnote „Linie 683 v. GWP (37)“ liegt weit links vom eigentlichen Halt.
        if (partWords.Count > 1)
        {
            var cluster = new List<string>();
            double previousX = partWords[0].MidX;
            cluster.Add(parts[0]);
            for (var i = 1; i < partWords.Count; i++)
            {
                if (partWords[i].MidX - previousX > 80)
                {
                    cluster.Clear();
                }

                cluster.Add(parts[i]);
                previousX = partWords[i].MidX;
            }

            parts = cluster;
        }

        return NormalizeRoleSlashes(string.Join(" ", parts));
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

            // Fußnote mit Uhrzeit im Satz („fährt um 11:42 aus der Schleife“) ist keine Fahrtsäule.
            if (IsProseNoteRow(row))
            {
                continue;
            }

            var times = row.Where(w => IsTimeToken(w.Text)).ToList();
            var maxTimeX = times.Count > 0 ? times.Max(w => w.MidX) : double.MaxValue;
            var rawLabel = NormalizeRoleSlashes(string.Join(
                " ",
                row.Where(w =>
                        !IsTimeToken(w.Text) &&
                        !IsTripToken(w.Text) &&
                        !IsRightMarginDepotAnnotation(w, maxTimeX))
                    .Select(w => w.Text)));
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

            // Name steht links der Zeit. Liegt auf derselben Zeile ein zweiter Block
            // (696 links, 693 rechts), bekommt jede Zeit nur den Namen unmittelbar links.
            // „weiter“ / „Seite 2“ rechts der Zeiten bleiben draußen.
            string? siblingLabel = null;
            string? lastLabel = null;
            foreach (var w in times.OrderBy(t => t.MidX))
            {
                var own = LabelImmediatelyLeftOf(row, w);
                var label = own;
                var hint = CleanStopHint(own);
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    siblingLabel = own;
                }
                else if (!string.IsNullOrWhiteSpace(siblingLabel))
                {
                    label = siblingLabel!;
                }
                else if (string.IsNullOrWhiteSpace(hint))
                {
                    var below = FindStopLabelJustBelow(rows, rowIndex, w.Y);
                    if (!string.IsNullOrWhiteSpace(below))
                    {
                        label = below!;
                    }
                    else
                    {
                        var above = FindStopLabelJustAbove(rows, rowIndex, w.Y);
                        if (!string.IsNullOrWhiteSpace(above))
                        {
                            label = MergeRoleOntoStopLabel(above!, own);
                        }
                        else if (!string.IsNullOrWhiteSpace(lastStopName) &&
                                 lastWasAnAb &&
                                 (IsAbAnLabel(own) ||
                                  IsPureDepartureLabel(own) ||
                                  string.IsNullOrWhiteSpace(own) ||
                                  LooksLikeOrphanTimeRoleRow(own)))
                        {
                            label = MergeRoleOntoStopLabel(lastStopName!, own);
                        }
                    }
                }

                timeCells.Add(new TimeCell(
                    NormalizeTime(w.Text),
                    w.MidX,
                    w.Y,
                    label));
                lastLabel = label;
            }

            var resolvedName = CleanStopHint(lastLabel);
            if (!string.IsNullOrWhiteSpace(resolvedName))
            {
                lastStopName = resolvedName;
                lastWasAnAb = IsAnAbLabel(lastLabel);
            }
            else
            {
                lastStopName = null;
                lastWasAnAb = false;
            }
        }

        return timeCells;
    }

    private static string MergeRoleOntoStopLabel(string stopName, string? roleRaw)
    {
        var role = (roleRaw ?? string.Empty).Trim().ToLowerInvariant();
        if (IsAbAnLabel(roleRaw) || role is "ab" or "ab/an")
        {
            return $"{stopName} ab/an";
        }

        if (IsAnAbLabel(roleRaw) || role is "an" or "an/ab")
        {
            return $"{stopName} an/ab";
        }

        if (IsPureDepartureLabel(roleRaw))
        {
            return $"{stopName} ab";
        }

        if (IsPureArrivalLabel(roleRaw))
        {
            return $"{stopName} an";
        }

        return stopName;
    }

    /// <summary>
    /// Wie FindStopLabelJustBelow, aber Zeile darüber (GWP-Name auf der ab-Zeile, Zeit auf an darüber).
    /// </summary>
    private static string? FindStopLabelJustAbove(
        IReadOnlyList<List<PdfWord>> rows,
        int timeRowIndex,
        double timeY)
    {
        for (var i = timeRowIndex - 1; i >= 0 && i >= timeRowIndex - 3; i--)
        {
            var row = rows[i];
            if (row.Count == 0)
            {
                continue;
            }

            if (row.Any(w => IsTimeToken(w.Text)))
            {
                // Zeitzeile darüber – weiter suchen (Doppelzeit an/ab)
            }

            if (row.Count(w => IsTripToken(w.Text)) >= 3)
            {
                continue;
            }

            var y = row.Average(w => w.Y);
            var dy = y - timeY;
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

    /// <summary>Einzelziffern/Satzzeichen / OCR-Reste („o“, „e“) – kein Haltestellenname.</summary>
    private static bool IsStopNameNoise(string? stopName)
    {
        if (string.IsNullOrWhiteSpace(stopName))
        {
            return true;
        }

        var t = stopName.Trim().ToLowerInvariant();
        if (t.Length <= 2 && t.All(c => !char.IsLetter(c)))
        {
            return true;
        }

        // PdfPig-Artefakte: alleinstehendes „o“/„e“/„r“ (oft GWP-an-Zeile ohne Namen)
        if (t.Length <= 2 && t.All(char.IsLetter))
        {
            return true;
        }

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
        string sectionFallback,
        string? turnaroundHint = null)
    {
        var colTripNumbers = tripNumbersByColumn.TryGetValue(colIndex, out var tn)
            ? tn
            : [];

        // Wendepunkt mit Fahrtwechsel (rote Nr.): Ankunft und Abfahrt gehören zu zwei Fahrten
        halfTimes = FilterTurnaroundCellsForFahrtwechsel(
            halfTimes,
            colTripNumbers,
            preferUpperNumbers,
            label => IsTurnaroundAnchor(label, turnaroundHint));

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
    /// Wendepunkt-Doppelzeile mit Fahrtwechsel (rote Nr. zwischen Ankunft/Abfahrt):
    /// nur die Zeit dieser Fahrt behalten.
    /// </summary>
    private static List<TimeCell> FilterTurnaroundCellsForFahrtwechsel(
        List<TimeCell> halfTimes,
        IReadOnlyList<(string Number, double Y)> tripNumbers,
        bool preferUpperHalf,
        Func<string?, bool> isTurnaround)
    {
        var turnCells = halfTimes
            .Where(c => !string.IsNullOrWhiteSpace(c.Time) && isTurnaround(c.RowLabel))
            .OrderByDescending(c => c.Y)
            .ToList();
        if (turnCells.Count < 2 || tripNumbers.Count == 0)
        {
            return halfTimes;
        }

        var topY = turnCells[0].Y;
        var botY = turnCells[^1].Y;

        var changeMarkers = tripNumbers
            .Where(t => IsYStrictlyBetween(t.Y, topY, botY))
            .Select(t => t.Y)
            .ToList();
        if (changeMarkers.Count == 0)
        {
            return halfTimes;
        }

        var splitY = changeMarkers.Average();
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
                if (!isTurnaround(c.RowLabel) || string.IsNullOrWhiteSpace(c.Time))
                {
                    return true;
                }

                return preferUpperHalf ? c.Y >= splitY : c.Y <= splitY;
            })
            .ToList();

        var kept = filtered.Count(c =>
            !string.IsNullOrWhiteSpace(c.Time) && isTurnaround(c.RowLabel));
        return kept > 0 ? filtered : halfTimes;
    }

    /// <summary>
    /// Planzeiten in Leserichtung (oben → unten).
    /// Doppelzeile eng beieinander (GWP an/ab+ab/an) = ein Halt; zweiter Besuch derselben Hst
    /// weiter unten (Rundkurs 683) = eigener TimingPoint.
    /// OCR-Reste („o“) werden an die nächste echte Halt-Zeile gehängt – sonst zerbricht
    /// Bf. Mitte→GWP (Phantom-Anker zwischen den korrekten Zeiten).
    /// </summary>
    private static IReadOnlyList<TimingPoint> ExtractTimingPoints(
        IReadOnlyList<TimeCell> halfTimes,
        string endTime,
        string? endStopHint,
        IReadOnlyList<(string Number, double Y)> tripNumbers,
        bool preferUpperHalf)
    {
        const double doubleLineGapPt = 28.0;

        // PdfPig: größeres Y = weiter oben → absteigend = Leserichtung
        var rawCells = halfTimes
            .Where(c => !string.IsNullOrWhiteSpace(c.Time))
            .OrderByDescending(c => c.Y)
            .ToList();

        // Orphan-Zeiten (leerer/Noise-Hint) dem nächsten echten Halt in Y-Nähe zuordnen
        var cells = new List<(string Hint, TimeCell Cell)>();
        for (var idx = 0; idx < rawCells.Count; idx++)
        {
            var cell = rawCells[idx];
            var hint = CleanStopHint(cell.RowLabel);
            if (string.IsNullOrWhiteSpace(hint))
            {
                string? donor = null;
                for (var d = idx + 1; d < rawCells.Count; d++)
                {
                    if (cell.Y - rawCells[d].Y > doubleLineGapPt)
                    {
                        break;
                    }

                    var h = CleanStopHint(rawCells[d].RowLabel);
                    if (!string.IsNullOrWhiteSpace(h))
                    {
                        donor = h;
                        break;
                    }
                }

                if (donor is null)
                {
                    for (var d = idx - 1; d >= 0; d--)
                    {
                        if (rawCells[d].Y - cell.Y > doubleLineGapPt)
                        {
                            break;
                        }

                        var h = CleanStopHint(rawCells[d].RowLabel);
                        if (!string.IsNullOrWhiteSpace(h))
                        {
                            donor = h;
                            break;
                        }
                    }
                }

                if (donor is null)
                {
                    continue;
                }

                hint = donor;
            }

            cells.Add((hint!, cell));
        }

        var points = new List<TimingPoint>();
        var i = 0;
        while (i < cells.Count)
        {
            var hint = cells[i].Hint;
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
                // Doppelzeile: Abfahrt = spätere Uhrzeit (2. Zeile / ab)
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
        if (string.IsNullOrWhiteSpace(key) || IsStopNameNoise(key))
        {
            return null;
        }

        return key;
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

    /// <summary>„Hasselstraße Einfahrt“ → „hasselstraße“ (Vorlagen-Match ohne Depot-Suffix).</summary>
    private static string? StripDepotQualifier(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return hint;
        }

        var l = hint.Trim();
        l = Regex.Replace(l, @"\b(einfahrt|ausfahrt|betr\.?\s*hof|betriebshof)\b", " ",
            RegexOptions.IgnoreCase);
        l = Regex.Replace(l, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(l) ? hint : l.ToLowerInvariant();
    }

    private static string NormalizeStopKey(string? label)
    {
        var l = (label ?? string.Empty).Trim().ToLowerInvariant();
        l = AnAbFlexibleRegex.Replace(l, " ");
        l = AbAnFlexibleRegex.Replace(l, " ");
        l = Regex.Replace(l, @"\b(an|ab)\b", " ");
        // PDF: „/ Graf - Wilh. - Pl. ab/an“ und „Graf - Wilh. - Pl. an/ab“ → gleicher Halt
        // (sonst TimingPoints doppelt: :13 und :15, Anzeige nimmt oft die Ankunft)
        l = Regex.Replace(l, @"^[\s/|.:.\-–—]+", "");
        l = Regex.Replace(l, @"[\s/|.:.\-–—]+$", "");
        l = Regex.Replace(l, @"\s+", " ").Trim();
        // Fortsetzung auf der nächsten Umlaufseite: „Hästen weiter seite 2“ ist der Halt Hästen.
        l = Regex.Replace(l, @"\bweiter\s+seite\s*\d*\b", " ", RegexOptions.IgnoreCase);
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

    /// <summary>Erkannte Fahrtwechsel-Zeile (rote Nr.) inkl. Wendepunkt-Halt.</summary>
    private sealed record FahrtwechselSplit(double SplitY, string TurnaroundHint);

    /// <summary>
    /// Findet die Zeile mit roten Fahrtnummern am Wendepunkt – linienunabhängig
    /// (Hbf, Vohwinkel, Burger Bf., Schule Widdert, …). Rote Nummern trennen Hin/Rück.
    /// </summary>
    private static FahrtwechselSplit? FindFahrtwechselSplit(
        List<List<PdfWord>> rows,
        List<TimeCell> timeCells,
        string? lineCourse)
    {
        // Hbf-Ankunft/Abfahrt-Split nur auf 681/682-Doppelkarten – sonst wird z. B. 683
        // (Vohwinkel↔Burg, Hbf nur Zwischenhalt) fälschlich am Hbf geteilt.
        if (IsHbfPairLine(lineCourse))
        {
            var hbfY = FindHbfSplitY(rows, timeCells);
            if (hbfY is double hy)
            {
                return new FahrtwechselSplit(hy, "Hauptbahnhof");
            }
        }

        if (timeCells.Count < 4)
        {
            return null;
        }

        var pageMinY = timeCells.Min(c => c.Y);
        var pageMaxY = timeCells.Max(c => c.Y);
        var pageSpan = Math.Max(pageMaxY - pageMinY, 1.0);

        (double Y, string Hint, int Score)? best = null;
        foreach (var row in rows)
        {
            var redTripWords = row.Where(w => IsTripToken(w.Text) && w.IsRed).ToList();
            var tripWords = redTripWords.Count > 0
                ? redTripWords
                : row.Where(w => IsTripToken(w.Text)).ToList();
            // Rote Nr.: schon eine reicht als Trenner; sonst mind. 2 schwarze Nummern
            if (redTripWords.Count == 0 && tripWords.Count < 2)
            {
                continue;
            }

            if (tripWords.Count == 0)
            {
                continue;
            }

            var y = tripWords.Average(w => w.Y);
            if (IsNearGrafWilhelmTimes(y, timeCells))
            {
                continue;
            }

            var hint = ResolveTurnaroundHintNearY(timeCells, y);
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            var stopsAbove = timeCells.Count(c =>
                !string.IsNullOrWhiteSpace(c.Time) && c.Y > y + 12);
            var stopsBelow = timeCells.Count(c =>
                !string.IsNullOrWhiteSpace(c.Time) && c.Y < y - 12);
            // Endhaltestellen-Nummern am Blattrand meiden – Wendepunkt liegt typisch mittig
            if (stopsAbove < 2 || stopsBelow < 2)
            {
                continue;
            }

            var score = tripWords.Count * 10;
            score += Math.Min(stopsAbove, 8) + Math.Min(stopsBelow, 8);
            score += ScoreTurnaroundStraddle(timeCells, y, hint);
            var midDist = Math.Abs(y - (pageMinY + pageMaxY) / 2.0) / pageSpan;
            score += (int)Math.Round((1.0 - midDist) * 25);
            // Rote Fahrtnummern = vereinbarter Hin/Rück-Trenner – stark bevorzugen
            if (redTripWords.Count > 0)
            {
                score += 120 + redTripWords.Count * 15;
            }

            if (best is null || score > best.Value.Score)
            {
                best = (y, hint, score);
            }
        }

        return best is null ? null : new FahrtwechselSplit(best.Value.Y, best.Value.Hint);
    }

    private static string? ResolveTurnaroundHintNearY(IReadOnlyList<TimeCell> timeCells, double y)
    {
        static string Key(string? hint) =>
            (hint ?? string.Empty).Trim().ToLowerInvariant();

        var groups = timeCells
            .Where(c => !string.IsNullOrWhiteSpace(c.Time))
            .Select(c =>
            {
                var hint = CleanStopHint(c.RowLabel);
                return (Cell: c, Hint: hint, Key: Key(hint));
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Hint) &&
                        !IsStopNameNoise(x.Hint) &&
                        !IsArrowDestinationLabel(x.Cell.RowLabel))
            .GroupBy(x => x.Key)
            .Select(g =>
            {
                var sample = g.First().Hint!;
                var above = g.Count(x => x.Cell.Y > y + 1);
                var below = g.Count(x => x.Cell.Y < y - 1);
                var nearCells = g.Where(x => Math.Abs(x.Cell.Y - y) < 32).ToList();
                var near = nearCells.Count;
                var avgDist = nearCells.Count > 0
                    ? nearCells.Average(x => Math.Abs(x.Cell.Y - y))
                    : 999.0;
                var score = near * 12;
                if (above > 0 && below > 0)
                {
                    score += 80;
                }

                score += above + below;
                // Näher an der roten Nr. = echter Wendepunkt (Hasselstraße vor Erbenhäuschen)
                score += (int)Math.Round(Math.Max(0, 40 - avgDist));
                return (Hint: sample, Score: score, Near: near, AvgDist: avgDist);
            })
            .Where(x => x.Near > 0 || x.Score >= 80)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.AvgDist)
            .ToList();

        return groups.FirstOrDefault().Hint;
    }

    private static int ScoreTurnaroundStraddle(
        IReadOnlyList<TimeCell> timeCells,
        double y,
        string turnaroundHint)
    {
        var matched = timeCells
            .Where(c => !string.IsNullOrWhiteSpace(c.Time) &&
                        IsTurnaroundAnchor(c.RowLabel, turnaroundHint))
            .ToList();
        if (matched.Count == 0)
        {
            return 0;
        }

        var above = matched.Count(c => c.Y > y);
        var below = matched.Count(c => c.Y < y);
        var near = matched.Count(c => Math.Abs(c.Y - y) < 28);
        var score = near * 8;
        if (above > 0 && below > 0)
        {
            score += 60;
        }

        return score;
    }

    private static bool IsArrowDestinationLabel(string? label) =>
        !string.IsNullOrWhiteSpace(label) &&
        (label.Contains('>') ||
         label.Contains('→') ||
         label.Contains("->", StringComparison.Ordinal));

    private static bool IsTurnaroundAnchor(string? label, string? turnaroundHint)
    {
        if (string.IsNullOrWhiteSpace(label) || IsArrowDestinationLabel(label))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(turnaroundHint))
        {
            return IsHauptbahnhofSplitAnchor(label);
        }

        if (IsHauptbahnhofHint(turnaroundHint) && IsHauptbahnhofSplitAnchor(label))
        {
            return true;
        }

        var cleaned = CleanStopHint(label);
        if (string.IsNullOrWhiteSpace(cleaned) || IsStopNameNoise(cleaned))
        {
            return false;
        }

        var a = cleaned.ToLowerInvariant();
        var b = turnaroundHint.Trim().ToLowerInvariant();
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return true;
        }

        static HashSet<string> Tokens(string s) =>
            s.Split([' ', '-', '.', '/', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 3)
                .Select(t => t.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);

        // „Bahnhof“ allein verbindet Bahnhof Mitte nicht mit Vohwinkel Bahnhof.
        var generic = new HashSet<string>(StringComparer.Ordinal)
        {
            "bahnhof", "platz", "strasse", "straße", "str", "weg", "schule", "hof"
        };
        var left = Tokens(a);
        var right = Tokens(b);
        left.ExceptWith(generic);
        right.ExceptWith(generic);
        foreach (var x in left)
        {
            foreach (var y in right)
            {
                if (string.Equals(x, y, StringComparison.Ordinal) ||
                    (x.Length >= 4 && y.StartsWith(x, StringComparison.Ordinal)) ||
                    (y.Length >= 4 && x.StartsWith(y, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Obere Hälfte: Fahrt endet am Wendepunkt (rote Nr.).</summary>
    private static ParsedTrip ForceUpperHalfEndAtTurnaround(
        ParsedTrip trip,
        IReadOnlyList<TimeCell> upperTimes,
        string turnaroundHint)
    {
        var turnEnd = upperTimes
            .Where(c => !string.IsNullOrWhiteSpace(c.Time) &&
                        !IsEinfahrtOrDepotLabel(c.RowLabel) &&
                        IsTurnaroundAnchor(c.RowLabel, turnaroundHint))
            .OrderBy(c => ParseMinutes(c.Time))
            .LastOrDefault();
        if (turnEnd is null)
        {
            return ForceUpperHalfStartBeforeHbf(trip, upperTimes);
        }

        var turnTime = NormalizeTime(turnEnd.Time);
        var first = upperTimes
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.Time) &&
                !IsTurnaroundAnchor(c.RowLabel, turnaroundHint) &&
                !IsEinfahrtOrDepotLabel(c.RowLabel) &&
                !string.IsNullOrWhiteSpace(CleanStopHint(c.RowLabel)) &&
                !IsStopNameNoise(CleanStopHint(c.RowLabel)))
            .OrderBy(c => ParseMinutes(c.Time))
            .FirstOrDefault();
        if (first is null)
        {
            return new ParsedTrip
            {
                Direction = trip.Direction,
                TripNumber = trip.TripNumber,
                StartTime = trip.StartTime,
                EndTime = turnTime,
                LineCourseHint = trip.LineCourseHint,
                SectionHint = trip.SectionHint,
                StartStopHint = trip.StartStopHint,
                EndStopHint = CleanStopHint(turnEnd.RowLabel) ?? turnaroundHint,
                TimingPoints = trip.TimingPoints
            };
        }

        var startHint = CleanStopHint(first.RowLabel);
        var startTime = NormalizeTime(first.Time);
        if (string.IsNullOrWhiteSpace(startHint))
        {
            return trip;
        }

        // Abfahrt am Wendepunkt liegt vor den Folgehalten (Hbf 7:07 → Hästen 7:41).
        // Sonst wird das Ende auf die Hbf-Zeit gezogen, Start und Ende fallen zusammen
        // und die Rückfahrt verschwindet.
        if (ParseMinutes(startTime) >= ParseMinutes(turnTime))
        {
            return trip;
        }

        return new ParsedTrip
        {
            Direction = trip.Direction,
            TripNumber = trip.TripNumber,
            StartTime = startTime,
            EndTime = turnTime,
            LineCourseHint = trip.LineCourseHint,
            SectionHint = trip.SectionHint,
            StartStopHint = startHint,
            EndStopHint = CleanStopHint(turnEnd.RowLabel) ?? turnaroundHint,
            TimingPoints = trip.TimingPoints
        };
    }

    /// <summary>
    /// Wenn nur „an“ am ersten Halt steht (kein „ab“ in der Spalte), Start sonst auf Folgehalt
    /// (z. B. Mix 693: Aufderhöhe an 7:30, erste ab = Merscheid).
    /// </summary>
    private static ParsedTrip PreferStartAtEarliestPlanPoint(ParsedTrip trip)
    {
        var points = trip.TimingPoints ?? [];
        if (points.Count == 0 ||
            !RouteScheduleTimeCalculator.TryParseTime(trip.StartTime, out _))
        {
            return trip;
        }

        var earliest = points
            .Where(p => RouteScheduleTimeCalculator.TryParseTime(p.Time, out _) &&
                        !string.IsNullOrWhiteSpace(p.StopHint) &&
                        !IsStopNameNoise(p.StopHint))
            .OrderBy(p => ParseMinutes(p.Time))
            .FirstOrDefault();
        if (earliest is null ||
            ParseMinutes(earliest.Time) >= ParseMinutes(trip.StartTime))
        {
            return trip;
        }

        return new ParsedTrip
        {
            Direction = trip.Direction,
            TripNumber = trip.TripNumber,
            StartTime = NormalizeTime(earliest.Time),
            EndTime = trip.EndTime,
            LineCourseHint = trip.LineCourseHint,
            SectionHint = trip.SectionHint,
            StartStopHint = earliest.StopHint,
            EndStopHint = trip.EndStopHint,
            TimingPoints = trip.TimingPoints,
            MarkColorR = trip.MarkColorR,
            MarkColorG = trip.MarkColorG,
            MarkColorB = trip.MarkColorB
        };
    }

    /// <summary>
    /// Innerer Halt → äußerster Terminus in den Planzeiten:
    /// Jagenberg/Seilbahn → Burger Bahnhof (683); Erbenhäuschen → Hasselstraße (684).
    /// </summary>
    private static ParsedTrip PreferOuterTerminus(ParsedTrip trip)
    {
        var points = trip.TimingPoints ?? [];
        if (points.Count == 0)
        {
            return trip;
        }

        static bool IsBurgThrough(string? hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                return false;
            }

            var l = hint.Trim().ToLowerInvariant();
            return l.Contains("jagenberg", StringComparison.Ordinal) ||
                   (l.Contains("burg", StringComparison.Ordinal) &&
                    l.Contains("seil", StringComparison.Ordinal));
        }

        static bool IsBurgerBahnhof(string? hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                return false;
            }

            var l = hint.Trim().ToLowerInvariant();
            return l.Contains("burger", StringComparison.Ordinal) &&
                   l.Contains("bahnhof", StringComparison.Ordinal);
        }

        static bool IsErbenhaeuschen(string? hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                return false;
            }

            var l = hint.Trim().ToLowerInvariant();
            return l.Contains("erbenh", StringComparison.Ordinal);
        }

        static bool IsHasselstrasse(string? hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                return false;
            }

            var l = hint.Trim().ToLowerInvariant();
            return l.Contains("hassel", StringComparison.Ordinal);
        }

        var start = trip.StartStopHint;
        var end = trip.EndStopHint;
        var startTime = trip.StartTime;
        var endTime = trip.EndTime;

        static bool IsTimedOuter(UmlaufPdfReader.TimingPoint p, Func<string?, bool> isOuter) =>
            isOuter(p.StopHint) &&
            RouteScheduleTimeCalculator.TryParseTime(p.Time, out _);

        // Fahrgast-Halt bevorzugen; Einfahrt/Depot nur als Fallback (Abend 684: nur Hasselstraße Einfahrt)
        static UmlaufPdfReader.TimingPoint? PickEarlier(
            IReadOnlyList<UmlaufPdfReader.TimingPoint> pts,
            Func<string?, bool> isOuter,
            string limitTime,
            bool allowDepot)
        {
            var pool = pts.Where(p =>
                IsTimedOuter(p, isOuter) &&
                (allowDepot || !IsEinfahrtOrDepotLabel(p.StopHint)) &&
                ParseMinutes(p.Time) <= ParseMinutes(limitTime));
            return pool.OrderBy(p => ParseMinutes(p.Time)).FirstOrDefault();
        }

        static UmlaufPdfReader.TimingPoint? PickLater(
            IReadOnlyList<UmlaufPdfReader.TimingPoint> pts,
            Func<string?, bool> isOuter,
            string limitTime,
            bool allowDepot)
        {
            var pool = pts.Where(p =>
                IsTimedOuter(p, isOuter) &&
                (allowDepot || !IsEinfahrtOrDepotLabel(p.StopHint)) &&
                ParseMinutes(p.Time) >= ParseMinutes(limitTime));
            return pool.OrderByDescending(p => ParseMinutes(p.Time)).FirstOrDefault();
        }

        void PreferEarlier(Func<string?, bool> isInner, Func<string?, bool> isOuter, bool allowDepotFallback)
        {
            if (!isInner(start))
            {
                return;
            }

            var earlier = PickEarlier(points, isOuter, startTime, allowDepot: false)
                          ?? (allowDepotFallback
                              ? PickEarlier(points, isOuter, startTime, allowDepot: true)
                              : null);
            if (earlier is not null)
            {
                start = StripDepotQualifier(earlier.StopHint) ?? earlier.StopHint;
                startTime = NormalizeTime(earlier.Time);
            }
        }

        void PreferLater(Func<string?, bool> isInner, Func<string?, bool> isOuter, bool allowDepotFallback)
        {
            if (!isInner(end))
            {
                return;
            }

            var later = PickLater(points, isOuter, endTime, allowDepot: false)
                        ?? (allowDepotFallback
                            ? PickLater(points, isOuter, endTime, allowDepot: true)
                            : null);
            if (later is not null)
            {
                end = StripDepotQualifier(later.StopHint) ?? later.StopHint;
                endTime = NormalizeTime(later.Time);
            }
        }

        PreferEarlier(IsBurgThrough, IsBurgerBahnhof, allowDepotFallback: false);
        PreferLater(IsBurgThrough, IsBurgerBahnhof, allowDepotFallback: false);
        PreferEarlier(IsErbenhaeuschen, IsHasselstrasse, allowDepotFallback: true);
        PreferLater(IsErbenhaeuschen, IsHasselstrasse, allowDepotFallback: true);

        if (string.Equals(start, trip.StartStopHint, StringComparison.Ordinal) &&
            string.Equals(end, trip.EndStopHint, StringComparison.Ordinal) &&
            string.Equals(startTime, trip.StartTime, StringComparison.Ordinal) &&
            string.Equals(endTime, trip.EndTime, StringComparison.Ordinal))
        {
            return trip;
        }

        return new ParsedTrip
        {
            Direction = trip.Direction,
            TripNumber = trip.TripNumber,
            StartTime = startTime,
            EndTime = endTime,
            LineCourseHint = trip.LineCourseHint,
            SectionHint = trip.SectionHint,
            StartStopHint = start,
            EndStopHint = end,
            TimingPoints = trip.TimingPoints,
            MarkColorR = trip.MarkColorR,
            MarkColorG = trip.MarkColorG,
            MarkColorB = trip.MarkColorB
        };
    }

    /// <summary>Untere Hälfte: Fahrt startet am Wendepunkt (rote Nr.).</summary>
    private static ParsedTrip ForceLowerHalfStartAtTurnaround(
        ParsedTrip trip,
        IReadOnlyList<TimeCell> lowerTimes,
        string turnaroundHint)
    {
        var turn = lowerTimes
            .Where(c => !string.IsNullOrWhiteSpace(c.Time) &&
                        !IsEinfahrtOrDepotLabel(c.RowLabel) &&
                        IsTurnaroundAnchor(c.RowLabel, turnaroundHint))
            .OrderBy(c => ParseMinutes(c.Time))
            .FirstOrDefault();
        if (turn is null)
        {
            return ForceLowerHalfStartAtHbf(trip, lowerTimes);
        }

        var turnTime = NormalizeTime(turn.Time);
        var turnHint = CleanStopHint(turn.RowLabel) ?? turnaroundHint;

        var endCell = lowerTimes
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.Time) &&
                !IsTurnaroundAnchor(c.RowLabel, turnaroundHint) &&
                !IsEinfahrtOrDepotLabel(c.RowLabel) &&
                ParseMinutes(c.Time) >= ParseMinutes(turnTime) &&
                !string.IsNullOrWhiteSpace(CleanStopHint(c.RowLabel)) &&
                !IsStopNameNoise(CleanStopHint(c.RowLabel)))
            .OrderBy(c => ParseMinutes(c.Time))
            .LastOrDefault();

        var endTime = endCell is not null ? NormalizeTime(endCell.Time) : trip.EndTime;
        var endHint = endCell is not null
            ? CleanStopHint(endCell.RowLabel) ?? trip.EndStopHint
            : trip.EndStopHint;

        if (ParseMinutes(endTime) < ParseMinutes(turnTime))
        {
            endTime = trip.EndTime;
            endHint = trip.EndStopHint;
        }

        return new ParsedTrip
        {
            Direction = trip.Direction,
            TripNumber = trip.TripNumber,
            StartTime = turnTime,
            EndTime = endTime,
            LineCourseHint = trip.LineCourseHint,
            SectionHint = trip.SectionHint,
            StartStopHint = turnHint,
            EndStopHint = endHint,
            TimingPoints = trip.TimingPoints
        };
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

    private static string? ExtractLinePrefix(string? lineCourse)
    {
        if (string.IsNullOrWhiteSpace(lineCourse))
        {
            return null;
        }

        var normalized = RouteDisplayHelper.NormalizeLineCourse(lineCourse);
        var parts = normalized.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1 ? parts[0] : null;
    }

    /// <summary>681/682-Doppelkarte (Hästen/Brockenberg); andere Linien (z. B. 683) nicht.</summary>
    private static bool IsHbfPairLine(string? lineCourse)
    {
        var prefix = ExtractLinePrefix(lineCourse);
        return prefix is "681" or "682";
    }

    private static bool SameLinePrefix(string? a, string? b)
    {
        var pa = ExtractLinePrefix(a);
        var pb = ExtractLinePrefix(b);
        return !string.IsNullOrWhiteSpace(pa) &&
               string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Kopf aus PDF-Text, ergänzt/überschrieben durch Dateiname („Mo 683-01.pdf“ → 683/01).
    /// </summary>
    private static string? ResolveLineCourseFromHeaderAndFileName(string? headerLine, string? pdfPath)
    {
        var fromFile = ParseLineCourseFromFileName(pdfPath);
        if (string.IsNullOrWhiteSpace(fromFile))
        {
            return string.IsNullOrWhiteSpace(headerLine) ? null : NormalizeLineCourse(headerLine);
        }

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return fromFile;
        }

        var headerNorm = NormalizeLineCourse(headerLine);
        // Gleicher Linienstamm: vollständigen Kurs aus Dateiname bevorzugen, falls Header nur Linie hat
        if (SameLinePrefix(headerNorm, fromFile))
        {
            return !string.IsNullOrWhiteSpace(ExtractCourseSuffix(fromFile))
                ? fromFile
                : headerNorm;
        }

        // Konflikt (z. B. Textartefakt 681 vs. Datei 683): Dateiname gewinnt
        return fromFile;
    }

    internal static string? ParseLineCourseFromFileName(string? pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(pdfPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // „Sa NE21-01“ ist die Nachtlinie. „696-14_mit_NE23-03“ bleibt 696/14.
        var neName = Regex.Match(
            name,
            @"NE\s*(?<line>\d{2,4})\s*[-_/]\s*(?<course>\d{1,3})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var dashName = FileNameLineCourseRegex.Match(name);
        if (neName.Success && (!dashName.Success || neName.Index <= dashName.Index))
        {
            return NormalizeLineCourse($"NE{neName.Groups["line"].Value}/{neName.Groups["course"].Value}");
        }

        var slash = LineCourseOnlyRegex.Match(name);
        if (slash.Success)
        {
            return NormalizeLineCourse(slash.Groups["line"].Value);
        }

        var dash = FileNameLineCourseRegex.Match(name);
        if (dash.Success)
        {
            return NormalizeLineCourse($"{dash.Groups["line"].Value}/{dash.Groups["course"].Value}");
        }

        return null;
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

    /// <summary>
    /// Spaltenfahrplan (L.685, L.691, …): in einer Spalte mehrere Fahrten von oben nach unten,
    /// danach die nächste Spalte wieder von oben. Kopf „Betr.Hof“ wiederholt sich auf jeder Seite.
    /// </summary>
    private static bool TryExtractLineColumnTrips(
        IReadOnlyList<PdfWord> words,
        string? defaultLineCourse,
        out List<ParsedTrip> trips)
    {
        trips = [];
        var markers = new List<(string Line, double X, double Y)>();
        foreach (var word in words)
        {
            if (TryParseLineColumnMarker(word.Text, out var line))
            {
                markers.Add((line, word.MidX, word.Y));
            }
        }

        foreach (var word in words)
        {
            if (!word.Text.Equals("L.", StringComparison.OrdinalIgnoreCase) &&
                !word.Text.Equals("L", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var digits in words)
            {
                if (digits.MidX <= word.MidX ||
                    digits.MidX - word.MidX > 28 ||
                    Math.Abs(digits.Y - word.Y) > 4 ||
                    digits.Text.Length != 3 ||
                    !digits.Text.All(char.IsDigit))
                {
                    continue;
                }

                markers.Add((digits.Text, word.MidX, word.Y));
                break;
            }
        }

        if (markers.Count < 3)
        {
            return false;
        }

        int Bucket(double y) => (int)Math.Round(y / RowTolerance);
        var skipBuckets = new HashSet<int>();
        foreach (var row in words.GroupBy(w => Bucket(w.Y)))
        {
            var text = string.Join(' ', row.Select(w => w.Text));
            if (text.Contains("Betr.Hof", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Betriebshof", StringComparison.OrdinalIgnoreCase))
            {
                skipBuckets.Add(row.Key);
            }
        }

        var timeWords = words
            .Where(w => IsTimeToken(w.Text) && w.MidX > 36 && !skipBuckets.Contains(Bucket(w.Y)))
            .ToList();
        var columns = ClusterColumns(timeWords.Select(w => w.MidX));
        if (columns.Count == 0)
        {
            return true;
        }

        var blocks = new List<List<int>> { new() { 0 } };
        for (var i = 1; i < columns.Count; i++)
        {
            if (columns[i] - columns[i - 1] > 70)
            {
                blocks.Add([i]);
            }
            else
            {
                blocks[^1].Add(i);
            }
        }

        int ColumnOf(double x)
        {
            var best = -1;
            var bestDist = 20.0;
            for (var i = 0; i < columns.Count; i++)
            {
                var d = Math.Abs(columns[i] - x);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
        }

        int BlockOf(int col)
        {
            for (var b = 0; b < blocks.Count; b++)
            {
                if (blocks[b].Contains(col))
                {
                    return b;
                }
            }

            return 0;
        }

        double BlockLeft(int block) =>
            block <= 0 ? 36 : columns[blocks[block - 1][^1]] + 12;

        var cells = new List<(string Time, double X, double Y, int Col, string Label)>();
        foreach (var time in timeWords)
        {
            var col = ColumnOf(time.MidX);
            if (col < 0)
            {
                continue;
            }

            var left = BlockLeft(BlockOf(col));
            var earlier = timeWords
                .Where(w => w.MidX < time.MidX - 4 &&
                            Math.Abs(w.Y - time.Y) <= 6 &&
                            ColumnOf(w.MidX) is var other && other >= 0 && BlockOf(other) == BlockOf(col))
                .Select(w => w.MidX)
                .DefaultIfEmpty(left)
                .Max();
            var labelWords = words
                .Where(w => Math.Abs(w.Y - time.Y) <= 6 &&
                            w.MidX > earlier + 3 &&
                            w.MidX < time.MidX - 2 &&
                            w.MidX >= left &&
                            !IsTimeToken(w.Text) &&
                            !IsTripToken(w.Text) &&
                            !TryParseLineColumnMarker(w.Text, out _) &&
                            w.Text.Length > 1)
                .OrderBy(w => w.MidX)
                .ToList();
            var cluster = new List<PdfWord>();
            foreach (var word in labelWords)
            {
                if (cluster.Count > 0 && word.MidX - cluster[^1].MidX > 80)
                {
                    cluster.Clear();
                }

                cluster.Add(word);
            }

            cells.Add((
                NormalizeTime(time.Text),
                time.MidX,
                time.Y,
                col,
                string.Join(' ', cluster.Select(w => w.Text))));
        }

        foreach (var group in cells.GroupBy(c => (BlockOf(c.Col), Bucket: Bucket(c.Y))).ToList())
        {
            var donor = group
                .Where(c => !string.IsNullOrWhiteSpace(CleanStopHint(c.Label)))
                .OrderBy(c => c.X)
                .Select(c => c.Label)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(donor))
            {
                continue;
            }

            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (BlockOf(cell.Col) == group.Key.Item1 &&
                    Bucket(cell.Y) == group.Key.Bucket &&
                    string.IsNullOrWhiteSpace(CleanStopHint(cell.Label)))
                {
                    cells[i] = (cell.Time, cell.X, cell.Y, cell.Col, donor);
                }
            }
        }

        var numbers = new List<(string Number, string Line, double X, double Y, int Col)>();
        foreach (var word in words)
        {
            if (!IsTripToken(word.Text) || LooksLikeClockDigits(word.Text) || skipBuckets.Contains(Bucket(word.Y)))
            {
                continue;
            }

            var col = ColumnOf(word.MidX);
            if (col < 0)
            {
                continue;
            }

            (string Line, double X, double Y)? marker = null;
            foreach (var candidate in markers)
            {
                if (candidate.X >= word.MidX ||
                    word.MidX - candidate.X > 130 ||
                    Math.Abs(candidate.Y - word.Y) > 12)
                {
                    continue;
                }

                if (marker is null || candidate.X > marker.Value.X)
                {
                    marker = candidate;
                }
            }

            if (marker is null)
            {
                continue;
            }

            numbers.Add((NormalizeTripNumber(word.Text), marker.Value.Line, word.MidX, word.Y, col));
        }

        if (numbers.Count == 0)
        {
            return true;
        }

        var courseSuffix = ExtractCourseSuffix(defaultLineCourse);
        foreach (var col in cells.Select(c => c.Col).Distinct().OrderBy(c => c))
        {
            var colNumbers = numbers.Where(n => n.Col == col).OrderByDescending(n => n.Y).ToList();
            var colCells = cells.Where(c => c.Col == col).ToList();
            var groups = new Dictionary<int, List<(string Time, double X, double Y, int Col, string Label)>>();
            foreach (var cell in colCells)
            {
                var owner = -1;
                var ownerY = double.MaxValue;
                for (var i = 0; i < colNumbers.Count; i++)
                {
                    if (colNumbers[i].Y + 3 < cell.Y)
                    {
                        continue;
                    }

                    if (colNumbers[i].Y < ownerY)
                    {
                        ownerY = colNumbers[i].Y;
                        owner = i;
                    }
                }

                if (owner < 0)
                {
                    continue;
                }

                if (!groups.TryGetValue(owner, out var list))
                {
                    list = [];
                    groups[owner] = list;
                }

                list.Add(cell);
            }

            // Abfahrt steht manchmal knapp über der Fahrtnummer (785 unter 7:45).
            // Die Abfahrt und die Halte bis zur Nummer gehören zur neuen Fahrt.
            for (var i = 0; i < colNumbers.Count; i++)
            {
                var numberY = colNumbers[i].Y;
                var departureY = double.MinValue;
                foreach (var cell in colCells)
                {
                    var gap = cell.Y - numberY;
                    if (gap <= 0 || gap > 32 || !IsPureDepartureLabel(cell.Label))
                    {
                        continue;
                    }

                    var closerAbove = false;
                    for (var j = 0; j < colNumbers.Count; j++)
                    {
                        if (j == i)
                        {
                            continue;
                        }

                        var other = colNumbers[j].Y - cell.Y;
                        if (other >= -3 && other < gap)
                        {
                            closerAbove = true;
                            break;
                        }
                    }

                    if (!closerAbove && cell.Y > departureY)
                    {
                        departureY = cell.Y;
                    }
                }

                if (departureY <= numberY)
                {
                    continue;
                }

                foreach (var cell in colCells.Where(c => c.Y <= departureY && c.Y > numberY).ToList())
                {
                    foreach (var list in groups.Values)
                    {
                        list.RemoveAll(c => c.Col == cell.Col && Math.Abs(c.Y - cell.Y) < 0.5 && c.Time == cell.Time);
                    }

                    if (!groups.TryGetValue(i, out var target))
                    {
                        target = [];
                        groups[i] = target;
                    }

                    target.Add(cell);
                }
            }

            foreach (var pair in groups)
            {
                var ordered = pair.Value.OrderByDescending(c => c.Y).ToList();
                var points = new List<TimingPoint>();
                foreach (var cell in ordered)
                {
                    var hint = CleanStopHint(cell.Label);
                    if (string.IsNullOrWhiteSpace(hint))
                    {
                        continue;
                    }

                    var time = NormalizeTime(cell.Time);
                    if (points.Count > 0 &&
                        string.Equals(points[^1].StopHint, hint, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(points[^1].Time, time, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    points.Add(new TimingPoint(hint, time));
                }

                if (points.Count < 2 ||
                    string.Equals(points[0].Time, points[^1].Time, StringComparison.Ordinal))
                {
                    continue;
                }

                var number = colNumbers[pair.Key];
                var startGwp = IsGrafWilhelmLabel(points[0].StopHint) ||
                               points[0].StopHint.Contains("wilh", StringComparison.OrdinalIgnoreCase);
                var endGwp = IsGrafWilhelmLabel(points[^1].StopHint) ||
                             points[^1].StopHint.Contains("wilh", StringComparison.OrdinalIgnoreCase);
                trips.Add(new ParsedTrip
                {
                    Direction = endGwp && !startGwp
                        ? UmlaufImportPlanner.Direction.Rueck
                        : UmlaufImportPlanner.Direction.Hin,
                    TripNumber = number.Number,
                    StartTime = points[0].Time,
                    EndTime = points[^1].Time,
                    LineCourseHint = BuildLineCourseWithSuffix(number.Line, courseSuffix),
                    StartStopHint = points[0].StopHint,
                    EndStopHint = points[^1].StopHint,
                    TimingPoints = points
                });
            }
        }

        trips = trips
            .OrderBy(t => ParseMinutes(t.StartTime))
            .ThenBy(t => t.TripNumber, StringComparer.Ordinal)
            .ToList();
        return true;
    }

    private static bool TryParseLineColumnMarker(string text, out string line)
    {
        line = string.Empty;
        var value = text.Trim();
        if (value.Length != 5 ||
            (value[0] != 'L' && value[0] != 'l') ||
            value[1] != '.' ||
            !char.IsDigit(value[2]) ||
            !char.IsDigit(value[3]) ||
            !char.IsDigit(value[4]))
        {
            return false;
        }

        line = value[2..];
        return true;
    }

    /// <summary>
    /// Nachtlinien NE21–NE25: mehrere Strecken nebeneinander, in jeder Spalte
    /// liegen Hin und Rück untereinander. Die Fahrtnummer zwischen den Zeiten trennt.
    /// </summary>
    private static bool TryExtractNeNightTrips(
        IReadOnlyList<PdfWord> words,
        string? defaultLineCourse,
        out List<ParsedTrip> trips)
    {
        trips = [];
        var line = defaultLineCourse;
        if (!IsNeLine(line))
        {
            var token = words.FirstOrDefault(w =>
                Regex.IsMatch(w.Text, @"^NE\d{2,4}/\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            if (token is null)
            {
                return false;
            }

            line = NormalizeLineCourse(token.Text);
        }

        if (!IsNeLine(line))
        {
            return false;
        }

        int Bucket(double y) => (int)Math.Round(y / RowTolerance);
        var skipBuckets = new HashSet<int>();
        foreach (var row in words.GroupBy(w => Bucket(w.Y)))
        {
            var text = string.Join(' ', row.Select(w => w.Text));
            if (text.Contains("Betr.Hof", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Betriebshof", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("beachten", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Taxibus", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("umgestiegen", StringComparison.OrdinalIgnoreCase))
            {
                skipBuckets.Add(row.Key);
            }
        }

        var timeWords = words
            .Where(w => IsTimeToken(w.Text) && w.MidX > 36 && !skipBuckets.Contains(Bucket(w.Y)))
            .ToList();
        var columns = ClusterColumns(timeWords.Select(w => w.MidX));
        if (columns.Count == 0)
        {
            return true;
        }

        var blocks = new List<List<int>> { new() { 0 } };
        for (var i = 1; i < columns.Count; i++)
        {
            if (columns[i] - columns[i - 1] > 60)
            {
                blocks.Add([i]);
            }
            else
            {
                blocks[^1].Add(i);
            }
        }

        int ColumnOf(double x)
        {
            var best = -1;
            var bestDist = 18.0;
            for (var i = 0; i < columns.Count; i++)
            {
                var d = Math.Abs(columns[i] - x);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
        }

        int BlockOf(int col)
        {
            for (var b = 0; b < blocks.Count; b++)
            {
                if (blocks[b].Contains(col))
                {
                    return b;
                }
            }

            return 0;
        }

        double BlockLeft(int block)
        {
            if (block <= 0)
            {
                return 36;
            }

            var prev = blocks[block - 1];
            return columns[prev[^1]] + 12;
        }

        var cells = new List<(string Time, double X, double Y, int Col, int Block, string Label)>();
        foreach (var time in timeWords)
        {
            var col = ColumnOf(time.MidX);
            if (col < 0)
            {
                continue;
            }

            var block = BlockOf(col);
            var left = BlockLeft(block);
            var earlier = timeWords
                .Where(w => w.MidX < time.MidX - 4 &&
                            Math.Abs(w.Y - time.Y) <= 6 &&
                            ColumnOf(w.MidX) >= 0 &&
                            BlockOf(ColumnOf(w.MidX)) == block)
                .Select(w => w.MidX)
                .DefaultIfEmpty(left)
                .Max();
            var labelWords = words
                .Where(w => Math.Abs(w.Y - time.Y) <= 6 &&
                            w.MidX > earlier + 3 &&
                            w.MidX < time.MidX - 2 &&
                            w.MidX >= left &&
                            !IsTimeToken(w.Text) &&
                            !IsTripToken(w.Text) &&
                            w.Text.Length > 1)
                .OrderBy(w => w.MidX)
                .Select(w => w.Text);
            cells.Add((NormalizeTime(time.Text), time.MidX, time.Y, col, block, string.Join(' ', labelWords)));
        }

        foreach (var group in cells.GroupBy(c => (c.Block, Bucket: Bucket(c.Y))).ToList())
        {
            var donor = group
                .Where(c => !string.IsNullOrWhiteSpace(CleanStopHint(c.Label)))
                .OrderBy(c => c.X)
                .Select(c => c.Label)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(donor))
            {
                continue;
            }

            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.Block == group.Key.Block &&
                    Bucket(c.Y) == group.Key.Bucket &&
                    string.IsNullOrWhiteSpace(CleanStopHint(c.Label)))
                {
                    cells[i] = (c.Time, c.X, c.Y, c.Col, c.Block, donor);
                }
            }
        }

        var numbers = new List<(string Number, double X, double Y, int Col)>();
        foreach (var word in words)
        {
            if (!IsTripToken(word.Text) || LooksLikeClockDigits(word.Text) || skipBuckets.Contains(Bucket(word.Y)))
            {
                continue;
            }

            var col = ColumnOf(word.MidX);
            if (col < 0 || cells.Any(c => c.Col == col && Math.Abs(c.Y - word.Y) < 3))
            {
                continue;
            }

            numbers.Add((NormalizeTripNumber(word.Text), word.MidX, word.Y, col));
        }

        foreach (var col in cells.Select(c => c.Col).Distinct().OrderBy(c => c))
        {
            var colCells = cells.Where(c => c.Col == col).OrderByDescending(c => c.Y).ToList();
            var cuts = numbers
                .Where(n => n.Col == col)
                .Select(n => n.Y)
                .OrderByDescending(y => y)
                .Aggregate(new List<double>(), (list, y) =>
                {
                    if (list.Count == 0 || list[^1] - y > 4)
                    {
                        list.Add(y);
                    }

                    return list;
                });

            var bands = new List<List<(string Time, double X, double Y, int Col, int Block, string Label)>>();
            var current = new List<(string Time, double X, double Y, int Col, int Block, string Label)>();
            var cutIndex = 0;
            foreach (var cell in colCells)
            {
                while (cutIndex < cuts.Count && cell.Y < cuts[cutIndex] - 2)
                {
                    if (current.Count > 0)
                    {
                        bands.Add(current);
                        current = [];
                    }

                    cutIndex++;
                }

                current.Add(cell);
            }

            if (current.Count > 0)
            {
                bands.Add(current);
            }

            foreach (var band in bands)
            {
                var ordered = band
                    .OrderBy(c => NightCardMinutes(c.Time))
                    .ThenByDescending(c => c.Y)
                    .ToList();
                var points = new List<TimingPoint>();
                foreach (var cell in ordered)
                {
                    var hint = CleanStopHint(cell.Label);
                    if (string.IsNullOrWhiteSpace(hint))
                    {
                        continue;
                    }

                    var time = NormalizeTime(cell.Time);
                    if (points.Count > 0 &&
                        string.Equals(points[^1].StopHint, hint, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(points[^1].Time, time, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    points.Add(new TimingPoint(hint, time));
                }

                if (points.Count < 2 ||
                    string.Equals(points[0].Time, points[^1].Time, StringComparison.Ordinal))
                {
                    continue;
                }

                var startCell = ordered.First(c =>
                    string.Equals(CleanStopHint(c.Label), points[0].StopHint, StringComparison.OrdinalIgnoreCase));
                var number = numbers
                    .Where(n => n.Col == col && Math.Abs(n.Y - startCell.Y) <= 24)
                    .OrderBy(n => Math.Abs(n.Y - startCell.Y))
                    .ThenByDescending(n => n.X)
                    .Select(n => n.Number)
                    .FirstOrDefault() ?? "?";
                var startGwp = IsNeGrafWilhelm(points[0].StopHint);
                var endGwp = IsNeGrafWilhelm(points[^1].StopHint);
                trips.Add(new ParsedTrip
                {
                    Direction = endGwp && !startGwp
                        ? UmlaufImportPlanner.Direction.Rueck
                        : UmlaufImportPlanner.Direction.Hin,
                    TripNumber = number,
                    StartTime = points[0].Time,
                    EndTime = points[^1].Time,
                    LineCourseHint = NormalizeLineCourse(line!),
                    StartStopHint = points[0].StopHint,
                    EndStopHint = points[^1].StopHint,
                    TimingPoints = points
                });
            }
        }

        trips = trips
            .OrderBy(t => NightCardMinutes(t.StartTime))
            .ThenBy(t => t.TripNumber, StringComparer.Ordinal)
            .ToList();
        return true;
    }

    private static bool IsNeLine(string? lineCourse)
    {
        var prefix = ExtractLinePrefix(lineCourse);
        return prefix != null && prefix.StartsWith("NE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNeGrafWilhelm(string? label)
    {
        if (IsGrafWilhelmLabel(label))
        {
            return true;
        }

        var l = label ?? string.Empty;
        return l.Contains("wilh", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>23:30 bleibt vor 0:00, solange die Karte nur den Nachtdienst enthält.</summary>
    private static int NightCardMinutes(string time)
    {
        var m = ParseMinutes(time);
        return m < 12 * 60 ? m + 24 * 60 : m;
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
