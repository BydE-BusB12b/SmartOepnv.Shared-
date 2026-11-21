using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Zieltext für die Haltestellenanzeige (DS009) im Wechsel mit Linie/Ziel – pro Route in
/// <c>routes_export.json</c> (<c>routeInteriorDisplayDestinations</c>).
/// </summary>
public static class RouteInteriorDisplayDestinationEditor
{
    public const string RootFieldName = "routeInteriorDisplayDestinations";

    public static Dictionary<string, string> LoadFromRoot(JsonObject root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root[RootFieldName] is not JsonObject map)
        {
            return result;
        }

        foreach (var entry in map)
        {
            var text = entry.Value?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrEmpty(text))
            {
                continue;
            }

            result[entry.Key.Trim()] = text;
        }

        return result;
    }

    public static string GetForRoute(
        IDictionary<string, string> map,
        string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (map.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        if (map.TryGetValue(routeDisplayKey.Trim(), out text) && !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return string.Empty;
    }

    public static void SetForRoute(
        IDictionary<string, string> map,
        string routeDisplayKey,
        string? text)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        var normalized = text?.Trim() ?? string.Empty;
        map.Remove(routeDisplayKey.Trim());
        if (string.IsNullOrEmpty(normalized))
        {
            map.Remove(key);
            return;
        }

        map[key] = normalized;
    }

    public static void RemoveRoute(IDictionary<string, string> map, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        map.Remove(key);
        map.Remove(routeDisplayKey.Trim());
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
            var key = RouteDisplayHelper.ToDistributionDisplayString(entry.Key);
            if (!allowedKeys.Contains(key))
            {
                continue;
            }

            var text = entry.Value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            obj[key] = text;
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
