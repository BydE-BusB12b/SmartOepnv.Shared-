using System.Text.RegularExpressions;

namespace SmartOepnv.Core.Dienstvorlagen;

public static class DutyTemplateRemarkHelper
{
    private static readonly Regex DefinitionRegex = new(
        @"^B(\d+)\s*=\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CodeOnlyRegex = new(
        @"^B(\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string GetDisplayCode(string? remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            return string.Empty;
        }

        var trimmed = remark.Trim();
        var definition = DefinitionRegex.Match(trimmed);
        if (definition.Success)
        {
            return $"B{definition.Groups[1].Value}";
        }

        var codeOnly = CodeOnlyRegex.Match(trimmed);
        if (codeOnly.Success)
        {
            return $"B{codeOnly.Groups[1].Value}";
        }

        return string.Empty;
    }

    public static string GetDefinitionText(string? remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            return string.Empty;
        }

        var definition = DefinitionRegex.Match(remark.Trim());
        return definition.Success ? definition.Groups[2].Value.Trim() : string.Empty;
    }

    public static IReadOnlyList<DutyTemplateRemarkEntry> BuildLegend(IEnumerable<DutyTemplateRow> rows)
    {
        var legend = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var code = GetDisplayCode(row.Remark);
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var text = GetDefinitionText(row.Remark);
            if (!string.IsNullOrWhiteSpace(text) || !legend.ContainsKey(code))
            {
                legend[code] = text;
            }
        }

        return legend
            .OrderBy(entry => ParseCodeNumber(entry.Key))
            .Select(entry => new DutyTemplateRemarkEntry(entry.Key, entry.Value))
            .ToList();
    }

    public static string GetNextCode(IEnumerable<string?> remarks)
    {
        var max = remarks
            .Select(GetDisplayCode)
            .Select(ParseCodeNumber)
            .DefaultIfEmpty(0)
            .Max();
        return $"B{max + 1}";
    }

    public static bool IsLeerzeile(string? remark)
    {
        var text = GetDefinitionText(remark);
        return !string.IsNullOrWhiteSpace(text)
               && text.Contains("Leerzeile", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseCodeNumber(string code)
    {
        var match = Regex.Match(code, @"B(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : 0;
    }
}

public sealed record DutyTemplateRemarkEntry(string Code, string Text);
