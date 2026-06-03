using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Vereinfachte Fahrwege für die Leitstellen-Karte: nur gesnappte Linie und Haltestellen.
/// Wird in <c>leitstelle_stand.json</c> unter <c>routePathOverviews</c> gespeichert.
/// </summary>
public static class LeitstelleRoutePathOverview
{
    public const string OverviewsKey = "routePathOverviews";

    public static JsonObject BuildOverviewsObject(EditableRoutePackage package)
    {
        var overviews = new JsonObject();
        if (package.PackageRoot["routePathDrafts"] is not JsonObject drafts)
        {
            return overviews;
        }

        foreach (var (routeName, draftNode) in drafts)
        {
            if (string.IsNullOrWhiteSpace(routeName))
            {
                continue;
            }

            var draftJson = JsonNodeExtensions.DraftNodeToJsonText(draftNode);
            if (string.IsNullOrWhiteSpace(draftJson))
            {
                continue;
            }

            try
            {
                var draftObj = JsonNode.Parse(draftJson)?.AsObject();
                if (draftObj is null)
                {
                    continue;
                }

                var overview = SimplifyDraftForOverview(draftObj, routeName);
                if (overview is not null)
                {
                    overviews[routeName] = overview.ToJsonString();
                }
            }
            catch
            {
                // Einzelne defekte Drafts überspringen
            }
        }

        return overviews;
    }

    public static void ApplyOverviewsToEditor(EditableRoutePackage editor, JsonObject root)
    {
        if (root[OverviewsKey] is not JsonObject overviews || overviews.Count == 0)
        {
            return;
        }

        editor.PackageRoot[OverviewsKey] = overviews.DeepClone().AsObject();
    }

    public static string? TryGetOverviewJson(JsonObject? packageRoot, string? routeName)
    {
        if (packageRoot is null || string.IsNullOrWhiteSpace(routeName))
        {
            return null;
        }

        var resolvedKey = ResolveRouteKey(packageRoot, routeName);
        if (resolvedKey is null)
        {
            return null;
        }

        if (packageRoot[OverviewsKey] is JsonObject overviews &&
            JsonNodeExtensions.DraftNodeToJsonText(overviews[resolvedKey]) is { } overviewJson)
        {
            return overviewJson;
        }

        if (packageRoot["routePathDrafts"] is JsonObject drafts &&
            JsonNodeExtensions.DraftNodeToJsonText(drafts[resolvedKey]) is { } draftJson)
        {
            try
            {
                var draftObj = JsonNode.Parse(draftJson)?.AsObject();
                if (draftObj is null)
                {
                    return null;
                }

                return SimplifyDraftForOverview(draftObj, resolvedKey)?.ToJsonString();
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> GetAllOverviewJsons(JsonObject? packageRoot)
    {
        if (packageRoot is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var key in CollectRouteKeys(packageRoot).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var overviewJson = TryGetOverviewJson(packageRoot, key);
            if (!string.IsNullOrWhiteSpace(overviewJson))
            {
                result.Add(overviewJson);
            }
        }

        return result;
    }

    public static string? ResolveRouteKey(JsonObject packageRoot, string routeName)
    {
        var trimmed = routeName.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var keys = CollectRouteKeys(packageRoot);
        if (keys.Count == 0)
        {
            return null;
        }

        var exact = keys.FirstOrDefault(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var contains = keys
            .Where(k => trimmed.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                        k.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
        if (contains is not null)
        {
            return contains;
        }

        var linePrefix = trimmed.Split('/', 2)[0].Trim();
        if (!string.IsNullOrEmpty(linePrefix))
        {
            return keys
                .Where(k => k.StartsWith(linePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();
        }

        return null;
    }

    private static HashSet<string> CollectRouteKeys(JsonObject packageRoot)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (packageRoot[OverviewsKey] is JsonObject overviews)
        {
            foreach (var key in overviews.Select(p => p.Key))
            {
                keys.Add(key);
            }
        }

        if (packageRoot["routePathDrafts"] is JsonObject drafts)
        {
            foreach (var key in drafts.Select(p => p.Key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static JsonObject? SimplifyDraftForOverview(JsonObject draft, string routeName)
    {
        var stopNodes = new JsonArray();
        if (draft["nodes"] is JsonArray nodes)
        {
            foreach (var node in nodes.OfType<JsonObject>())
            {
                var type = node["type"]?.GetValue<string>() ?? string.Empty;
                if (!string.Equals(type, "STOP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lat = node["lat"]?.GetValue<double>() ?? double.NaN;
                var lon = node["lon"]?.GetValue<double>() ?? double.NaN;
                if (!double.IsFinite(lat) || !double.IsFinite(lon))
                {
                    continue;
                }

                stopNodes.Add(new JsonObject
                {
                    ["id"] = node["id"]?.DeepClone() ?? JsonValue.Create(Guid.NewGuid().ToString("N")),
                    ["type"] = "STOP",
                    ["title"] = node["title"]?.DeepClone() ?? JsonValue.Create("Haltestelle"),
                    ["lat"] = lat,
                    ["lon"] = lon
                });
            }
        }

        var segmentSnaps = SimplifySegmentSnaps(draft["segmentSnaps"]);
        var snappedShape = CopyLatLngArray(draft["snappedShape"]);
        var hasLine = (snappedShape?.Count ?? 0) >= 2 ||
                      segmentSnaps?.Any(s => s is JsonObject o && o["points"] is JsonArray pts && pts.Count >= 2) == true;
        if (!hasLine && stopNodes.Count == 0)
        {
            return null;
        }

        var overview = new JsonObject
        {
            ["routeName"] = routeName,
            ["routeLineColor"] = draft["routeLineColor"]?.DeepClone() ?? JsonValue.Create("#2196f3"),
            ["nodes"] = stopNodes
        };

        if (snappedShape is { Count: >= 2 })
        {
            overview["snappedShape"] = snappedShape;
        }

        if (segmentSnaps is { Count: > 0 })
        {
            overview["segmentSnaps"] = segmentSnaps;
        }

        if (draft["roadSnappedEdgeKeys"] is JsonArray edgeKeys)
        {
            overview["roadSnappedEdgeKeys"] = edgeKeys.DeepClone();
        }

        return overview;
    }

    private static JsonArray? SimplifySegmentSnaps(JsonNode? segmentSnapsNode)
    {
        if (segmentSnapsNode is not JsonArray segmentSnaps || segmentSnaps.Count == 0)
        {
            return null;
        }

        var result = new JsonArray();
        foreach (var snap in segmentSnaps.OfType<JsonObject>())
        {
            var points = CopyLatLngArray(snap["points"]);
            if (points is null || points.Count < 2)
            {
                continue;
            }

            result.Add(new JsonObject
            {
                ["fromNodeId"] = snap["fromNodeId"]?.DeepClone(),
                ["toNodeId"] = snap["toNodeId"]?.DeepClone(),
                ["points"] = points
            });
        }

        return result.Count > 0 ? result : null;
    }

    private static JsonArray? CopyLatLngArray(JsonNode? pointsNode)
    {
        if (pointsNode is not JsonArray points || points.Count == 0)
        {
            return null;
        }

        var result = new JsonArray();
        foreach (var point in points.OfType<JsonObject>())
        {
            var lat = point["lat"]?.GetValue<double>() ?? double.NaN;
            var lon = point["lon"]?.GetValue<double>() ?? double.NaN;
            if (!double.IsFinite(lat) || !double.IsFinite(lon))
            {
                continue;
            }

            result.Add(new JsonObject
            {
                ["lat"] = lat,
                ["lon"] = lon
            });
        }

        return result.Count > 0 ? result : null;
    }
}
