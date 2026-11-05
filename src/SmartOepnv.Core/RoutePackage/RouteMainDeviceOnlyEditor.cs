using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Steuert, ob eine Route nur auf Hauptnutzergeräten sichtbar ist
/// (<c>routes_export.json</c> → <c>routeMainDeviceOnly</c>).
/// Fehlender Eintrag = Route ist auf allen Geräten sichtbar (Abwärtskompatibilität).
/// </summary>
public static class RouteMainDeviceOnlyEditor
{
    public const string RootFieldName = "routeMainDeviceOnly";

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

    public static bool IsMainDeviceOnly(ISet<string> mainDeviceOnlyRoutes, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        return mainDeviceOnlyRoutes.Contains(key) ||
               mainDeviceOnlyRoutes.Contains(routeDisplayKey.Trim());
    }

    public static void SetMainDeviceOnly(ISet<string> mainDeviceOnlyRoutes, string routeDisplayKey, bool mainDeviceOnly)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        mainDeviceOnlyRoutes.Remove(routeDisplayKey.Trim());
        if (mainDeviceOnly)
        {
            mainDeviceOnlyRoutes.Add(key);
            return;
        }

        mainDeviceOnlyRoutes.Remove(key);
    }

    public static void RemoveRoute(ISet<string> mainDeviceOnlyRoutes, string routeDisplayKey)
    {
        var key = RouteDisplayHelper.ToDistributionDisplayString(routeDisplayKey);
        mainDeviceOnlyRoutes.Remove(key);
        mainDeviceOnlyRoutes.Remove(routeDisplayKey.Trim());
    }

    public static void SaveToRoot(
        JsonObject root,
        IEnumerable<string> routeKeys,
        ISet<string> mainDeviceOnlyRoutes)
    {
        var allowedKeys = routeKeys
            .Select(RouteDisplayHelper.ToDistributionDisplayString)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var arr = new JsonArray();
        foreach (var key in mainDeviceOnlyRoutes
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
