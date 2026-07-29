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
        var direct = DraftNodeToJsonText(drafts[routeName]);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        // Auto-Fahrplan speicherte zeitweise „Fahrt: 0004“ statt „Fahrt: 4“
        foreach (var entry in drafts)
        {
            if (RouteDisplayHelper.RouteKeysMatch(entry.Key, routeName))
            {
                return DraftNodeToJsonText(entry.Value);
            }
        }

        return null;
    }

    /// <summary>
    /// Schreibt Drafts unter kanonischem Routenschlüssel (ohne führende Nullen in der Fahrtnummer).
    /// </summary>
    public static void NormalizeDraftKeysToCanonical(JsonObject packageRoot)
    {
        if (packageRoot["routePathDrafts"] is not JsonObject drafts || drafts.Count == 0)
        {
            return;
        }

        var normalized = new JsonObject();
        foreach (var entry in drafts)
        {
            var text = DraftNodeToJsonText(entry.Value);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var canonical = RouteDisplayHelper.ToCanonicalRouteKey(entry.Key);
            if (normalized[canonical] is null)
            {
                normalized[canonical] = JsonValue.Create(text);
            }
        }

        packageRoot["routePathDrafts"] = normalized;
    }

    private static void RefreshNodesFromStops(RoutePathDraft draft, IList<RouteStopItem> stops) =>
        RoutePathNodeRefresh.RefreshNodesFromStops(draft, stops);
}
