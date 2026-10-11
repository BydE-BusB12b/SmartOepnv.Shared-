using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Steuert, ob eine Route in der ITCS-Dialog „Route wählen“ erscheint
/// (<c>routes_export.json</c> → <c>routeItcsRouteListExcluded</c>).
/// Fehlender Eintrag = Route ist in der Liste (Abwärtskompatibilität).
/// </summary>
public static class RouteItcsRouteListEditor
{
    public const string RootFieldName = "routeItcsRouteListExcluded";

    public static HashSet<string> LoadFromRoot(JsonObject root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (root[RootFieldName] is not JsonArray arr)
        {
            return result;
        }

        foreach (var node in arr)
        {
            var key = node?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(key))
            {
                result.Add(RouteDisplayHelper.ToDistributionDisplayString(key));
            }
        }

        return result;
    }

    public static bool IsInItcsRouteList(ISet<string> excludedRoutes, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        if (excludedRoutes.Contains(key))
        {
            return false;
        }

        return !excludedRoutes.Contains(routeDisplayKey.Trim());
    }

    public static void SetInItcsRouteList(ISet<string> excludedRoutes, string routeDisplayKey, bool inList)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        excludedRoutes.Remove(routeDisplayKey.Trim());
        if (inList)
        {
            excludedRoutes.Remove(key);
            return;
        }

        excludedRoutes.Add(key);
    }

    public static void RemoveRoute(ISet<string> excludedRoutes, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        excludedRoutes.Remove(key);
        excludedRoutes.Remove(routeDisplayKey.Trim());
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        ISet<string> excludedRoutes)
    {
        var allowedKeys = routeKeys
            .Select(RouteDisplayHelper.ToDistributionDisplayString)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var arr = new JsonArray();
        foreach (var key in excludedRoutes
                     .Select(RouteDisplayHelper.ToDistributionDisplayString)
                     .Where(allowedKeys.Contains)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            arr.Add(key);
        }

        if (arr.Count == 0)
        {
            root.Remove(RootFieldName);
        }
        else
        {
            root[RootFieldName] = arr;
        }
    }
}
