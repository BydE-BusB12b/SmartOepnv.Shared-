using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Datumsbereich pro Route in <c>routes_export.json</c> (<c>routeDateRanges</c>).
/// Fehlendes Feld = keine Datumsbeschränkung (abwärtskompatibel).
/// </summary>
public static class RouteDateRangeEditor
{
    public const string RootFieldName = "routeDateRanges";
    public const string FromFieldName = "from";
    public const string ToFieldName = "to";

    public static Dictionary<string, RouteDateRange> LoadFromRoot(JsonObject root)
    {
        var result = new Dictionary<string, RouteDateRange>(StringComparer.Ordinal);
        if (root[RootFieldName] is not JsonObject map)
        {
            return result;
        }

        foreach (var entry in map)
        {
            if (entry.Value is not JsonObject obj || string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            var from = obj[FromFieldName]?.GetValue<string>();
            var to = obj[ToFieldName]?.GetValue<string>();
            if (!RouteDateRange.TryParse(from, to, out var range) || !range.IsRestricted)
            {
                continue;
            }

            result[entry.Key.Trim()] = range;
        }

        return result;
    }

    public static RouteDateRange GetRangeForRoute(
        IDictionary<string, RouteDateRange> map,
        string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (map.TryGetValue(key, out var range))
        {
            return range;
        }

        if (map.TryGetValue(routeDisplayKey.Trim(), out range))
        {
            return range;
        }

        return RouteDateRange.Unrestricted;
    }

    public static void SetRangeForRoute(
        IDictionary<string, RouteDateRange> map,
        string routeDisplayKey,
        RouteDateRange? range)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (range is null || !range.IsRestricted)
        {
            map.Remove(key);
            map.Remove(routeDisplayKey.Trim());
            return;
        }

        map[key] = range;
    }

    public static void RemoveRoute(IDictionary<string, RouteDateRange> map, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(key);
        map.Remove(routeDisplayKey.Trim());
    }

    public static void RenameRouteKey(
        IDictionary<string, RouteDateRange> map,
        string oldRouteDisplayKey,
        string newRouteDisplayKey)
    {
        var range = GetRangeForRoute(map, oldRouteDisplayKey);
        if (!range.IsRestricted)
        {
            return;
        }

        RemoveRoute(map, oldRouteDisplayKey);
        SetRangeForRoute(map, newRouteDisplayKey, range);
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        IDictionary<string, RouteDateRange> map)
    {
        var allowedKeys = routeKeys
            .Select(RouteDisplayHelper.ToDistributionDisplayString)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var obj = new JsonObject();
        foreach (var entry in map.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!entry.Value.IsRestricted)
            {
                continue;
            }

            var key = RouteDisplayHelper.ToDistributionDisplayString(entry.Key);
            if (!allowedKeys.Contains(key))
            {
                continue;
            }

            var item = new JsonObject();
            if (entry.Value.From is { } from)
            {
                item[FromFieldName] = RouteDateRange.FormatDate(from);
            }

            if (entry.Value.To is { } to)
            {
                item[ToFieldName] = RouteDateRange.FormatDate(to);
            }

            if (item.Count == 0)
            {
                continue;
            }

            obj[key] = item;
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
