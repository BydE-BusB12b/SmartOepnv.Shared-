using System.Globalization;
using System.Text;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Suche in eingebetteten Tondateinamen (z. B. „Wuppertal Vohwinkel“ → WUPPERTAL_VOHWINKEL).
/// Tokenbasiert: „Dingshaus / SWS“ findet auch „0025_Dingshaus.wav“.
/// </summary>
public static class EmbeddedSoundSearch
{
    private static readonly char[] QuerySeparators = [' ', ',', ';', '/', '\\', '-', '_', '.', '(', ')', '[', ']'];

    /// <summary>Mindestlänge für „bedeutsame“ Tokens (Kurztokens wie „S“ allein reichen nicht).</summary>
    private const int SignificantTokenMinLength = 3;

    public static bool Matches(string fileName, string query, string? extraSearchText = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Score(fileName, query, extraSearchText) > 0;
    }

    /// <summary>
    /// Höher = besserer Treffer. 0 = kein Treffer.
    /// Bevorzugt Dateinamen-Treffer und mehr passende Tokens.
    /// </summary>
    public static int Score(string fileName, string query, string? extraSearchText = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 1;
        }

        var fileScore = ScoreInternal(fileName, query);
        if (fileScore > 0)
        {
            // Dateiname-Treffer stärker gewichten als nur Zusatztext
            return fileScore + 100;
        }

        if (!string.IsNullOrWhiteSpace(extraSearchText))
        {
            return ScoreInternal(extraSearchText, query);
        }

        return 0;
    }

    private static int ScoreInternal(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var haystack = Normalize(text);
        var normalizedQuery = Normalize(query);
        if (haystack.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 50 + Math.Min(normalizedQuery.Length, 40);
        }

        var compactHaystack = Compact(haystack);
        var compactQuery = Compact(normalizedQuery);
        if (!string.IsNullOrEmpty(compactQuery) &&
            compactHaystack.Contains(compactQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 40 + Math.Min(compactQuery.Length, 40);
        }

        var tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            return 1;
        }

        var words = haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matched = tokens.Count(token => TokenMatches(token, words, compactHaystack));
        if (matched == tokens.Count)
        {
            return 30 + matched * 5;
        }

        // Teiltreffer: mind. ein bedeutsames Token trifft (z. B. Dingshaus aus „Dingshaus / SWS“)
        var significantMatched = tokens
            .Where(t => Compact(t).Length >= SignificantTokenMinLength)
            .Count(token => TokenMatches(token, words, compactHaystack));
        if (significantMatched > 0)
        {
            return 10 + significantMatched * 5;
        }

        return 0;
    }

    private static bool TokenMatches(string token, string[] words, string compactHaystack)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var compactToken = Compact(token);
        if (compactToken.Length > 0 &&
            compactHaystack.Contains(compactToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return words.Any(word => word.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                                 Compact(word).Contains(compactToken, StringComparison.OrdinalIgnoreCase));
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
