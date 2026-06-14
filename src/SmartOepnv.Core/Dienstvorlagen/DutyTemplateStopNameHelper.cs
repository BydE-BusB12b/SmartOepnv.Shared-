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
        return text.Trim();
    }

    public static bool StopsEqual(string? a, string? b) =>
        string.Equals(
            StripHaltestelleMarker(a),
            StripHaltestelleMarker(b),
            StringComparison.OrdinalIgnoreCase);
}
