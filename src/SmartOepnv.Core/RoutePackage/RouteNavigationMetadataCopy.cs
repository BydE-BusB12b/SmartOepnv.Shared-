using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePath;
using static SmartOepnv.Core.JsonNodeExtensions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Kopiert Navidaten (Fahrweg-Entwurf, Offline-Linienführung) bei Routen-Duplikaten.
/// </summary>
public static class RouteNavigationMetadataCopy
{
    public static void CopyForRoute(
        JsonObject packageRoot,
        string sourceRouteKey,
        string targetRouteKey)
    {
        if (string.IsNullOrWhiteSpace(sourceRouteKey) ||
            string.IsNullOrWhiteSpace(targetRouteKey))
        {
            return;
        }

        var source = sourceRouteKey.Trim();
        var target = targetRouteKey.Trim();
        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return;
        }

        CopyPathDraft(packageRoot, source, target);
        CopyOfflineGuidance(packageRoot, source, target);
        CopyRawKeyedEntry(packageRoot, LeitstelleRoutePathOverview.OverviewsKey, source, target);
    }

    private static void CopyPathDraft(JsonObject root, string source, string target)
    {
        var json = RoutePathDraftRepository.TryGetDraftJson(root, source);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var draft = RoutePathDraftCloner.Clone(RoutePathDraftSerializer.FromJson(json));
            draft.RouteName = target;
            draft.UpdatedAtEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            RoutePathDraftRepository.SaveToPackage(root, draft);
        }
        catch
        {
            CopyRawKeyedEntry(root, "routePathDrafts", source, target);
        }
    }

    private static void CopyOfflineGuidance(JsonObject root, string source, string target)
    {
        if (root["routeOfflineGuidance"] is not JsonObject block)
        {
            return;
        }

        var text = DraftNodeToJsonText(block[source]);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var guidance = OfflineRouteGuidanceJson.FromJson(text);
        if (guidance is null)
        {
            CopyRawKeyedEntry(root, "routeOfflineGuidance", source, target);
            return;
        }

        OfflineRouteGuidancePackageSync.Save(
            root,
            target,
            new OfflineRouteGuidance
            {
                Version = guidance.Version,
                RouteKey = target,
                Polyline = guidance.Polyline,
                Maneuvers = guidance.Maneuvers,
                TotalLengthMeters = guidance.TotalLengthMeters
            });
    }

    private static void CopyRawKeyedEntry(
        JsonObject root,
        string propertyName,
        string source,
        string target)
    {
        if (root[propertyName] is not JsonObject block)
        {
            return;
        }

        var text = DraftNodeToJsonText(block[source]);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        block[target] = JsonValue.Create(text);
    }
}
