using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.Dienstvorlagen;

public sealed class DutyTemplateImportRow
{
    public int SourceLineNumber { get; init; }

    public string RawLine { get; init; } = string.Empty;

    public string LineCourse { get; init; } = string.Empty;

    public string Destination { get; init; } = string.Empty;

    public string FromTime { get; init; } = string.Empty;

    public string FromStop { get; init; } = string.Empty;

    public string ToTime { get; init; } = string.Empty;

    public string ToStop { get; init; } = string.Empty;

    /// <summary>z. B. „Kurs 06:53 (Richtung 1)“ bei Ersatzfahrplan-PDF.</summary>
    public string ImportGroup { get; init; } = string.Empty;

    public string TripNumber { get; init; } = string.Empty;

    public string Remark { get; init; } = string.Empty;

    public string Preview
    {
        get
        {
            var trip = string.IsNullOrWhiteSpace(TripNumber) ? string.Empty : $"{TripNumber} · ";
            var core =
                $"{LineCourse} · {Destination} · ab {FromTime} {FromStop} · an {ToTime} {ToStop}".Trim(' ', '·');
            var grouped = string.IsNullOrWhiteSpace(ImportGroup) ? core : $"[{ImportGroup}] {core}";
            return $"{trip}{grouped}".Trim(' ', '·');
        }
    }

    public DutyTemplateRow ToTemplateRow() => new()
    {
        TripNumber = TripNumber,
        LineCourse = LineCourse,
        Remark = Remark,
        Destination = Destination,
        FromTime = FromTime,
        FromStop = DutyTemplateStopNameHelper.StripHaltestelleMarker(FromStop),
        ToTime = ToTime,
        ToStop = DutyTemplateStopNameHelper.StripHaltestelleMarker(ToStop)
    };
}

public static class DutyTemplateFahrplanParser
{
    private static readonly string[] LineHeaders =
        ["linie/kurs", "linie", "kurs", "line", "linie kurs"];

    private static readonly string[] DestinationHeaders =
        ["ziel", "destination", "endziel", "richtung"];

    private static readonly string[] FromTimeHeaders =
        ["ab zeit", "ab", "von zeit", "abfahrt", "startzeit", "start"];

    private static readonly string[] FromStopHeaders =
        ["ab haltestelle", "ab hst", "von", "abfahrtsort", "start hst", "von haltestelle"];

    private static readonly string[] ToTimeHeaders =
        ["an zeit", "an", "bis zeit", "ankunft", "endzeit", "ende"];

    private static readonly string[] ToStopHeaders =
        ["an haltestelle", "an hst", "bis", "ankunftsort", "ziel hst", "bis haltestelle"];

    public static IReadOnlyList<DutyTemplateImportRow> ParseFile(string filePath) =>
        ParseFileWithHints(filePath).Rows;

    public static DutyTemplateImportResult ParseFileWithHints(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension is ".xlsx" or ".xlsm")
        {
            return DutyTemplateExcelParser.ParseWithHints(filePath);
        }

        if (extension == ".pdf" || filePath.Contains(".xlsx.pdf", StringComparison.OrdinalIgnoreCase))
        {
            return DutyTemplateErsatzfahrplanParser.ParsePdfWithHints(filePath);
        }

        var lines = File.ReadAllLines(filePath);
        return new DutyTemplateImportResult
        {
            Rows = ParseLines(lines)
        };
    }

    public static IReadOnlyList<DutyTemplateImportRow> ParseLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        var delimiter = DetectDelimiter(lines);
        var nonEmpty = lines
            .Select((line, index) => (Line: line, Index: index + 1))
            .Where(x => !string.IsNullOrWhiteSpace(x.Line))
            .ToList();

        if (nonEmpty.Count == 0)
        {
            return [];
        }

        var headerIndex = FindHeaderIndex(nonEmpty.Select(x => x.Line).ToList(), delimiter);
        Dictionary<string, int>? columnMap = null;
        var startAt = 0;

        if (headerIndex >= 0)
        {
            columnMap = BuildColumnMap(SplitLine(nonEmpty[headerIndex].Line, delimiter));
            startAt = headerIndex + 1;
        }

        var result = new List<DutyTemplateImportRow>();
        for (var i = startAt; i < nonEmpty.Count; i++)
        {
            var (line, lineNo) = nonEmpty[i];
            if (LooksLikeComment(line))
            {
                continue;
            }

            var cells = SplitLine(line, delimiter);
            if (cells.Count == 0)
            {
                continue;
            }

            var importRow = columnMap is not null
                ? ParseMappedRow(lineNo, line, cells, columnMap)
                : ParsePositionalRow(lineNo, line, cells);

            if (HasMeaningfulContent(importRow))
            {
                result.Add(importRow);
            }
        }

        return result;
    }

    private static bool LooksLikeComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith('#') || trimmed.StartsWith("//") || trimmed.StartsWith(';');
    }

    private static char DetectDelimiter(IReadOnlyList<string> lines)
    {
        var sample = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        var semicolon = sample.Count(c => c == ';');
        var tab = sample.Count(c => c == '\t');
        var comma = sample.Count(c => c == ',');
        if (tab >= semicolon && tab >= comma && tab > 0)
        {
            return '\t';
        }

        if (semicolon >= comma && semicolon > 0)
        {
            return ';';
        }

        if (comma > 0)
        {
            return ',';
        }

        return ';';
    }

    private static int FindHeaderIndex(IReadOnlyList<string> lines, char delimiter)
    {
        for (var i = 0; i < Math.Min(lines.Count, 5); i++)
        {
            var cells = SplitLine(lines[i], delimiter)
                .Select(NormalizeHeader)
                .ToList();
            if (cells.Any(c => LineHeaders.Contains(c) || DestinationHeaders.Contains(c) ||
                               FromTimeHeaders.Contains(c) || ToTimeHeaders.Contains(c)))
            {
                return i;
            }
        }

        return -1;
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Count; i++)
        {
            map[NormalizeHeader(headers[i])] = i;
        }

        return map;
    }

    private static string NormalizeHeader(string header) =>
        header.Trim().ToLowerInvariant().Replace("  ", " ");

    private static DutyTemplateImportRow ParseMappedRow(
        int lineNo,
        string raw,
        IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> map)
    {
        string Read(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (map.TryGetValue(key, out var index) && index < cells.Count)
                {
                    return cells[index].Trim();
                }
            }

            return string.Empty;
        }

        var lineCourse = CombineLineCourse(
            Read("linie/kurs", "linie kurs", "linie", "kurs", "line"),
            Read("kurs"));

        return new DutyTemplateImportRow
        {
            SourceLineNumber = lineNo,
            RawLine = raw,
            LineCourse = lineCourse,
            Destination = Read("ziel", "destination", "endziel", "richtung"),
            FromTime = Read("ab zeit", "ab", "von zeit", "abfahrt", "startzeit", "start"),
            FromStop = Read("ab haltestelle", "ab hst", "von", "abfahrtsort", "start hst", "von haltestelle"),
            ToTime = Read("an zeit", "an", "bis zeit", "ankunft", "endzeit", "ende"),
            ToStop = Read("an haltestelle", "an hst", "bis", "ankunftsort", "ziel hst", "bis haltestelle")
        };
    }

    private static DutyTemplateImportRow ParsePositionalRow(int lineNo, string raw, IReadOnlyList<string> cells)
    {
        if (cells.Count >= 6)
        {
            return new DutyTemplateImportRow
            {
                SourceLineNumber = lineNo,
                RawLine = raw,
                LineCourse = cells[0].Trim(),
                Destination = cells[1].Trim(),
                FromTime = cells[2].Trim(),
                FromStop = cells[3].Trim(),
                ToTime = cells[4].Trim(),
                ToStop = cells[5].Trim()
            };
        }

        return ParseFreeTextRow(lineNo, raw, cells);
    }

    private static DutyTemplateImportRow ParseFreeTextRow(int lineNo, string raw, IReadOnlyList<string> cells)
    {
        var joined = string.Join(" ", cells);
        var times = Regex.Matches(joined, @"\b(\d{1,2})[.:](\d{2})\b")
            .Select(m => $"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)}:{m.Groups[2].Value}")
            .Distinct()
            .Take(2)
            .ToList();

        return new DutyTemplateImportRow
        {
            SourceLineNumber = lineNo,
            RawLine = raw,
            LineCourse = cells.Count > 0 ? cells[0].Trim() : string.Empty,
            Destination = cells.Count > 1 ? cells[1].Trim() : string.Empty,
            FromTime = times.Count > 0 ? times[0] : string.Empty,
            FromStop = cells.Count > 2 ? cells[2].Trim() : string.Empty,
            ToTime = times.Count > 1 ? times[1] : string.Empty,
            ToStop = cells.Count > 3 ? cells[^1].Trim() : string.Empty
        };
    }

    private static string CombineLineCourse(string primary, string course)
    {
        if (string.IsNullOrWhiteSpace(course) || primary.Contains('/', StringComparison.Ordinal))
        {
            return primary.Trim();
        }

        if (string.IsNullOrWhiteSpace(primary))
        {
            return course.Trim();
        }

        return $"{primary.Trim()}/{course.Trim()}";
    }

    private static bool HasMeaningfulContent(DutyTemplateImportRow row) =>
        !string.IsNullOrWhiteSpace(row.LineCourse) ||
        !string.IsNullOrWhiteSpace(row.Destination) ||
        !string.IsNullOrWhiteSpace(row.FromTime) ||
        !string.IsNullOrWhiteSpace(row.ToTime) ||
        !string.IsNullOrWhiteSpace(row.FromStop) ||
        !string.IsNullOrWhiteSpace(row.ToStop);

    private static List<string> SplitLine(string line, char delimiter) =>
        line.Split(delimiter)
            .Select(part => part.Trim().Trim('"'))
            .Where(part => part.Length > 0)
            .ToList();
}
