using System.Globalization;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Linienersatz für IBIS-IP. Eine Zeile oder ein Bereich, z. B. 001–099 → RE1–RE99.
/// </summary>
public sealed record LineReplacementRule(
    string FromLine,
    string ToLine,
    string FromDisplay,
    string ToDisplay)
{
    public string ListTitle
    {
        get
        {
            if (string.Equals(FromLine, ToLine, StringComparison.Ordinal) &&
                string.Equals(FromDisplay, ToDisplay, StringComparison.Ordinal))
            {
                return $"{FromLine} → {FromDisplay}";
            }

            return $"{FromLine}–{ToLine} → {FromDisplay}–{ToDisplay}";
        }
    }

    public static bool TryCreate(
        string? fromLine,
        string? toLine,
        string? fromDisplay,
        string? toDisplay,
        out LineReplacementRule? rule,
        out string error)
    {
        rule = null;
        var lineFrom = (fromLine ?? string.Empty).Trim();
        var lineTo = (toLine ?? string.Empty).Trim();
        var displayFrom = (fromDisplay ?? string.Empty).Trim();
        var displayTo = (toDisplay ?? string.Empty).Trim();
        if (lineTo.Length == 0)
        {
            lineTo = lineFrom;
        }

        if (displayTo.Length == 0)
        {
            displayTo = displayFrom;
        }

        if (lineFrom.Length == 0 || displayFrom.Length == 0)
        {
            error = "Linie und Ersatztext eintragen.";
            return false;
        }

        if (!TryParseLineNumber(lineFrom, out var fromNumber) || !TryParseLineNumber(lineTo, out var toNumber))
        {
            error = "Die Linie muss eine Zahl sein, zum Beispiel 001 oder 047.";
            return false;
        }

        if (toNumber < fromNumber)
        {
            error = "Die Linie bis muss mindestens so groß sein wie die Linie von.";
            return false;
        }

        if (fromNumber == toNumber)
        {
            if (!string.Equals(displayFrom, displayTo, StringComparison.Ordinal))
            {
                error = "Bei einer einzelnen Linie nur einen Ersatztext eintragen.";
                return false;
            }

            rule = new LineReplacementRule(lineFrom, lineTo, displayFrom, displayTo);
            error = string.Empty;
            return true;
        }

        if (!TrySplitTrailingNumber(displayFrom, out var prefixFrom, out var displayFromNumber, out _, out var suffixFrom) ||
            !TrySplitTrailingNumber(displayTo, out var prefixTo, out var displayToNumber, out _, out var suffixTo))
        {
            error = "Bei einem Bereich muss der Ersatz eine Zahl enthalten, zum Beispiel RE1 und RE99 oder RE1X und RE99X.";
            return false;
        }

        if (!string.Equals(prefixFrom, prefixTo, StringComparison.Ordinal) ||
            !string.Equals(suffixFrom, suffixTo, StringComparison.Ordinal))
        {
            error = "Der Ersatz von und bis muss denselben Text vor und hinter der Zahl haben.";
            return false;
        }

        if (displayToNumber < displayFromNumber)
        {
            error = "Der Ersatz bis muss mindestens so groß sein wie der Ersatz von.";
            return false;
        }

        var lineCount = toNumber - fromNumber;
        var displayCount = displayToNumber - displayFromNumber;
        if (lineCount != displayCount)
        {
            error = $"Der Bereich passt nicht. Linien {lineFrom}–{lineTo} und Ersatz {displayFrom}–{displayTo} haben unterschiedlich viele Nummern.";
            return false;
        }

        rule = new LineReplacementRule(lineFrom, lineTo, displayFrom, displayTo);
        error = string.Empty;
        return true;
    }

    public JsonObject ToJson() =>
        new()
        {
            ["fromLine"] = FromLine,
            ["toLine"] = ToLine,
            ["fromDisplay"] = FromDisplay,
            ["toDisplay"] = ToDisplay
        };

    public static LineReplacementRule FromJson(JsonObject obj) =>
        new(
            JsonNodeReading.GetString(obj["fromLine"]),
            JsonNodeReading.GetString(obj["toLine"]),
            JsonNodeReading.GetString(obj["fromDisplay"]),
            JsonNodeReading.GetString(obj["toDisplay"]));

    private bool Contains(int lineNumber)
    {
        if (!TryParseLineNumber(FromLine, out var from) || !TryParseLineNumber(ToLine, out var to))
        {
            return false;
        }

        return lineNumber >= from && lineNumber <= to;
    }

    private int Span()
    {
        if (!TryParseLineNumber(FromLine, out var from) || !TryParseLineNumber(ToLine, out var to))
        {
            return int.MaxValue;
        }

        return to - from;
    }

    private string? DisplayFor(int lineNumber)
    {
        if (!TryParseLineNumber(FromLine, out var from) || !TryParseLineNumber(ToLine, out var to))
        {
            return null;
        }

        if (from == to)
        {
            return FromDisplay;
        }

        if (!TrySplitTrailingNumber(FromDisplay, out var prefix, out var displayFrom, out var padWidth, out var suffix))
        {
            return FromDisplay;
        }

        var value = displayFrom + (lineNumber - from);
        var digits = padWidth > 0
            ? value.ToString(CultureInfo.InvariantCulture).PadLeft(padWidth, '0')
            : value.ToString(CultureInfo.InvariantCulture);
        return prefix + digits + suffix;
    }

    public static string Resolve(IEnumerable<LineReplacementRule> rules, string? line)
    {
        var entered = line?.Trim() ?? string.Empty;
        if (entered.Length == 0)
        {
            return string.Empty;
        }

        if (!TryParseLineNumber(entered, out var number))
        {
            return entered;
        }

        LineReplacementRule? best = null;
        var bestSpan = int.MaxValue;
        foreach (var rule in rules)
        {
            if (!rule.Contains(number))
            {
                continue;
            }

            var span = rule.Span();
            if (span < bestSpan)
            {
                best = rule;
                bestSpan = span;
            }
        }

        return best?.DisplayFor(number) ?? entered;
    }

    private static bool TryParseLineNumber(string text, out int number)
    {
        number = 0;
        var digits = text.Trim();
        if (digits.Length == 0 || !digits.All(char.IsDigit))
        {
            return false;
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// Zahl am Ende oder direkt vor einem gemeinsamen Schluss, z. B. RE47 und RE47X.
    /// </summary>
    private static bool TrySplitTrailingNumber(
        string text,
        out string prefix,
        out int number,
        out int padWidth,
        out string suffix)
    {
        prefix = string.Empty;
        number = 0;
        padWidth = 0;
        suffix = string.Empty;
        var suffixStart = text.Length;
        while (suffixStart > 0 && !char.IsDigit(text[suffixStart - 1]))
        {
            suffixStart--;
        }

        var digitStart = suffixStart;
        while (digitStart > 0 && char.IsDigit(text[digitStart - 1]))
        {
            digitStart--;
        }

        if (digitStart == suffixStart)
        {
            return false;
        }

        var digits = text[digitStart..suffixStart];
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        prefix = text[..digitStart];
        suffix = text[suffixStart..];
        padWidth = digits.Length > 1 && digits[0] == '0' ? digits.Length : 0;
        return true;
    }
}

public static class LineReplacementList
{
    public const string JsonKey = "lineReplacements";

    public static IList<LineReplacementRule> LoadFromRoot(JsonObject root)
    {
        if (root[JsonKey] is not JsonArray arr)
        {
            return [];
        }

        var list = new List<LineReplacementRule>();
        foreach (var node in arr)
        {
            if (node is JsonObject obj)
            {
                var rule = LineReplacementRule.FromJson(obj);
                if (rule.FromLine.Length > 0 && rule.FromDisplay.Length > 0)
                {
                    list.Add(rule);
                }
            }
        }

        return list;
    }

    public static void SaveToRoot(JsonObject root, IList<LineReplacementRule> rules)
    {
        if (rules.Count == 0)
        {
            root.Remove(JsonKey);
            return;
        }

        var arr = new JsonArray();
        foreach (var rule in rules)
        {
            if (rule.FromLine.Length == 0 || rule.FromDisplay.Length == 0)
            {
                continue;
            }

            arr.Add(rule.ToJson());
        }

        if (arr.Count == 0)
        {
            root.Remove(JsonKey);
            return;
        }

        root[JsonKey] = arr;
    }
}
