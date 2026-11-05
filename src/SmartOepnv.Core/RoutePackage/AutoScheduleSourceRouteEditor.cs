using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Merkt pro Route die Fahrplan-Vorlage (<c>routeAutoScheduleSources</c> in routes_export.json).
/// </summary>
public static class AutoScheduleSourceRouteEditor
{
    public const string RootFieldName = "routeAutoScheduleSources";

    public static Dictionary<string, string> LoadFromRoot(JsonObject root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root[RootFieldName] is not JsonObject map)
        {
            return result;
        }

        foreach (var entry in map)
        {
            var source = entry.Value?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrEmpty(source))
            {
                continue;
            }

            result[entry.Key.Trim()] = RouteDisplayHelper.ToDistributionDisplayString(source);
        }

        return result;
    }

    public static string GetSourceRoute(
        IDictionary<string, string> map,
        string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (map.TryGetValue(key, out var source) && !string.IsNullOrWhiteSpace(source))
        {
            return source.Trim();
        }

        if (map.TryGetValue(routeDisplayKey.Trim(), out source) && !string.IsNullOrWhiteSpace(source))
        {
            return source.Trim();
        }

        return string.Empty;
    }

    public static void SetSourceRoute(
        IDictionary<string, string> map,
        string routeDisplayKey,
        string sourceRouteKey)
    {
        var targetKey = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        var sourceKey = RouteDisplayHelper.ToDistributionDisplayString(sourceRouteKey);
        map.Remove(routeDisplayKey.Trim());
        if (string.IsNullOrEmpty(sourceKey))
        {
            map.Remove(targetKey);
            return;
        }

        map[targetKey] = sourceKey;
    }

    public static void RemoveRoute(IDictionary<string, string> map, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(key);
        map.Remove(routeDisplayKey.Trim());

        foreach (var entry in map.Where(pair => RouteDisplayHelper.RouteKeysMatch(pair.Value, routeDisplayKey)).ToList())
        {
            map.Remove(entry.Key);
        }
    }

    public static void RenameRouteKey(
        IDictionary<string, string> map,
        string oldKey,
        string newKey)
    {
        if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
        {
            return;
        }

        var oldTarget = RouteDisplayHelper.ToDistributionDisplayString(oldKey);
        var newTarget = RouteDisplayHelper.ToDistributionDisplayString(newKey);
        if (map.TryGetValue(oldTarget, out var asTarget))
        {
            map.Remove(oldTarget);
            map.Remove(oldKey.Trim());
            if (!string.IsNullOrEmpty(asTarget))
            {
                map[newTarget] = asTarget;
            }
        }

        foreach (var entry in map
                     .Where(pair => RouteDisplayHelper.RouteKeysMatch(pair.Value, oldKey))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            map[entry] = newTarget;
        }
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        IDictionary<string, string> map)
    {
        var allowedKeys = routeKeys
            .Select(RouteDisplayHelper.ToDistributionDisplayString)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var obj = new JsonObject();
        foreach (var entry in map.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            var targetKey = RouteDisplayHelper.ToDistributionDisplayString(entry.Key);
            if (!allowedKeys.Contains(targetKey))
            {
                continue;
            }

            var source = entry.Value?.Trim();
            if (string.IsNullOrEmpty(source) || !allowedKeys.Contains(source))
            {
                continue;
            }

            obj[targetKey] = source;
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
