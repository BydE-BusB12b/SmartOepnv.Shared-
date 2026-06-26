using System.Globalization;
using System.Text;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Suche in eingebetteten Tondateinamen (z. B. „Wuppertal Vohwinkel“ → WUPPERTAL_VOHWINKEL).
/// </summary>
public static class EmbeddedSoundSearch
{
    private static readonly char[] QuerySeparators = [' ', ',', ';', '/', '\\', '-', '_', '.'];

    public static bool Matches(string fileName, string query, string? extraSearchText = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (MatchesInternal(fileName, query))
        {
            return true;
        }

        // Zusatztext (Haltestelle, Linien …) nur bei mehreren Suchbegriffen –
        // sonst liefert z. B. „Düs“ alle Einträge mit „Düsseldorf“ in der Beschreibung.
        var tokens = Tokenize(query);
        if (tokens.Count >= 2 && !string.IsNullOrWhiteSpace(extraSearchText))
        {
            return MatchesInternal(extraSearchText, query);
        }

        return false;
    }

    private static bool MatchesInternal(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var haystack = Normalize(text);
        var normalizedQuery = Normalize(query);
        if (haystack.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var compactHaystack = Compact(haystack);
        var compactQuery = Compact(normalizedQuery);
        if (!string.IsNullOrEmpty(compactQuery) &&
            compactHaystack.Contains(compactQuery, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            return true;
        }

        var words = haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matchedTokens = tokens.Count(token => TokenMatches(token, words, compactHaystack));
        return matchedTokens == tokens.Count;
    }

    private static bool TokenMatches(string token, string[] words, string compactHaystack)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (compactHaystack.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return words.Any(word => word.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var folded = FoldDiacritics(text);
        var buffer = new char[folded.Length];
        var length = 0;
        var lastWasSpace = true;

        foreach (var ch in folded)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[length++] = char.ToLowerInvariant(ch);
                lastWasSpace = false;
                continue;
            }

            if (!lastWasSpace)
            {
                buffer[length++] = ' ';
                lastWasSpace = true;
            }
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static string FoldDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(ch switch
            {
                'ß' => "ss",
                'ẞ' => "ss",
                _ => ch
            });
        }

        return builder.ToString();
    }

    private static string Compact(string text) => text.Replace(" ", string.Empty);

    private static List<string> Tokenize(string query) =>
        query
            .Split(QuerySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
