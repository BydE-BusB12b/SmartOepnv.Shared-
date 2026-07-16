using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Überträgt gesnappte Nav-Verbindungen aus einer Quell-Route in den Fahrweg der Ziel-Route (Knoten-IDs werden gemappt).
/// </summary>
public static class RoutePathNavReuseApplier
{
    public static int Apply(
        JsonObject packageRoot,
        string targetRouteKey,
        IList<RouteStopItem> targetStops,
        IEnumerable<RoutePathNavReuseCandidate> matches,
        Func<string, IList<RouteStopItem>>? getStopsForRoute = null)
    {
        if (matches is null)
        {
            return 0;
        }

        var targetDraft = RoutePathDraftRepository.LoadOrCreate(targetRouteKey, targetStops, packageRoot);
        var appliedEdges = 0;

        foreach (var match in matches.OrderBy(m => m.TargetFirstListIndex))
        {
            var sourceJson = RoutePathDraftRepository.TryGetDraftJson(packageRoot, match.SourceRouteKey);
            if (string.IsNullOrWhiteSpace(sourceJson))
            {
                continue;
            }

            RoutePathDraft sourceDraft;
            try
            {
                sourceDraft = RoutePathDraftSerializer.FromJson(sourceJson);
            }
            catch
            {
                continue;
            }

            var sourceStops = getStopsForRoute?.Invoke(match.SourceRouteKey) ?? targetStops;
            appliedEdges += CopyNavSlice(
                sourceDraft,
                sourceStops,
                match.SourceFirstListIndex,
                match.SourceLastListIndex,
                targetDraft,
                targetStops,
                match.TargetFirstListIndex,
                match.TargetLastListIndex);
        }

        if (appliedEdges > 0)
        {
            EnsureBusStraightGeometry(targetDraft);
            RoutePathDraftMutator.DeduplicateSegmentsByEdge(targetDraft);
            RoutePathDraftMutator.DeduplicateManeuversPerEdge(targetDraft);
            RoutePathDraftMutator.EnsureBusStraightEdgeKeys(targetDraft);
            foreach (var key in targetDraft.RoadSegmentPolylines.Keys.ToList())
            {
                RoutePathPolylineJoin.AlignSegmentEndpointsAtSharedNodes(targetDraft, key);
            }

            RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(targetDraft);
            RoutePathDraftRepository.SaveToPackage(packageRoot, targetDraft);
        }

        return appliedEdges;
    }

    internal static int CopyNavSlice(
        RoutePathDraft source,
        IList<RouteStopItem> sourceStops,
        int sourceFromListIdx,
        int sourceToListIdx,
        RoutePathDraft target,
        IList<RouteStopItem> targetStops,
        int targetFromListIdx,
        int targetToListIdx)
    {
        if (sourceFromListIdx > sourceToListIdx || targetFromListIdx > targetToListIdx)
        {
            return 0;
        }

        var span = sourceToListIdx - sourceFromListIdx;
        if (targetToListIdx - targetFromListIdx != span)
        {
            return 0;
        }

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var offset = 0; offset <= span; offset++)
        {
            var srcIdx = sourceFromListIdx + offset;
            var tgtIdx = targetFromListIdx + offset;
            idMap[$"stop_{srcIdx}"] = $"stop_{tgtIdx}";
            idMap[$"announcement_{srcIdx}"] = $"announcement_{tgtIdx}";
        }

        var segmentsToCopy = RoutePathNavReuseGraph.CollectSegmentsForCopy(
            source,
            sourceStops,
            sourceFromListIdx,
            sourceToListIdx);
        var applied = 0;

        foreach (var seg in segmentsToCopy)
        {
            if (!TryMapNode(source, target, idMap, seg.FromNodeId, out var newFrom) ||
                !TryMapNode(source, target, idMap, seg.ToNodeId, out var newTo))
            {
                continue;
            }

            var oldKey = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
            var newKey = RoutePathDraft.SegmentEdgeKey(newFrom, newTo);
            var hasNavData = RoutePathNavReuseGraph.HasNavData(source, seg);

            UpsertSegment(target, newFrom, newTo);
            if (!hasNavData)
            {
                continue;
            }

            if (source.RoadSnappedEdgeKeys.Contains(oldKey))
            {
                target.RoadSnappedEdgeKeys.Add(newKey);
            }

            if (source.RoadBusStraightEdgeKeys.Contains(oldKey))
            {
                target.RoadBusStraightEdgeKeys.Add(newKey);
            }

            if (source.RoadSegmentPolylines.TryGetValue(oldKey, out var polyline))
            {
                target.RoadSegmentPolylines[newKey] = polyline
                    .Select(p => new RoutePathLatLng { Lat = p.Lat, Lon = p.Lon })
                    .ToList();
            }

            if (source.RoadSegmentManeuvers.TryGetValue(oldKey, out var maneuvers))
            {
                target.RoadSegmentManeuvers[newKey] = maneuvers
                    .Select(m => new RoutePathSnapManeuver
                    {
                        DistanceM = m.DistanceM,
                        Instruction = m.Instruction,
                        CurrentStreet = m.CurrentStreet,
                        NextStreet = m.NextStreet,
                        NavSymbolType = m.NavSymbolType
                    })
                    .ToList();
            }

            applied++;
        }

        return applied;
    }

    private static bool TryMapNode(
        RoutePathDraft source,
        RoutePathDraft target,
        Dictionary<string, string> idMap,
        string sourceNodeId,
        out string mappedNodeId)
    {
        if (idMap.TryGetValue(sourceNodeId, out mappedNodeId!))
        {
            return true;
        }

        var sourceNode = source.Nodes.FirstOrDefault(n =>
            string.Equals(n.Id, sourceNodeId, StringComparison.Ordinal));
        if (sourceNode is null ||
            sourceNode.Type is not (RoutePathNodeType.AUTO_WAYPOINT or RoutePathNodeType.MANUAL_WAYPOINT))
        {
            mappedNodeId = string.Empty;
            return false;
        }

        var reuseId = $"reuse_{SanitizeReuseNodeId(sourceNodeId)}";
        if (target.Nodes.Any(n => string.Equals(n.Id, reuseId, StringComparison.Ordinal)))
        {
            mappedNodeId = reuseId;
            idMap[sourceNodeId] = mappedNodeId;
            return true;
        }

        mappedNodeId = reuseId;
        target.Nodes.Add(new RoutePathNode
        {
            Id = mappedNodeId,
            Type = sourceNode.Type,
            Title = sourceNode.Title,
            SourceStopName = sourceNode.SourceStopName,
            Lat = sourceNode.Lat,
            Lon = sourceNode.Lon
        });
        idMap[sourceNodeId] = mappedNodeId;
        return true;
    }

    private static string SanitizeReuseNodeId(string sourceNodeId)
    {
        var cleaned = new string(sourceNodeId
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? Guid.NewGuid().ToString("N") : cleaned;
    }

    private static void EnsureBusStraightGeometry(RoutePathDraft draft)
    {
        foreach (var key in draft.RoadBusStraightEdgeKeys.ToList())
        {
            if (draft.RoadSegmentPolylines.TryGetValue(key, out var poly) && poly.Count >= 2)
            {
                continue;
            }

            var parts = key.Split('\u0001', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var segment = draft.Segments.FirstOrDefault(s =>
                string.Equals(s.FromNodeId, parts[0], StringComparison.Ordinal) &&
                string.Equals(s.ToNodeId, parts[1], StringComparison.Ordinal));
            if (segment is null)
            {
                continue;
            }

            RoutePathBusLaneHelper.ApplyBusStraightToSegment(draft, segment, preserveExistingManeuvers: true);
        }
    }

    private static void UpsertSegment(RoutePathDraft draft, string from, string to)
    {
        if (draft.Segments.Any(s =>
                string.Equals(s.FromNodeId, from, StringComparison.Ordinal) &&
                string.Equals(s.ToNodeId, to, StringComparison.Ordinal)))
        {
            return;
        }

        draft.Segments.Add(new RoutePathSegment
        {
            Order = draft.Segments.Count + 1,
            FromNodeId = from,
            ToNodeId = to
        });
    }
}
