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

        // Unter kanonischem Schlüssel speichern und alle Alias-Keys entfernen,
        // sonst verwirft NormalizeDraftKeysToCanonical den neuen Stand zugunsten eines alten Keys.
        var canonical = RouteDisplayHelper.ToCanonicalRouteKey(draft.RouteName);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            canonical = draft.RouteName;
        }

        foreach (var key in drafts.Select(e => e.Key).ToList())
        {
            if (RouteDisplayHelper.RouteKeysMatch(key, draft.RouteName) ||
                RouteDisplayHelper.RouteKeysMatch(key, canonical))
            {
                drafts.Remove(key);
            }
        }

        draft.RouteName = canonical;
        drafts[canonical] = JsonValue.Create(RoutePathDraftSerializer.ToJson(draft));
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
        string? bestJson = null;
        var bestUpdatedAt = long.MinValue;
        foreach (var entry in drafts)
        {
            if (!RouteDisplayHelper.RouteKeysMatch(entry.Key, routeName))
            {
                continue;
            }

            var text = DraftNodeToJsonText(entry.Value);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var updatedAt = TryPeekUpdatedAtEpochMs(text);
            if (bestJson is null || updatedAt >= bestUpdatedAt)
            {
                bestJson = text;
                bestUpdatedAt = updatedAt;
            }
        }

        return bestJson;
    }

    /// <summary>
    /// Schreibt Drafts unter kanonischem Routenschlüssel (ohne führende Nullen in der Fahrtnummer).
    /// Bei Kollision gewinnt der neuere <c>updatedAtEpochMs</c>.
    /// </summary>
    public static void NormalizeDraftKeysToCanonical(JsonObject packageRoot)
    {
        if (packageRoot["routePathDrafts"] is not JsonObject drafts || drafts.Count == 0)
        {
            return;
        }

        var normalized = new JsonObject();
        var updatedAtByCanonical = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in drafts)
        {
            var text = DraftNodeToJsonText(entry.Value);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var canonical = RouteDisplayHelper.ToCanonicalRouteKey(entry.Key);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                canonical = entry.Key;
            }

            var updatedAt = TryPeekUpdatedAtEpochMs(text);
            if (normalized[canonical] is not null &&
                updatedAtByCanonical.TryGetValue(canonical, out var existingAt) &&
                existingAt > updatedAt)
            {
                continue;
            }

            normalized[canonical] = JsonValue.Create(text);
            updatedAtByCanonical[canonical] = updatedAt;
        }

        packageRoot["routePathDrafts"] = normalized;
    }

    private static void RefreshNodesFromStops(RoutePathDraft draft, IList<RouteStopItem> stops) =>
        RoutePathNodeRefresh.RefreshNodesFromStops(draft, stops);

    private static long TryPeekUpdatedAtEpochMs(string draftJson)
    {
        try
        {
            var node = JsonNode.Parse(draftJson) as JsonObject;
            return node?["updatedAtEpochMs"]?.GetValue<long>() ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
