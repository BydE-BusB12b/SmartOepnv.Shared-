namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ersetzt eine Eigenschaft im obersten JSON-Objekt, ohne das ganze Dokument zu parsen.</summary>
public static class JsonTopLevelProperty
{
    public static bool TryReplace(string json, string propertyName, string rawValue, out string updated)
    {
        updated = json;
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(propertyName) || rawValue is null)
        {
            return false;
        }

        if (!TryLocateValue(json, propertyName, out var valueStart, out var valueEnd))
        {
            return false;
        }

        updated = string.Concat(json.AsSpan(0, valueStart), rawValue, json.AsSpan(valueEnd));
        return true;
    }

    public static string Upsert(string json, string propertyName, string rawValue)
    {
        if (TryReplace(json, propertyName, rawValue, out var updated))
        {
            return updated;
        }

        var trimmed = json.TrimEnd();
        var close = trimmed.LastIndexOf('}');
        if (close < 0)
        {
            return json;
        }

        var needsComma = false;
        for (var i = close - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(trimmed[i]))
            {
                continue;
            }

            needsComma = trimmed[i] != '{';
            break;
        }

        var insertion = (needsComma ? "," : string.Empty) + "\"" + propertyName + "\":" + rawValue;
        return string.Concat(trimmed.AsSpan(0, close), insertion, trimmed.AsSpan(close));
    }

    private static bool TryLocateValue(string json, string propertyName, out int valueStart, out int valueEnd)
    {
        valueStart = 0;
        valueEnd = 0;
        var depth = 0;
        var i = 0;
        while (i < json.Length)
        {
            var c = json[i];
            if (c == '"')
            {
                if (!TryReadString(json, i, out var text, out var next))
                {
                    return false;
                }

                if (depth == 1 && text == propertyName)
                {
                    var value = SkipWhiteSpace(json, next);
                    if (value >= json.Length || json[value] != ':')
                    {
                        return false;
                    }

                    valueStart = SkipWhiteSpace(json, value + 1);
                    valueEnd = EndOfValue(json, valueStart);
                    return valueEnd > valueStart;
                }

                i = next;
                continue;
            }

            if (c == '{' || c == '[')
            {
                depth++;
            }
            else if (c == '}' || c == ']')
            {
                depth--;
            }

            i++;
        }

        return false;
    }

    private static int EndOfValue(string json, int start)
    {
        if (start >= json.Length)
        {
            return start;
        }

        var c = json[start];
        if (c == '"')
        {
            return TryReadString(json, start, out _, out var next) ? next : start;
        }

        if (c is '{' or '[')
        {
            var depth = 0;
            var i = start;
            while (i < json.Length)
            {
                var ch = json[i];
                if (ch == '"')
                {
                    if (!TryReadString(json, i, out _, out var next))
                    {
                        return start;
                    }

                    i = next;
                    continue;
                }

                if (ch is '{' or '[')
                {
                    depth++;
                }
                else if (ch is '}' or ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i + 1;
                    }
                }

                i++;
            }

            return start;
        }

        var end = start;
        while (end < json.Length && json[end] is not ',' and not '}' and not ']')
        {
            end++;
        }

        return end;
    }

    private static bool TryReadString(string json, int openQuote, out string text, out int next)
    {
        text = string.Empty;
        next = openQuote;
        if (openQuote >= json.Length || json[openQuote] != '"')
        {
            return false;
        }

        var i = openQuote + 1;
        while (i < json.Length)
        {
            var c = json[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == '"')
            {
                text = json.Substring(openQuote + 1, i - openQuote - 1);
                next = i + 1;
                return true;
            }

            i++;
        }

        return false;
    }

    private static int SkipWhiteSpace(string json, int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }

        return index;
    }
}
