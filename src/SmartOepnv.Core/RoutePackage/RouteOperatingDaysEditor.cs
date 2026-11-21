using System.Text.Json.Nodes;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Verkehrstage pro Route in <c>routes_export.json</c> (<c>routeOperatingDays</c>).
/// Betriebstag je Verkehrstag: 03:00 bis 02:59 Uhr am Folgetag (Logik in der App; ab 03:00 neuer Tag).
/// Schlüssel = voller Anzeigename inkl. „Verkehr: …“, damit Tagesvarianten getrennt bleiben.
/// </summary>
public static class RouteOperatingDaysEditor
{
    public const string RootFieldName = "routeOperatingDays";

    private static readonly Dictionary<DutyOperatingDay, string> DayToId = new()
    {
        [DutyOperatingDay.Monday] = "monday",
        [DutyOperatingDay.Tuesday] = "tuesday",
        [DutyOperatingDay.Wednesday] = "wednesday",
        [DutyOperatingDay.Thursday] = "thursday",
        [DutyOperatingDay.Friday] = "friday",
        [DutyOperatingDay.Saturday] = "saturday",
        [DutyOperatingDay.SundayHoliday] = "sundayHoliday"
    };

    private static readonly Dictionary<string, DutyOperatingDay> IdToDay =
        DayToId.ToDictionary(x => x.Value, x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<DutyOperatingDay> AllDays { get; } =
        DayToId.Keys.OrderBy(d => (int)d).ToList();

    public static string ToDayId(DutyOperatingDay day) => DayToId[day];

    public static bool TryParseDayId(string? id, out DutyOperatingDay day)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            day = default;
            return false;
        }

        return IdToDay.TryGetValue(id.Trim(), out day);
    }

    /// <summary>Kein Eintrag oder alle Tage = täglich verfügbar.</summary>
    public static bool IsConfiguredForAllDays(IReadOnlyCollection<DutyOperatingDay>? days) =>
        days is null || days.Count == 0 || days.Count >= AllDays.Count;

    public static HashSet<DutyOperatingDay> EffectiveDaySet(IEnumerable<DutyOperatingDay> days)
    {
        var set = days.Distinct().ToHashSet();
        return IsConfiguredForAllDays(set) ? AllDays.ToHashSet() : set;
    }

    public static bool DaysOverlap(
        IReadOnlyCollection<DutyOperatingDay> left,
        IReadOnlyCollection<DutyOperatingDay> right) =>
        EffectiveDaySet(left).Overlaps(EffectiveDaySet(right));

    public static Dictionary<string, HashSet<DutyOperatingDay>> LoadFromRoot(JsonObject root)
    {
        var result = new Dictionary<string, HashSet<DutyOperatingDay>>(StringComparer.Ordinal);
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

            var days = new HashSet<DutyOperatingDay>();
            foreach (var node in arr)
            {
                var id = node?.GetValue<string>();
                if (TryParseDayId(id, out var day))
                {
                    days.Add(day);
                }
            }

            result[entry.Key.Trim()] = days;
        }

        return result;
    }

    public static HashSet<DutyOperatingDay> GetDaysForRoute(
        IDictionary<string, HashSet<DutyOperatingDay>> map,
        string routeDisplayKey)
    {
        var trimmed = (routeDisplayKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        if (map.TryGetValue(trimmed, out var days))
        {
            return new HashSet<DutyOperatingDay>(days);
        }

        foreach (var key in map.Keys)
        {
            if (RouteDisplayHelper.RouteKeysMatchSameSchedule(key, trimmed))
            {
                return new HashSet<DutyOperatingDay>(map[key]);
            }
        }

        // Legacy: ein Eintrag ohne Verkehr-Suffix – nur nutzen, wenn keine Tagesvariante existiert.
        var distributionKey = RouteDisplayHelper.ToDistributionDisplayString(trimmed);
        if (map.TryGetValue(distributionKey, out days))
        {
            var hasDaySpecificEntry = map.Keys.Any(key =>
                RouteDisplayHelper.RouteKeysMatch(key, trimmed) &&
                !string.IsNullOrEmpty(RouteDisplayHelper.GetVerkehrLabel(key)));
            if (!hasDaySpecificEntry)
            {
                return new HashSet<DutyOperatingDay>(days);
            }
        }

        // Nach Alias-Merge ohne Verkehr-Suffix: genau eine Map-Variante derselben Fahrt übernehmen.
        var scheduleMatches = map.Keys
            .Where(key => RouteDisplayHelper.RouteKeysMatch(key, trimmed))
            .ToList();
        if (scheduleMatches.Count == 1)
        {
            return new HashSet<DutyOperatingDay>(map[scheduleMatches[0]]);
        }

        return [];
    }

    public static void SetDaysForRoute(
        IDictionary<string, HashSet<DutyOperatingDay>> map,
        string routeDisplayKey,
        IEnumerable<DutyOperatingDay> days)
    {
        var trimmed = (routeDisplayKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        var distributionKey = RouteDisplayHelper.ToDistributionDisplayString(trimmed);
        var normalized = days.Distinct().ToHashSet();
        if (IsConfiguredForAllDays(normalized))
        {
            map.Remove(trimmed);
            map.Remove(distributionKey);
            return;
        }

        map[trimmed] = normalized;

        // Alter gemeinsamer Schlüssel ohne Verkehr würde Tagesvarianten wieder vermischen.
        if (!string.IsNullOrEmpty(RouteDisplayHelper.GetVerkehrLabel(trimmed)))
        {
            map.Remove(distributionKey);
        }
    }

    public static void RemoveRoute(
        IDictionary<string, HashSet<DutyOperatingDay>> map,
        string routeDisplayKey)
    {
        var trimmed = (routeDisplayKey ?? string.Empty).Trim();
        map.Remove(trimmed);
        map.Remove(RouteDisplayHelper.ToDistributionDisplayString(trimmed));

        foreach (var key in map.Keys
                     .Where(key => RouteDisplayHelper.RouteKeysMatchSameSchedule(key, trimmed))
                     .ToList())
        {
            map.Remove(key);
        }
    }

    public static void RenameRouteKey(
        IDictionary<string, HashSet<DutyOperatingDay>> map,
        string oldRouteKey,
        string newRouteKey)
    {
        var oldTrimmed = (oldRouteKey ?? string.Empty).Trim();
        var newTrimmed = (newRouteKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(oldTrimmed) ||
            string.IsNullOrEmpty(newTrimmed) ||
            string.Equals(oldTrimmed, newTrimmed, StringComparison.Ordinal))
        {
            return;
        }

        var days = GetDaysForRoute(map, oldTrimmed);
        RemoveRoute(map, oldTrimmed);
        if (!IsConfiguredForAllDays(days))
        {
            SetDaysForRoute(map, newTrimmed, days);
        }
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        IDictionary<string, HashSet<DutyOperatingDay>> map)
    {
        var obj = new JsonObject();
        foreach (var routeKey in routeKeys
                     .Where(key => !string.IsNullOrWhiteSpace(key))
                     .Select(key => key.Trim())
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            var days = GetDaysForRoute(map, routeKey);
            if (IsConfiguredForAllDays(days))
            {
                continue;
            }

            var arr = new JsonArray();
            foreach (var day in days.OrderBy(d => (int)d))
            {
                arr.Add(ToDayId(day));
            }

            // Voller Anzeigeschlüssel inkl. Verkehr – getrennte Tagesvarianten bleiben getrennt.
            obj[routeKey] = arr;
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
}
