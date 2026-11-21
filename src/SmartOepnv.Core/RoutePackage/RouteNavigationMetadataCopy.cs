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

    public static bool HasNavigationData(JsonObject packageRoot, string routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return false;
        }

        var key = routeKey.Trim();
        if (!string.IsNullOrWhiteSpace(RoutePathDraftRepository.TryGetDraftJson(packageRoot, key)))
        {
            return true;
        }

        if (OfflineRouteGuidancePackageSync.HasGuidance(packageRoot, key))
        {
            return true;
        }

        if (packageRoot[LeitstelleRoutePathOverview.OverviewsKey] is JsonObject overviews &&
            overviews[key] is not null)
        {
            return true;
        }

        return false;
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
            // Vollersatz: Shape der Vorlage behalten, nur IDs/Reihenfolge bereinigen falls nötig
            var shapeBackup = draft.SnappedShape
                .Select(p => new RoutePathLatLng { Lat = p.Lat, Lon = p.Lon })
                .ToList();
            var maneuversBackup = draft.SnappedManeuvers
                .Select(m => new RoutePathSnapManeuver
                {
                    DistanceM = m.DistanceM,
                    Instruction = m.Instruction,
                    CurrentStreet = m.CurrentStreet,
                    NextStreet = m.NextStreet,
                    NavSymbolType = m.NavSymbolType
                })
                .ToList();

            RoutePathDraftRepair.NormalizeReuseNodeIds(draft);
            RoutePathDraftMutator.DeduplicateSegmentsByEdge(draft);
            RoutePathDraftRepair.ReorderSegmentsAsSinglePath(draft);

            if (shapeBackup.Count >= 2 &&
                RoutePathDraftIntegrity.Evaluate(
                    new RoutePathDraft
                    {
                        RouteName = draft.RouteName,
                        Nodes = draft.Nodes,
                        Segments = draft.Segments,
                        SnappedShape = shapeBackup
                    }).Count == 0)
            {
                draft.SnappedShape = shapeBackup;
                draft.SnappedManeuvers = maneuversBackup;
            }
            else
            {
                RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(draft);
            }

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
