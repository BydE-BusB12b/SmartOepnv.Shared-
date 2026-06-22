using System.Text.Json.Nodes;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Verkehrstage pro Route in <c>routes_export.json</c> (<c>routeOperatingDays</c>).
/// Betriebstag je Verkehrstag: 00:01 bis 03:59 Uhr am Folgetag (Logik in der App, Überschneidungen möglich).
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
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (map.TryGetValue(key, out var days))
        {
            return new HashSet<DutyOperatingDay>(days);
        }

        if (map.TryGetValue(routeDisplayKey.Trim(), out days))
        {
            return new HashSet<DutyOperatingDay>(days);
        }

        return [];
    }

    public static void SetDaysForRoute(
        IDictionary<string, HashSet<DutyOperatingDay>> map,
        string routeDisplayKey,
        IEnumerable<DutyOperatingDay> days)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        var normalized = days.Distinct().ToHashSet();
        if (IsConfiguredForAllDays(normalized))
        {
            map.Remove(key);
            map.Remove(routeDisplayKey.Trim());
            return;
        }

        map[key] = normalized;
    }

    public static void RemoveRoute(
        IDictionary<string, HashSet<DutyOperatingDay>> map,
        string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(key);
        map.Remove(routeDisplayKey.Trim());
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        IDictionary<string, HashSet<DutyOperatingDay>> map)
    {
        var allowedKeys = routeKeys
            .Select(RouteDisplayHelper.ToDistributionDisplayString)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var obj = new JsonObject();
        foreach (var entry in map.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (IsConfiguredForAllDays(entry.Value))
            {
                continue;
            }

            var key = RouteDisplayHelper.ToDistributionDisplayString(entry.Key);
            if (!allowedKeys.Contains(key))
            {
                continue;
            }

            var arr = new JsonArray();
            foreach (var day in entry.Value.OrderBy(d => (int)d))
            {
                arr.Add(ToDayId(day));
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
}
