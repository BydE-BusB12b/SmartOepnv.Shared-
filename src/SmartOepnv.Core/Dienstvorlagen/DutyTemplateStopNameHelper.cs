using System.Text.RegularExpressions;

namespace SmartOepnv.Core.Dienstvorlagen;

public static class DutyTemplateStopNameHelper
{
    private static readonly Regex HaltestelleMarkerRegex = new(@"\(H\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>Entfernt die Ersatzfahrplan-Kennzeichnung „(H)“ aus Haltestellennamen.</summary>
    public static string StripHaltestelleMarker(string? stop)
    {
        if (string.IsNullOrWhiteSpace(stop))
        {
            return string.Empty;
        }

        var text = HaltestelleMarkerRegex.Replace(stop.Trim(), string.Empty);
        text = WhitespaceRegex.Replace(text, " ");
        text = CollapseDuplicatedStationPrefix(text);
        return text.Trim();
    }

    /// <summary>
    /// Nutzt für den Fahrplan-Import nur die Ersatzhaltestelle (Spalte „Ersatzhaltestelle“ / „(H) …“),
    /// nicht den Bahnhof – vermeidet doppelte Namen wie „Düsseldorf Hbf Düsseldorf Hbf, Bussteig 17“.
    /// </summary>
    public static string ResolveImportStopName(string? shortStop, string? longStop)
    {
        var shortText = shortStop?.Trim() ?? string.Empty;
        var longText = longStop?.Trim() ?? string.Empty;

        if (ContainsHaltestelleMarker(longText) || LooksLikeReplacementStop(longText))
        {
            return StripHaltestelleMarker(longText);
        }

        if (ContainsHaltestelleMarker(shortText) || LooksLikeReplacementStop(shortText))
        {
            return StripHaltestelleMarker(shortText);
        }

        if (!string.IsNullOrWhiteSpace(longText) &&
            !string.Equals(shortText, longText, StringComparison.OrdinalIgnoreCase))
        {
            return StripHaltestelleMarker(longText);
        }

        var combined = string.Join(" ", new[] { shortText, longText }.Where(part => part.Length > 0)).Trim();
        if (TryExtractHaltestelleSegment(combined, out var haltestelle))
        {
            return StripHaltestelleMarker(haltestelle);
        }

        return StripHaltestelleMarker(combined);
    }

    /// <summary>Trennt Bahnhof und Ersatzhaltestelle aus einer zusammengezogenen PDF-Zeile.</summary>
    public static (string ShortStop, string LongStop) SplitBahnhofAndHaltestelle(string stops)
    {
        var trimmed = stops.Trim();
        if (TryExtractHaltestelleSegment(trimmed, out var haltestelle))
        {
            var bahnhof = trimmed[..trimmed.IndexOf("(H)", StringComparison.OrdinalIgnoreCase)].Trim();
            return (bahnhof, haltestelle);
        }

        var parts = WhitespaceRegex.Split(trimmed)
            .Where(part => part.Length > 0)
            .ToArray();
        if (parts.Length >= 4)
        {
            var midpoint = parts.Length / 2;
            var first = string.Join(' ', parts.Take(midpoint));
            var second = string.Join(' ', parts.Skip(midpoint));
            if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            {
                return (first, second);
            }
        }

        return (trimmed, trimmed);
    }

    public static bool StopsEqual(string? a, string? b) =>
        string.Equals(NormalizeForMatch(a), NormalizeForMatch(b), StringComparison.Ordinal);

    public static bool StopsMatch(string? actual, string? pattern) =>
        StopsEqual(actual, pattern);

    private static bool ContainsHaltestelleMarker(string text) =>
        text.Contains("(H)", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeReplacementStop(string text) =>
        text.Contains("Bussteig", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Bstg", StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractHaltestelleSegment(string text, out string haltestelle)
    {
        haltestelle = string.Empty;
        var index = text.IndexOf("(H)", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        haltestelle = text[index..].Trim();
        return haltestelle.Length > 0;
    }

    private static string CollapseDuplicatedStationPrefix(string text)
    {
        var commaIndex = text.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex <= 0)
        {
            return text;
        }

        var beforeComma = text[..commaIndex].Trim();
        var afterComma = text[commaIndex..];
        var words = beforeComma.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var len = words.Length / 2; len >= 1; len--)
        {
            if (words.Length < len * 2)
            {
                continue;
            }

            var first = string.Join(' ', words.Take(len));
            var second = string.Join(' ', words.Skip(len).Take(len));
            if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            {
                return $"{first}{afterComma}";
            }
        }

        return text;
    }

    private static string NormalizeForMatch(string? stop)
    {
        if (string.IsNullOrWhiteSpace(stop))
        {
            return string.Empty;
        }

        var text = StripHaltestelleMarker(stop);
        text = text.Replace(',', ' ');
        text = Regex.Replace(text, @"\bBussteig\b", "Bstg", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"\bBstg\.?\b", "Bstg", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = WhitespaceRegex.Replace(text, " ").Trim();
        return text.ToLowerInvariant();
    }
}
