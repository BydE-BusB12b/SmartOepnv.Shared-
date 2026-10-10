using System.Globalization;
using System.Text.RegularExpressions;
using SmartOepnv.Core.Dienstvorlagen;
using UglyToad.PdfPig;

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
    }

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

        var trips = ExtractTripsFromColumns(words, lineCourse);
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
        string? defaultLineCourse)
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
        var upperLine = defaultLineCourse;
        var lowerLine = BuildSisterLineCourse(defaultLineCourse, courseSuffix);

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
                // Kein Hbf-Split erkennbar → ganze Spalte als eine Fahrt
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
                trips.Add(upperTrip);
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
                trips.Add(lowerTrip);
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
    /// </summary>
    private static List<TimeCell> BuildTimeCellsWithInheritedStopNames(List<List<PdfWord>> rows)
    {
        var timeCells = new List<TimeCell>();
        string? lastStopName = null;
        var lastWasAnAb = false;

        foreach (var row in rows)
        {
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
            if (string.IsNullOrWhiteSpace(stopName) &&
                !string.IsNullOrWhiteSpace(lastStopName) &&
                lastWasAnAb &&
                (IsAbAnLabel(rawLabel) ||
                 string.IsNullOrWhiteSpace(rawLabel) ||
                 LooksLikeOrphanTimeRoleRow(rawLabel)))
            {
                // Nur direkte Folgezeile ab/an zur vorherigen an/ab
                label = $"{lastStopName} ab/an";
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
        }
        else if (terminusAb.Count > 0)
        {
            startTime = terminusAb.OrderBy(c => ParseMinutes(c.Time)).First().Time;
            endTime = chronoAn.Count > 0
                ? chronoAn[^1].Time
                : chronoAb.Count > 0 ? chronoAb[^1].Time : startTime;
        }
        else if (terminusAn.Count > 0)
        {
            endTime = terminusAn.OrderBy(c => ParseMinutes(c.Time)).Last().Time;
            startTime = chronoAb.Count > 0 ? chronoAb[0].Time : endTime;
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
                endTime = chronoAn.Count > 0 ? chronoAn[^1].Time : chronoAb[^1].Time;
            }
            else if (hbfAn.Count > 0 || hbfAb.Count > 0)
            {
                // Ende am Hbf: Ankunft bevorzugen, sonst ab/an-Zeit
                endTime = hbfAn.Count > 0 ? hbfAn[^1].Time : hbfAb[^1].Time;
            }
            else
            {
                endTime = chronoAn.Count > 0 ? chronoAn[^1].Time : chronoAb[^1].Time;
            }

            if (string.Equals(startTime, endTime, StringComparison.Ordinal) && chronoAb.Count > 1)
            {
                endTime = chronoAn.Count > 0 ? chronoAn[^1].Time : chronoAb[^1].Time;
            }
        }

        if (ParseMinutes(endTime) < ParseMinutes(startTime))
        {
            (startTime, endTime) = (endTime, startTime);
        }

        var tripNo = ResolveTripNumberForHalf(
            tripNumbersByColumn,
            colIndex,
            halfTimes,
            preferUpperNumbers) ?? $"?{colIndex + 1}{(preferUpperNumbers ? "a" : "b")}";

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

        var endStopHint = CleanStopHint(
            endArrivalCells.LastOrDefault(c =>
                    string.Equals(c.Time, endTime, StringComparison.Ordinal))
                ?.RowLabel
            ?? chronoAn.LastOrDefault()?.RowLabel
            ?? chronoAb.LastOrDefault()?.RowLabel);

        // Endhalt: bei Doppelzeile Ankunft = an/ab (erste); Hbf+Fahrtwechsel: Zeit oberhalb der Nr.
        endTime = PreferArrivalTimeForStop(
            halfTimes, endStopHint, endTime, colTripNumbers, preferUpperNumbers);

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
    /// Nicht anfassen, wenn die Hälfte Brockenberg-ab hat (Rückfahrt Brockenberg→Hbf, z. B. 5:38).
    /// </summary>
    private static ParsedTrip ForceLowerHalfStartAtHbf(ParsedTrip trip, IReadOnlyList<TimeCell> lowerTimes)
    {
        var hasBrockenbergAb = lowerTimes.Any(c =>
            !string.IsNullOrWhiteSpace(c.Time) &&
            IsTerminusAbLabel(c.RowLabel));
        if (hasBrockenbergAb)
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
    /// Planzeiten je Haltestelle.
    /// GWP: bei an/ab + ab/an zählt immer die Abfahrt (2. / spätere Zeit).
    /// Hbf mit Fahrtwechsel (Nr. dazwischen): Zeit dieser Hälfte.
    /// </summary>
    private static IReadOnlyList<TimingPoint> ExtractTimingPoints(
        IReadOnlyList<TimeCell> halfTimes,
        string endTime,
        string? endStopHint,
        IReadOnlyList<(string Number, double Y)> tripNumbers,
        bool preferUpperHalf)
    {
        var byHint = new Dictionary<string, TimeCell>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in halfTimes
                     .Where(c => !string.IsNullOrWhiteSpace(c.Time))
                     .Select(c => (Hint: CleanStopHint(c.RowLabel), Cell: c))
                     .Where(x => !string.IsNullOrWhiteSpace(x.Hint))
                     .GroupBy(x => x.Hint!, StringComparer.OrdinalIgnoreCase))
        {
            var cells = group.Select(x => x.Cell).ToList();
            var hint = group.Key;

            if (cells.Count == 1)
            {
                byHint[hint] = cells[0];
                continue;
            }

            if (IsHauptbahnhofHint(hint) &&
                HasTripNumberBetween(cells.Max(c => c.Y), cells.Min(c => c.Y), tripNumbers))
            {
                byHint[hint] = PreferHbfCellForHalf(
                    cells.OrderByDescending(c => c.Y).First(),
                    cells.OrderBy(c => c.Y).First(),
                    preferUpperHalf);
                continue;
            }

            if (IsHauptbahnhofHint(hint))
            {
                // Hbf ohne Fahrtnr.: Ankunft = frühere Zeit
                byHint[hint] = cells.OrderBy(c => ParseMinutes(c.Time)).First();
                continue;
            }

            // GWP u. a.: ab/an bevorzugen, sonst immer die spätere Zeit (2. Zeile)
            var abAn = cells.Where(c => IsAbAnLabel(c.RowLabel)).ToList();
            byHint[hint] = abAn.Count > 0
                ? abAn.OrderByDescending(c => ParseMinutes(c.Time)).First()
                : cells.OrderByDescending(c => ParseMinutes(c.Time)).First();
        }

        // Endhalt: Ankunft (an/ab) falls vorhanden und noch nicht als Abfahrt gesetzt
        if (!string.IsNullOrWhiteSpace(endTime) &&
            !string.IsNullOrWhiteSpace(endStopHint) &&
            RouteScheduleTimeCalculator.TryParseTime(endTime, out _))
        {
            var endNorm = NormalizeTime(endTime);
            if (!byHint.ContainsKey(endStopHint))
            {
                byHint[endStopHint] = new TimeCell(endNorm, 0, 0, endStopHint);
            }
            else if (IsHauptbahnhofHint(endStopHint))
            {
                // Endhalt Hbf: explizite Endzeit (Ankunft) setzen
                byHint[endStopHint] = new TimeCell(
                    endNorm,
                    byHint[endStopHint].MidX,
                    byHint[endStopHint].Y,
                    byHint[endStopHint].RowLabel);
            }
        }

        return byHint
            .Select(kv => new TimingPoint(kv.Key, NormalizeTime(kv.Value.Time)))
            .OrderBy(p => ParseMinutes(p.Time))
            .ToList();
    }

    /// <summary>
    /// Bei Doppelzeile derselben Hst (nicht Hbf-Fahrtwechsel): ab/an bzw. spätere Zeit (2.).
    /// GWP: an/ab 5:13 + ab/an 5:15 → 5:15.
    /// </summary>
    private static TimeCell PreferDepartureCell(TimeCell a, TimeCell b)
    {
        var aAbAn = IsAbAnLabel(a.RowLabel);
        var bAbAn = IsAbAnLabel(b.RowLabel);
        if (aAbAn != bAbAn)
        {
            return bAbAn ? b : a;
        }

        var aAnAb = IsAnAbLabel(a.RowLabel);
        var bAnAb = IsAnAbLabel(b.RowLabel);
        if (aAnAb != bAnAb)
        {
            // an/ab = Ankunft → die andere (Abfahrt) bevorzugen
            return aAnAb ? b : a;
        }

        if (IsPureArrivalLabel(a.RowLabel) != IsPureArrivalLabel(b.RowLabel))
        {
            return IsPureArrivalLabel(a.RowLabel) ? b : a;
        }

        // Gleiche Art / unklare Labels → zweite / spätere Uhrzeit (GWP-Doppelzeile)
        return ParseMinutes(b.Time) >= ParseMinutes(a.Time) ? b : a;
    }

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
                // Doppel-GWP: nur ab/an (Abfahrt). Gibt es mehrere → spätere Zeit.
                // Am Hbf hat FilterHbfCellsForFahrtwechsel zuvor getrennt (rote Fahrtnr.).
                var abFirst = combined.Where(c => IsAbAnLabel(c.RowLabel)).ToList();
                if (abFirst.Count > 0)
                {
                    result.Add(abFirst.OrderByDescending(c => ParseMinutes(c.Time)).First());
                }
                else
                {
                    // nur an/ab: bei mehreren Zeiten die spätere (2.) als Betriebsabfahrt
                    result.Add(combined.OrderByDescending(c => ParseMinutes(c.Time)).First());
                }
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
        }

        return result;
    }

    private static string NormalizeStopKey(string? label)
    {
        var l = (label ?? string.Empty).Trim().ToLowerInvariant();
        l = AnAbFlexibleRegex.Replace(l, " ");
        l = AbAnFlexibleRegex.Replace(l, " ");
        l = Regex.Replace(l, @"\b(an|ab)\b", " ");
        l = Regex.Replace(l, @"\s+", " ").Trim();
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
        bool preferUpperNumbers)
    {
        if (!tripNumbersByColumn.TryGetValue(colIndex, out var list) || list.Count == 0)
        {
            return null;
        }

        var halfMinY = halfTimes.Min(c => c.Y) - 8;
        var halfMaxY = halfTimes.Max(c => c.Y) + 8;
        var inHalf = list.Where(x => x.Y >= halfMinY && x.Y <= halfMaxY).ToList();
        var pool = inHalf.Count > 0 ? inHalf : list;

        if (preferUpperNumbers)
        {
            // Obere Hälfte: 646/642/438 bevorzugen
            var preferred = pool
                .Where(x => x.Number is "646" or "642" or "438" or "430" or "432")
                .OrderByDescending(x => x.Y)
                .FirstOrDefault();
            if (preferred.Number is { Length: > 0 })
            {
                return preferred.Number;
            }
        }
        else
        {
            // Untere Hälfte: 648/444 bevorzugen
            var preferred = pool
                .Where(x => x.Number is "648" or "444" or "401" or "404" or "400" or "405")
                .OrderBy(x => x.Y)
                .FirstOrDefault();
            if (preferred.Number is { Length: > 0 })
            {
                return preferred.Number;
            }
        }

        return (preferUpperNumbers
                ? pool.OrderByDescending(x => x.Y)
                : pool.OrderBy(x => x.Y))
            .First()
            .Number;
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
            return string.IsNullOrWhiteSpace(courseSuffix) ? "682" : $"682/{courseSuffix}";
        }

        if (normalized.StartsWith("682", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(courseSuffix) ? "681" : $"681/{courseSuffix}";
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
        lowerLabel.Contains("widdert", StringComparison.Ordinal);

    private static string GuessSectionLabel(IReadOnlyList<TimeCell> colTimes)
    {
        foreach (var c in colTimes)
        {
            var l = c.RowLabel;
            if (l.Contains("Brockenberg", StringComparison.OrdinalIgnoreCase))
            {
                return "Brockenberg";
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
