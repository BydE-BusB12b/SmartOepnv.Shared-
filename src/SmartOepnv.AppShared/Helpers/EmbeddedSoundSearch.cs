namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Flexible Suche in eingebetteten Tondateinamen (z. B. „Wuppertal Vohwinkel“ → WUPPERTAL_VOHWINKEL,
/// „Mettmann Neanderthal“ → 0090_NEANDERTHAL_zusammen.wav).
/// </summary>
public static class EmbeddedSoundSearch
{
    private const double PartialTokenMatchRatio = 0.55;

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

        return !string.IsNullOrWhiteSpace(extraSearchText) && MatchesInternal(extraSearchText, query);
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
        var matchedTokens = tokens.Where(token => TokenMatches(token, words, compactHaystack)).ToList();
        if (matchedTokens.Count == tokens.Count)
        {
            return true;
        }

        if (tokens.Count == 1)
        {
            return matchedTokens.Count == 1;
        }

        var totalWeight = tokens.Sum(token => token.Length);
        if (totalWeight == 0)
        {
            return false;
        }

        var matchedWeight = matchedTokens.Sum(token => token.Length);
        return matchedWeight >= totalWeight * PartialTokenMatchRatio;
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

        var buffer = new char[text.Length];
        var length = 0;
        var lastWasSpace = true;

        foreach (var ch in text)
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

    private static string Compact(string text) => text.Replace(" ", string.Empty);

    private static List<string> Tokenize(string query) =>
        query
            .Split(QuerySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
