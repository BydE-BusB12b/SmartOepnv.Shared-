using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Textextraktion aus PDF-Dateien (Excel-Ersatzfahrpläne) über PdfPig.</summary>
internal static class SimplePdfTextExtractor
{
    private static readonly Regex TimeTokenRegex = new(@"\b\d{1,2}[.:]\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex TimesOnlyRowRegex = new(
        @"^(ab|an)\s+(?:\d{1,2}[.:]\d{2}\s*)+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FullTableRowRegex = new(
        @"^(?<stops>.+?)\s+(?<dir>ab|an)\s+(?<times>(?:\d{1,2}[.:]\d{2}\s*)+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> ExtractLines(string filePath)
    {
        return MergeSplitErsatzfahrplanRows(ExtractRawLines(filePath));
    }

    public static IReadOnlyList<string> ExtractRawLines(string filePath)
    {
        var rawLines = new List<string>();
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            rawLines.AddRange(ExtractPageLines(page));
        }

        return rawLines;
    }

    internal static bool TryMergeTableLine(IReadOnlyList<string> lines, int index, out string mergedLine, out int consumed)
    {
        mergedLine = string.Empty;
        consumed = 0;
        if (index >= lines.Count)
        {
            return false;
        }

        var line = NormalizeWhitespace(lines[index]);
        if (string.IsNullOrWhiteSpace(line) || IsSkippableLine(line))
        {
            return false;
        }

        if (IsFullTableRow(line))
        {
            mergedLine = line;
            consumed = 1;
            return true;
        }

        if (IsTimesOnlyRow(line) && index + 1 < lines.Count)
        {
            var next = NormalizeWhitespace(lines[index + 1]);
            if (IsStopOnlyRow(next))
            {
                mergedLine = $"{next} {line}";
                consumed = 2;
                return true;
            }
        }

        if (IsStopOnlyRow(line) && index + 1 < lines.Count)
        {
            var next = NormalizeWhitespace(lines[index + 1]);
            if (IsTimesOnlyRow(next))
            {
                mergedLine = $"{line} {next}";
                consumed = 2;
                return true;
            }
        }

        return false;
    }

    internal static string NormalizeWhitespace(string line) =>
        Regex.Replace(line.Trim(), @"\s+", " ", RegexOptions.CultureInvariant);

    private static IEnumerable<string> ExtractPageLines(UglyToad.PdfPig.Content.Page page)
    {
        const double rowTolerance = 3.0;
        return page.GetWords()
            .GroupBy(word => (int)Math.Round(word.BoundingBox.Bottom / rowTolerance))
            .OrderByDescending(group => group.Key)
            .Select(group => string.Join(
                " ",
                group.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));
    }

    private static List<string> MergeSplitErsatzfahrplanRows(IReadOnlyList<string> lines)
    {
        var merged = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = NormalizeWhitespace(lines[i]);
            if (string.IsNullOrWhiteSpace(line) || IsSkippableLine(line))
            {
                continue;
            }

            if (IsFullTableRow(line))
            {
                merged.Add(line);
                continue;
            }

            if (IsTimesOnlyRow(line) && i + 1 < lines.Count)
            {
                var next = NormalizeWhitespace(lines[i + 1]);
                if (IsStopOnlyRow(next))
                {
                    merged.Add($"{next} {line}");
                    i++;
                    continue;
                }
            }

            if (IsStopOnlyRow(line) && i + 1 < lines.Count)
            {
                var next = NormalizeWhitespace(lines[i + 1]);
                if (IsTimesOnlyRow(next))
                {
                    merged.Add($"{line} {next}");
                    i++;
                    continue;
                }
            }

            if (TimesOnlyRowRegex.IsMatch(line) || FullTableRowRegex.IsMatch(line))
            {
                merged.Add(line);
            }
        }

        return merged;
    }

    private static bool IsSkippableLine(string line)
    {
        var normalized = line.ToLowerInvariant();
        if (normalized.Contains("ersatzfahrplan", StringComparison.Ordinal) &&
            !TimeTokenRegex.IsMatch(normalized))
        {
            return true;
        }

        if (normalized.Contains("sev haltestelle", StringComparison.Ordinal) &&
            !TimeTokenRegex.IsMatch(normalized))
        {
            return true;
        }

        return normalized is "ab" or "an" or "sev" or "haltestelle"
               || Regex.IsMatch(line, @"^(SEV\s*)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsFullTableRow(string line) =>
        FullTableRowRegex.IsMatch(line) &&
        FullTableRowRegex.Match(line).Groups["stops"].Value.Length >= 3;

    private static bool IsTimesOnlyRow(string line) => TimesOnlyRowRegex.IsMatch(line);

    private static bool IsStopOnlyRow(string line)
    {
        if (IsTimesOnlyRow(line) || IsFullTableRow(line))
        {
            return false;
        }

        if (Regex.IsMatch(line, @"^(ab|an)\s+\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (!line.Any(char.IsLetter))
        {
            return false;
        }

        return line.Length >= 4;
    }
}
