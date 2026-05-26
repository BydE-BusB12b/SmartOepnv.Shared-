using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;
using static SmartOepnv.Core.JsonNodeExtensions;

namespace SmartOepnv.Core.RoutePath;

public static class RoutePathDraftRepository
{
    public static RoutePathDraft LoadOrCreate(string routeName, IList<RouteStopItem> stops, JsonObject? packageRoot)
    {
        var existingJson = TryGetDraftJson(packageRoot, routeName);
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            var draft = RoutePathDraftSerializer.FromJson(existingJson);
            draft.RouteName = routeName;
            RefreshNodesFromStops(draft, stops);
            return draft;
        }

        return RoutePathDraftBuilder.CreateSeed(routeName, stops);
    }

    public static void SaveToPackage(JsonObject packageRoot, RoutePathDraft draft)
    {
        draft.UpdatedAtEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (packageRoot["routePathDrafts"] is not JsonObject drafts)
        {
            drafts = new JsonObject();
            packageRoot["routePathDrafts"] = drafts;
        }
        drafts[draft.RouteName] = JsonValue.Create(RoutePathDraftSerializer.ToJson(draft));
    }

    public static string? TryGetDraftJson(JsonObject? packageRoot, string routeName)
    {
        if (packageRoot?["routePathDrafts"] is not JsonObject drafts) return null;
        return DraftNodeToJsonText(drafts[routeName]);
    }

    private static void RefreshNodesFromStops(RoutePathDraft draft, IList<RouteStopItem> stops)
    {
        var seeded = RoutePathDraftBuilder.BuildSeedNodes(stops);
        var preserved = draft.Nodes
            .Where(n => n.Type is RoutePathNodeType.AUTO_WAYPOINT or RoutePathNodeType.MANUAL_WAYPOINT)
            .ToList();
        draft.Nodes = seeded.Concat(preserved).ToList();
        var validIds = draft.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        draft.Segments = draft.Segments
            .Where(s => validIds.Contains(s.FromNodeId) && validIds.Contains(s.ToNodeId))
            .OrderBy(s => s.Order)
            .Select((s, idx) => new RoutePathSegment { Order = idx + 1, FromNodeId = s.FromNodeId, ToNodeId = s.ToNodeId })
            .ToList();
    }
}
