using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>
/// Parser für typische SEV-/Kampagne-Ersatzfahrpläne (Excel → PDF):
/// Zeilen = Haltestellen, Spalten = Kurse mit ab/an-Zeiten.
/// </summary>
public static class DutyTemplateErsatzfahrplanParser
{
    private static readonly Regex TimeTokenRegex = new(@"\b(\d{1,2})[.:](\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex LineTokenRegex = new(@"\b(?:SEV\s+)?([SR]\s*\d+[A-Z]?|RE\s*\d+[A-Z]?|RB\s*\d+[A-Z]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RouteTokenRegex = new(@"\b([A-ZÄÖÜ]{2,})\s*[-–]\s*([A-ZÄÖÜ]{2,})\b", RegexOptions.Compiled);
    private static readonly Regex FahrtnrRegex = new(@"\b(\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex BusPrefixRegex = new(@"^(\d{1,2})\s+(?=[A-ZÄÖÜW])", RegexOptions.Compiled);
    private static readonly Regex ValidityRegex = new(@"Gültig\s+vom\s+(.+?)(?:\s+Uhr)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<DutyTemplateImportRow> ParsePdf(string filePath) =>
        ParsePdfWithHints(filePath).Rows;

    public static DutyTemplateImportResult ParsePdfWithHints(string filePath)
    {
        var blocks = ExtractDirectionBlocks(filePath);
        return BuildImportResultFromBlocks(blocks, filePath, null);
    }

    internal static DutyTemplateImportResult BuildImportResultFromBlocks(
        IReadOnlyList<DirectionBlock> blocks,
        string filePath,
        string? validityOverride)
    {
        if (blocks.Count == 0)
        {
            return new DutyTemplateImportResult();
        }

        if (!IsExcelPath(filePath))
        {
            foreach (var block in blocks)
            {
                AlignBlockRowTimes(block);
            }
        }

        var allRows = blocks.SelectMany(block => block.Rows).ToList();
        var metadata = ExtractMetadata(filePath, allRows, blocks);
        var validity = validityOverride
            ?? (IsPdfPath(filePath)
                ? ExtractValidity(SimplePdfTextExtractor.ExtractRawLines(filePath))
                : string.Empty);
        var vehicle = blocks.Select(block => block.VehicleNumber)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var result = new List<DutyTemplateImportRow>();
        var directionNo = 1;

        foreach (var block in blocks)
        {
            result.AddRange(BuildImportRowsForDirection(block, metadata, vehicle, directionNo));
            directionNo++;
        }

        result.Sort((a, b) => CompareImportRowsByDepartureTime(result, a, b));

        return new DutyTemplateImportResult
        {
            Rows = result,
            Hints = new DutyTemplateImportHints
            {
                Line = metadata.Line,
                Route = metadata.Route,
                VehicleNumber = vehicle,
                LineCourse = BuildLineCourse(metadata.Line, vehicle),
                Validity = validity
            }
        };
    }

    private static ErsatzfahrplanMetadata ExtractMetadata(
        string filePath,
        IReadOnlyList<ErsatzfahrplanTableRow> rows,
        IReadOnlyList<DirectionBlock> blocks)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        var headerText = string.Join(" ", rows.Take(12).Select(row => row.RawLine));
        var line = MatchLine(fileName) ?? MatchLine(headerText) ?? string.Empty;
        var route = MatchRoute(fileName) ?? MatchRoute(headerText) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(route))
        {
            route = blocks.Select(block => NormalizeRouteLabel(block.RouteLabel))
                .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label)) ?? string.Empty;
        }

        var destination = route.Contains('-', StringComparison.Ordinal)
            ? route.Split('-', 2)[^1].Trim()
            : string.Empty;
        var validity = IsPdfPath(filePath)
            ? ExtractValidity(SimplePdfTextExtractor.ExtractLines(filePath))
            : string.Empty;

        return new ErsatzfahrplanMetadata(line, route, destination, string.Empty, validity);
    }

    private static bool IsPdfPath(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
               || filePath.Contains(".xlsx.pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcelPath(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase);
    }

    internal static int CompareImportRowsByDepartureTime(
        IReadOnlyList<DutyTemplateImportRow> rows,
        DutyTemplateImportRow a,
        DutyTemplateImportRow b)
    {
        var fromTimes = rows
            .Select(row => DutyTemplateCalculator.ParseMinutes(row.FromTime))
            .Where(minutes => minutes.HasValue)
            .Select(minutes => minutes!.Value)
            .ToList();
        var spansMidnight = fromTimes.Any(minutes => minutes >= 18 * 60)
                            && fromTimes.Any(minutes => minutes < DutyTemplateCalculator.OperatingDayStartMinutes);
        var aKey = DutyTemplateCalculator.ToOperatingDaySortKey(
            DutyTemplateCalculator.ParseMinutes(a.FromTime) ?? int.MaxValue, spansMidnight);
        var bKey = DutyTemplateCalculator.ToOperatingDaySortKey(
            DutyTemplateCalculator.ParseMinutes(b.FromTime) ?? int.MaxValue, spansMidnight);
        var compare = aKey.CompareTo(bKey);
        return compare != 0
            ? compare
            : CompareTripNumberSortKey(a.TripNumber).CompareTo(CompareTripNumberSortKey(b.TripNumber));
    }

    internal static int CompareTripNumberSortKey(string? tripNumber)
    {
        if (string.IsNullOrWhiteSpace(tripNumber))
        {
            return int.MaxValue;
        }

        return int.TryParse(tripNumber.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;
    }

    private static string NormalizeRouteLabel(string routeLabel)
    {
        if (string.IsNullOrWhiteSpace(routeLabel))
        {
            return string.Empty;
        }

        var normalized = routeLabel.Replace("->", "→", StringComparison.OrdinalIgnoreCase);
        var parts = normalized.Split('→', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2
            ? $"{parts[0]}-{parts[^1]}"
            : routeLabel.Trim();
    }

    private static string ExtractValidity(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var match = ValidityRegex.Match(line.Trim());
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            if (line.Contains("Gültig vom", StringComparison.OrdinalIgnoreCase))
            {
                return line.Replace("Gültig vom", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            }
        }

        return string.Empty;
    }

    private static string? MatchLine(string text)
    {
        var match = LineTokenRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return NormalizeLine(match.Groups[1].Value);
    }

    private static string? MatchRoute(string text)
    {
        var match = RouteTokenRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return $"{match.Groups[1].Value}-{match.Groups[2].Value}";
    }

    private static string NormalizeLine(string value) =>
        Regex.Replace(value.Trim(), @"\s+", string.Empty, RegexOptions.CultureInvariant)
            .Replace("SEV", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static List<DirectionBlock> ExtractDirectionBlocks(string filePath)
    {
        var rawLines = SimplePdfTextExtractor.ExtractRawLines(filePath);
        var blocks = new List<DirectionBlock>();
        DirectionBlock? current = null;
        string? detectedVehicle = null;

        for (var i = 0; i < rawLines.Count; i++)
        {
            var line = SimplePdfTextExtractor.NormalizeWhitespace(rawLines[i]);
            if (string.IsNullOrWhiteSpace(line) || line.Length <= 2 && !line.Any(char.IsDigit))
            {
                continue;
            }

            if (IsRouteHeaderLine(line))
            {
                if (current is { Rows.Count: > 0 })
                {
                    blocks.Add(current);
                }

                current = new DirectionBlock { RouteLabel = line.Trim() };
                continue;
            }

            if (IsFahrtnrHeaderLine(line, out var fahrtNumbers))
            {
                if (current is { Rows.Count: > 0 })
                {
                    blocks.Add(current);
                    current = new DirectionBlock { VehicleNumber = current.VehicleNumber };
                }

                current ??= new DirectionBlock();
                current.FahrtNumbers = fahrtNumbers;
                current.CourseColumns = Enumerable.Range(0, fahrtNumbers.Count).ToList();
                continue;
            }

            if (!SimplePdfTextExtractor.TryMergeTableLine(rawLines, i, out var tableLine, out var consumed))
            {
                continue;
            }

            i += consumed - 1;
            var cells = SplitDelimitedCells(tableLine);
            var parsed = TryParseTableRow(tableLine, cells);
            if (parsed is null)
            {
                continue;
            }

            current ??= new DirectionBlock();
            parsed = StripBusPrefix(parsed, out var vehicle);
            if (!string.IsNullOrWhiteSpace(vehicle))
            {
                detectedVehicle ??= vehicle;
            }

            if (current.FahrtNumbers.Count == 0)
            {
                continue;
            }

            current.Rows.Add(parsed with { SourceLineNumber = i + 1 });
        }

        if (current is { Rows.Count: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(detectedVehicle))
            {
                current.VehicleNumber = detectedVehicle;
            }

            blocks.Add(current);
        }

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.VehicleNumber) && !string.IsNullOrWhiteSpace(detectedVehicle))
            {
                block.VehicleNumber = detectedVehicle;
            }
        }

        return blocks;
    }

    private static bool IsRouteHeaderLine(string line)
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

    private static bool IsFahrtnrHeaderLine(string line, out List<string> fahrtNumbers)
    {
        fahrtNumbers = [];
        var markerIndex = line.IndexOf("Ersatzhaltestelle", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        fahrtNumbers = FahrtnrRegex.Matches(line[markerIndex..])
            .Select(match => match.Groups[1].Value)
            .ToList();
        return fahrtNumbers.Count > 0;
    }

    private static ErsatzfahrplanTableRow StripBusPrefix(ErsatzfahrplanTableRow row, out string? vehicle)
    {
        vehicle = null;
        var shortName = row.ShortStop;
        var longName = row.LongStop;

        var shortMatch = BusPrefixRegex.Match(shortName);
        if (shortMatch.Success)
        {
            vehicle = shortMatch.Groups[1].Value;
            shortName = shortName[shortMatch.Length..].Trim();
        }

        var longMatch = BusPrefixRegex.Match(longName);
        if (longMatch.Success)
        {
            vehicle ??= longMatch.Groups[1].Value;
            longName = longName[longMatch.Length..].Trim();
        }

        return row with
        {
            ShortStop = DutyTemplateStopNameHelper.StripHaltestelleMarker(shortName),
            LongStop = DutyTemplateStopNameHelper.ResolveImportStopName(shortName, longName)
        };
    }

    private static List<string> SplitDelimitedCells(string line)
    {
        if (line.Contains('|'))
        {
            return line.Split('|')
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();
        }

        return [line.Trim()];
    }

    private static ErsatzfahrplanTableRow? TryParseTableRow(string rawLine, IReadOnlyList<string> cells)
    {
        if (IsHeaderRow(rawLine, cells))
        {
            return null;
        }

        if (cells.Count >= 4 &&
            IsDirectionToken(cells[2]))
        {
            var times = ParseTimes(string.Join(" ", cells.Skip(3)));
            if (times.Count == 0)
            {
                return null;
            }

            var shortStop = cells[0].Trim();
            var longStop = cells[1].Trim();
            if (IsGarbageStopRow(shortStop, longStop))
            {
                return null;
            }

            return new ErsatzfahrplanTableRow(
                0,
                rawLine,
                shortStop,
                DutyTemplateStopNameHelper.ResolveImportStopName(shortStop, longStop),
                cells[2].Trim().ToLowerInvariant(),
                times);
        }

        var match = Regex.Match(
            rawLine,
            @"^(?<stops>.+?)\s+(?<dir>ab|an)\s+(?<times>(?:\d{1,2}[.:]\d{2}\s*)+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var timesFromRegex = ParseTimes(match.Groups["times"].Value);
        if (timesFromRegex.Count == 0)
        {
            return null;
        }

        var stops = match.Groups["stops"].Value.Trim();
        var (shortName, longName) = DutyTemplateStopNameHelper.SplitBahnhofAndHaltestelle(stops);

        if (IsGarbageStopRow(shortName, longName) || IsBahnhofOnlyRow(shortName, longName))
        {
            return null;
        }

        return new ErsatzfahrplanTableRow(
            0,
            rawLine,
            shortName,
            DutyTemplateStopNameHelper.ResolveImportStopName(shortName, longName),
            match.Groups["dir"].Value.ToLowerInvariant(),
            timesFromRegex);
    }

    private static bool IsHeaderRow(string rawLine, IReadOnlyList<string> cells)
    {
        var normalized = rawLine.ToLowerInvariant();
        if (normalized.Contains("sev haltestelle", StringComparison.Ordinal) &&
            !TimeTokenRegex.IsMatch(normalized))
        {
            return true;
        }

        if (cells.Count <= 2 && normalized.Contains("sev", StringComparison.Ordinal) &&
            !TimeTokenRegex.IsMatch(normalized))
        {
            return true;
        }

        if (normalized.Contains("ersatzhaltestelle", StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.Contains("ersatzfahrplan", StringComparison.Ordinal) &&
               !TimeTokenRegex.IsMatch(normalized);
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

        var tokens = combined.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return true;
        }

        var sevCount = tokens.Count(token => token.Equals("SEV", StringComparison.OrdinalIgnoreCase));
        if (sevCount == tokens.Length)
        {
            return true;
        }

        if (sevCount > 0 && sevCount >= tokens.Length / 2)
        {
            return true;
        }

        return combined.Contains("Haltestelle", StringComparison.OrdinalIgnoreCase) &&
               !TimeTokenRegex.IsMatch(combined);
    }

    internal static bool IsBahnhofOnlyRow(string shortStop, string longStop)
    {
        var combined = $"{shortStop} {longStop}".Trim();
        if (combined.Length == 0)
        {
            return true;
        }

        return !combined.Contains("(H)", StringComparison.OrdinalIgnoreCase) &&
               !combined.Contains("Bussteig", StringComparison.OrdinalIgnoreCase) &&
               !combined.Contains("Bstg", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseTimes(string text) =>
        TimeTokenRegex.Matches(text)
            .Select(m => $"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture):00}:{m.Groups[2].Value}")
            .ToList();

    private static void AlignBlockRowTimes(DirectionBlock block)
    {
        var columnCount = block.CourseColumns.Count > 0
            ? block.CourseColumns.Count
            : block.FahrtNumbers.Count;
        if (columnCount <= 0)
        {
            return;
        }

        for (var i = 0; i < block.Rows.Count; i++)
        {
            var row = block.Rows[i];
            if (row.Times.Count == columnCount)
            {
                continue;
            }

            block.Rows[i] = row with { Times = ParseAlignedTimes(row.RawLine, columnCount) };
        }
    }

    private static List<string> ParseAlignedTimes(string rawLine, int columnCount)
    {
        var match = Regex.Match(
            rawLine,
            @"\s+(?<dir>ab|an)\s+(?<times>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return PadTimes(ParseTimes(rawLine), columnCount);
        }

        var timesText = match.Groups["times"].Value.Trim();
        var chunks = Regex.Split(timesText, @"\s{2,}")
            .Select(chunk => chunk.Trim())
            .Where(chunk => chunk.Length > 0)
            .ToList();
        if (chunks.Count == columnCount)
        {
            return chunks
                .Select(chunk =>
                {
                    var parsed = ParseTimes(chunk);
                    return parsed.Count > 0 ? parsed[0] : string.Empty;
                })
                .ToList();
        }

        var tokens = ParseTimes(timesText);
        return PadTimes(tokens, columnCount);
    }

    private static List<string> PadTimes(IReadOnlyList<string> times, int columnCount)
    {
        var result = times.Take(columnCount).ToList();
        while (result.Count < columnCount)
        {
            result.Add(string.Empty);
        }

        return result;
    }

    private static string ExtractDestinationFromRoute(string routeLabel)
    {
        var normalized = routeLabel.Replace("->", "→", StringComparison.OrdinalIgnoreCase);
        var parts = normalized.Split('→', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[^1] : routeLabel.Trim();
    }

    private sealed record CourseStop(string Stop, string Time, string Direction, int RowIndex);

    private static (CourseStop? Start, CourseStop End) ResolveTripEndpoints(IReadOnlyList<CourseStop> courseStops)
    {
        if (courseStops.Count == 0)
        {
            return (null, default!);
        }

        var ordered = courseStops.OrderBy(stop => stop.RowIndex).ToList();
        return (ordered[0], ordered[^1]);
    }

    private static IEnumerable<DutyTemplateImportRow> BuildImportRowsForDirection(
        DirectionBlock block,
        ErsatzfahrplanMetadata metadata,
        string vehicleNumber,
        int directionNo)
    {
        if (block.Rows.Count == 0)
        {
            yield break;
        }

        var vehicle = string.IsNullOrWhiteSpace(block.VehicleNumber) ? vehicleNumber : block.VehicleNumber;
        var lineCourse = BuildLineCourse(metadata.Line, vehicle);

        var destination = string.IsNullOrWhiteSpace(block.RouteLabel)
            ? metadata.Destination
            : ExtractDestinationFromRoute(block.RouteLabel);
        var columnCount = block.CourseColumns.Count > 0
            ? block.CourseColumns.Count
            : block.FahrtNumbers.Count > 0
                ? block.FahrtNumbers.Count
                : block.Rows.Max(r => r.Times.Count);

        for (var courseIndex = 0; courseIndex < columnCount; courseIndex++)
        {
            var tripNumber = courseIndex < block.FahrtNumbers.Count
                ? block.FahrtNumbers[courseIndex]
                : string.Empty;
            if (string.IsNullOrWhiteSpace(tripNumber))
            {
                continue;
            }

            var courseStops = new List<CourseStop>();
            for (var rowIndex = 0; rowIndex < block.Rows.Count; rowIndex++)
            {
                var row = block.Rows[rowIndex];
                if (row.Times.Count <= courseIndex)
                {
                    continue;
                }

                var time = row.Times[courseIndex];
                if (string.IsNullOrWhiteSpace(time))
                {
                    continue;
                }

                var stop = DutyTemplateStopNameHelper.ResolveImportStopName(row.ShortStop, row.LongStop);
                courseStops.Add(new CourseStop(stop, time, row.Direction, rowIndex));
            }

            if (courseStops.Count < 1)
            {
                continue;
            }

            var (first, last) = ResolveTripEndpoints(courseStops);
            if (first is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(first.Time) || string.IsNullOrWhiteSpace(last.Time))
            {
                continue;
            }

            var courseStart = first.Time;
            var courseLabel = $"Fahrt {tripNumber} (Richtung {directionNo})";
            yield return new DutyTemplateImportRow
            {
                SourceLineNumber = block.Rows[first.RowIndex].SourceLineNumber,
                RawLine = $"{first.Stop} → {last.Stop}",
                ImportGroup = courseLabel,
                TripNumber = tripNumber,
                Remark = string.Empty,
                LineCourse = lineCourse,
                Destination = destination,
                FromTime = first.Time,
                FromStop = first.Stop,
                ToTime = last.Time,
                ToStop = last.Stop
            };
        }
    }

    internal static string BuildLineCourse(string line, string vehicleNumber)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.IsNullOrWhiteSpace(vehicleNumber)
                ? string.Empty
                : $"Kurs {FormatCourseNumber(vehicleNumber)}";
        }

        if (string.IsNullOrWhiteSpace(vehicleNumber))
        {
            return line.Trim();
        }

        return $"{line.Trim()}/{FormatCourseNumber(vehicleNumber)}";
    }

    private static string FormatCourseNumber(string vehicleNumber)
    {
        var trimmed = vehicleNumber.Trim();
        return int.TryParse(trimmed, out var number)
            ? number.ToString("00", CultureInfo.InvariantCulture)
            : trimmed;
    }

    internal sealed class DirectionBlock
    {
        public string RouteLabel { get; set; } = string.Empty;

        public List<string> FahrtNumbers { get; set; } = [];

        public List<int> CourseColumns { get; set; } = [];

        public string VehicleNumber { get; set; } = string.Empty;

        public List<ErsatzfahrplanTableRow> Rows { get; } = [];
    }

    internal sealed record ErsatzfahrplanTableRow(
        int SourceLineNumber,
        string RawLine,
        string ShortStop,
        string LongStop,
        string Direction,
        IReadOnlyList<string> Times);

    private sealed record ErsatzfahrplanMetadata(
        string Line,
        string Route,
        string Destination,
        string VehicleNumber,
        string Validity);
}
