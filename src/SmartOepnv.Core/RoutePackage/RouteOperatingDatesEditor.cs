using System.Globalization;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Einzelne Betriebstage pro Route in <c>routes_export.json</c> (<c>routeOperatingDates</c>).
/// Fehlendes/leeres Array = keine Whitelist (abwärtskompatibel).
/// Mit Verkehrstagen und optionalem von/bis: Route nur sichtbar, wenn das Datum
/// in der Liste steht UND der Wochentag markiert ist UND im Datumsbereich liegt.
/// </summary>
public static class RouteOperatingDatesEditor
{
    public const string RootFieldName = "routeOperatingDates";

    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    private static readonly string[] AcceptedInputFormats =
    [
        "dd.MM.yyyy",
        "d.M.yyyy",
        "dd.MM.yy",
        "d.M.yy",
        "yyyy-MM-dd",
        "dd.MM",
        "d.M"
    ];

    public static Dictionary<string, HashSet<DateOnly>> LoadFromRoot(JsonObject root)
    {
        var result = new Dictionary<string, HashSet<DateOnly>>(StringComparer.Ordinal);
        if (root[RootFieldName] is not JsonObject map)
        {
            return result;
        }

        foreach (var entry in map)
        {
            if (entry.Value is not JsonArray arr || string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            var dates = new HashSet<DateOnly>();
            foreach (var node in arr)
            {
                var raw = node?.GetValue<string>();
                if (TryParseDate(raw, out var date))
                {
                    dates.Add(date);
                }
            }

            if (dates.Count > 0)
            {
                result[entry.Key.Trim()] = dates;
            }
        }

        return result;
    }

    public static HashSet<DateOnly> GetDatesForRoute(
        IDictionary<string, HashSet<DateOnly>> map,
        string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (map.TryGetValue(key, out var dates))
        {
            return new HashSet<DateOnly>(dates);
        }

        if (map.TryGetValue(routeDisplayKey.Trim(), out dates))
        {
            return new HashSet<DateOnly>(dates);
        }

        return [];
    }

    public static void SetDatesForRoute(
        IDictionary<string, HashSet<DateOnly>> map,
        string routeDisplayKey,
        IEnumerable<DateOnly>? dates)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        var normalized = dates?.Distinct().ToHashSet() ?? [];
        if (normalized.Count == 0)
        {
            map.Remove(key);
            map.Remove(routeDisplayKey.Trim());
            return;
        }

        map[key] = normalized;
    }

    public static void RemoveRoute(IDictionary<string, HashSet<DateOnly>> map, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(key);
        map.Remove(routeDisplayKey.Trim());
    }

    public static void RenameRouteKey(
        IDictionary<string, HashSet<DateOnly>> map,
        string oldRouteDisplayKey,
        string newRouteDisplayKey)
    {
        var dates = GetDatesForRoute(map, oldRouteDisplayKey);
        if (dates.Count == 0)
        {
            return;
        }

        RemoveRoute(map, oldRouteDisplayKey);
        SetDatesForRoute(map, newRouteDisplayKey, dates);
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        IDictionary<string, HashSet<DateOnly>> map)
    {
        var allowedKeys = routeKeys
            .Select(RouteDisplayHelper.ToDistributionDisplayString)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var obj = new JsonObject();
        foreach (var entry in map.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (entry.Value.Count == 0)
            {
                continue;
            }

            var key = RouteDisplayHelper.ToDistributionDisplayString(entry.Key);
            if (!allowedKeys.Contains(key))
            {
                continue;
            }

            var arr = new JsonArray();
            foreach (var date in entry.Value.OrderBy(d => d))
            {
                arr.Add(RouteDateRange.FormatDate(date));
            }

            obj[key] = arr;
        }

        if (obj.Count == 0)
        {
            root.Remove(RootFieldName);
        }
        else
        {
            root[RootFieldName] = obj;
        }
    }

    public static bool IsRestricted(IReadOnlyCollection<DateOnly>? dates) =>
        dates is { Count: > 0 };

    public static bool Contains(IReadOnlyCollection<DateOnly>? dates, DateOnly date) =>
        !IsRestricted(dates) || dates!.Contains(date);

    /// <summary>Leere/fehlende Liste = keine Einschränkung → Überschneidung wie bei Datumsbereichen.</summary>
    public static bool DateListsOverlap(
        IReadOnlyCollection<DateOnly>? left,
        IReadOnlyCollection<DateOnly>? right)
    {
        if (!IsRestricted(left) || !IsRestricted(right))
        {
            return true;
        }

        return left!.Any(right!.Contains);
    }

    public static string FormatDisplay(IEnumerable<DateOnly>? dates)
    {
        if (dates is null)
        {
            return string.Empty;
        }

        var ordered = dates.OrderBy(d => d).Select(RouteDateRange.FormatDate).ToList();
        return ordered.Count == 0 ? string.Empty : string.Join(", ", ordered);
    }

    /// <summary>
    /// Kurze Anzeige für UI-Zeilen: ein Tag, zusammenhängender Bereich oder Spanne mit Anzahl.
    /// </summary>
    public static string FormatSummary(IReadOnlyCollection<DateOnly>? dates)
    {
        if (!IsRestricted(dates))
        {
            return string.Empty;
        }

        var ordered = dates!.OrderBy(d => d).ToList();
        if (ordered.Count == 1)
        {
            return RouteDateRange.FormatDate(ordered[0]);
        }

        var from = RouteDateRange.FormatDate(ordered[0]);
        var to = RouteDateRange.FormatDate(ordered[^1]);
        var contiguous = true;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i] != ordered[i - 1].AddDays(1))
            {
                contiguous = false;
                break;
            }
        }

        return contiguous
            ? $"{from} – {to}"
            : $"{from} – {to} ({ordered.Count} Betriebstage)";
    }

    public static bool TryParseDate(string? raw, out DateOnly date)
    {
        date = default;
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        foreach (var format in AcceptedInputFormats)
        {
            if (!DateOnly.TryParseExact(trimmed, format, GermanCulture, DateTimeStyles.None, out date))
            {
                continue;
            }

            // TT.MM ohne Jahr → aktuelles Kalenderjahr
            if (format is "dd.MM" or "d.M")
            {
                date = new DateOnly(DateTime.Today.Year, date.Month, date.Day);
            }

            return true;
        }

        return DateOnly.TryParse(trimmed, GermanCulture, DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Parst kommagetrennte / zeilenweise Datumsliste inkl. Bereiche
    /// (z. B. „28.07, 30.07.2026“ oder „10.08-14.08, 17.08-19.08, 20.08“).
    /// </summary>
    public static bool TryParseDateList(string? raw, out List<DateOnly> dates, out string? error)
    {
        dates = [];
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var parts = raw
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var set = new HashSet<DateOnly>();
        foreach (var part in parts)
        {
            if (!TryParseDateOrRange(part, set, out error))
            {
                return false;
            }
        }

        dates = set.OrderBy(d => d).ToList();
        return true;
    }

    private static bool TryParseDateOrRange(string part, HashSet<DateOnly> set, out string? error)
    {
        error = null;
        var trimmed = part.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        // Bereich: „10.08-14.08“ / „10.08.2026-14.08.2026“
        var dash = trimmed.IndexOf('-');
        if (dash > 0 && dash < trimmed.Length - 1 && trimmed.Contains('.', StringComparison.Ordinal))
        {
            var fromRaw = trimmed[..dash].Trim();
            var toRaw = trimmed[(dash + 1)..].Trim();
            if (TryParseDate(fromRaw, out var from) && TryParseDate(toRaw, out var to))
            {
                if (to < from)
                {
                    (from, to) = (to, from);
                }

                for (var d = from; d <= to; d = d.AddDays(1))
                {
                    set.Add(d);
                }

                return true;
            }
        }

        if (!TryParseDate(trimmed, out var date))
        {
            error = $"Ungültiges Datum „{trimmed}“ – bitte TT.MM, TT.MM.JJJJ oder Bereich TT.MM-TT.MM.";
            return false;
        }

        set.Add(date);
        return true;
    }
}
