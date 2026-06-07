using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Zusatzfelder im GPSAnsagen-Route-JSON (Allowlist, Außenanzeigen, …).
/// </summary>
public static class RoutePackagePhoneMetadata
{
    public static IList<string> LoadOutsideDisplays(JsonObject root)
    {
        if (root["outsideDisplays"] is JsonArray outside)
        {
            return ReadStringArray(outside);
        }

        if (root["destinationList"] is JsonArray destinationList)
        {
            return ReadStringArray(destinationList);
        }

        return [];
    }

    public static void SaveOutsideDisplays(JsonObject root, IList<string> entries)
    {
        if (entries.Count == 0)
        {
            root.Remove("outsideDisplays");
            root.Remove("destinationList");
            return;
        }

        var arr = new JsonArray();
        foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            arr.AddString(entry.Trim());
        }

        if (arr.Count == 0)
        {
            root.Remove("outsideDisplays");
            root.Remove("destinationList");
            return;
        }

        root["outsideDisplays"] = arr;
        // GPSAnsagen liest teils destinationList (Merge), teils outsideDisplays (Replace) – beides befüllen.
        root["destinationList"] = arr.DeepClone();
    }

    public static IList<string> LoadAdditionalAllowedRoutes(JsonObject root, HashSet<string> exportedRoutes)
    {
        if (root["allowedRoutes"] is not JsonArray arr)
        {
            return [];
        }

        var extra = new List<string>();
        foreach (var node in arr)
        {
            var name = node?.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(name) || exportedRoutes.Contains(name))
            {
                continue;
            }

            if (!extra.Contains(name, StringComparer.Ordinal))
            {
                extra.Add(name);
            }
        }

        return extra;
    }

    public static void SaveAllowedRoutes(JsonObject root, IEnumerable<string> exportedRoutes, IEnumerable<string> additionalAllowed)
    {
        var allowed = exportedRoutes
            .Concat(additionalAllowed)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.Ordinal);

        var arr = new JsonArray();
        foreach (var name in allowed.OrderBy(n => n, StringComparer.Ordinal))
        {
            arr.AddString(name);
        }

        root["allowedRoutes"] = arr;
    }

    /// <summary>
    /// Wie Handy: Werte in <c>routePathDrafts</c> / <c>routeOfflineGuidance</c> sind JSON-Strings.
    /// </summary>
    public static void SyncStringKeyedRouteBlocks(
        JsonObject root,
        string propertyName,
        IEnumerable<string>? onlyRoutes = null)
    {
        if (root[propertyName] is not JsonObject existing)
        {
            root.Remove(propertyName);
            return;
        }

        HashSet<string>? exportSet = onlyRoutes is null ? null : onlyRoutes.ToHashSet(StringComparer.Ordinal);
        var filtered = new JsonObject();
        foreach (var entry in existing)
        {
            if (exportSet is not null && !exportSet.Contains(entry.Key))
            {
                continue;
            }

            var text = JsonNodeExtensions.DraftNodeToJsonText(entry.Value);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            filtered[entry.Key] = JsonValue.Create(text);
        }

        if (filtered.Count > 0)
        {
            root[propertyName] = filtered;
        }
        else
        {
            root.Remove(propertyName);
        }
    }

    public static IEnumerable<string> GetRouteKeysFromBlock(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonObject obj)
        {
            return [];
        }

        return obj.Select(p => p.Key);
    }

    private static List<string> ReadStringArray(JsonArray arr)
    {
        var list = new List<string>();
        foreach (var node in arr)
        {
            var value = node?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(value))
            {
                list.Add(value);
            }
        }

        return list;
    }
}
