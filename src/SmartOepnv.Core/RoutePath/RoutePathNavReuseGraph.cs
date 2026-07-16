using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Ermittelt Nav-Kanten entlang des Fahrwegs zwischen Listen-Indizes (inkl. Wegpunkte, Busspuren).
/// </summary>
internal static class RoutePathNavReuseGraph
{
    private const int MaxPathSegments = 48;

    internal static IReadOnlyList<RoutePathSegment> CollectSnappedSegmentsBetween(
        RoutePathDraft draft,
        IList<RouteStopItem> stops,
        int fromListIdx,
        int toListIdx) =>
        CollectSegmentsForCopy(draft, stops, fromListIdx, toListIdx)
            .Where(seg => HasNavData(draft, seg))
            .ToList();

    internal static IReadOnlyList<RoutePathSegment> CollectSegmentsForCopy(
        RoutePathDraft draft,
        IList<RouteStopItem> stops,
        int fromListIdx,
        int toListIdx)
    {
        if (fromListIdx >= toListIdx)
        {
            return [];
        }

        var nodeById = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var collected = new List<RoutePathSegment>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (legFrom, legTo) in BuildLegPairs(stops, fromListIdx, toListIdx))
        {
            var legPath = FindBestLegPath(draft, nodeById, legFrom, legTo);
            foreach (var seg in legPath)
            {
                TryAddSegment(seg, collected, seenKeys);
            }
        }

        AppendTerminalNavSegments(draft, nodeById, fromListIdx, toListIdx, collected, seenKeys);
        AppendNavSegmentsInReachableSubgraph(draft, nodeById, fromListIdx, toListIdx, collected, seenKeys);

        return collected;
    }

    internal static int CountSnappedSegmentsBetween(
        RoutePathDraft draft,
        IList<RouteStopItem> stops,
        int fromListIdx,
        int toListIdx) =>
        CollectSnappedSegmentsBetween(draft, stops, fromListIdx, toListIdx).Count;

    private static void AppendTerminalNavSegments(
        RoutePathDraft draft,
        IReadOnlyDictionary<string, RoutePathNode> nodeById,
        int fromListIdx,
        int toListIdx,
        List<RoutePathSegment> collected,
        HashSet<string> seenKeys)
    {
        var endNodes = new HashSet<string>(StringComparer.Ordinal)
        {
            $"stop_{toListIdx}",
            $"announcement_{toListIdx}"
        };

        foreach (var seg in draft.Segments)
        {
            if (!endNodes.Contains(seg.ToNodeId) || !HasNavData(draft, seg))
            {
                continue;
            }

            if (!IsNodeWithinSlice(nodeById, seg.FromNodeId, fromListIdx, toListIdx))
            {
                continue;
            }

            TryAddSegment(seg, collected, seenKeys);
        }
    }

    private static void AppendNavSegmentsInReachableSubgraph(
        RoutePathDraft draft,
        IReadOnlyDictionary<string, RoutePathNode> nodeById,
        int fromListIdx,
        int toListIdx,
        List<RoutePathSegment> collected,
        HashSet<string> seenKeys)
    {
        var reachable = BuildReachableNodes(draft, nodeById, fromListIdx, toListIdx);
        foreach (var seg in draft.Segments)
        {
            if (!reachable.Contains(seg.FromNodeId) || !reachable.Contains(seg.ToNodeId))
            {
                continue;
            }

            if (!HasNavData(draft, seg))
            {
                continue;
            }

            TryAddSegment(seg, collected, seenKeys);
        }
    }

    private static HashSet<string> BuildReachableNodes(
        RoutePathDraft draft,
        IReadOnlyDictionary<string, RoutePathNode> nodeById,
        int fromListIdx,
        int toListIdx)
    {
        var seeds = new Queue<string>();
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        for (var i = fromListIdx; i <= toListIdx; i++)
        {
            foreach (var id in new[] { $"stop_{i}", $"announcement_{i}" })
            {
                if (!nodeById.ContainsKey(id))
                {
                    continue;
                }

                seeds.Enqueue(id);
                reachable.Add(id);
            }
        }

        while (seeds.Count > 0)
        {
            var nodeId = seeds.Dequeue();
            foreach (var seg in draft.Segments)
            {
                string? other = null;
                if (string.Equals(seg.FromNodeId, nodeId, StringComparison.Ordinal))
                {
                    other = seg.ToNodeId;
                }
                else if (string.Equals(seg.ToNodeId, nodeId, StringComparison.Ordinal))
                {
                    other = seg.FromNodeId;
                }

                if (other is null || reachable.Contains(other))
                {
                    continue;
                }

                if (!IsNodeWithinSlice(nodeById, other, fromListIdx, toListIdx))
                {
                    continue;
                }

                reachable.Add(other);
                seeds.Enqueue(other);
            }
        }

        return reachable;
    }

    private static void TryAddSegment(
        RoutePathSegment seg,
        List<RoutePathSegment> collected,
        HashSet<string> seenKeys)
    {
        var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
        if (seenKeys.Add(key))
        {
            collected.Add(seg);
        }
    }

    private static bool IsNodeWithinSlice(
        IReadOnlyDictionary<string, RoutePathNode> nodeById,
        string nodeId,
        int fromListIdx,
        int toListIdx)
    {
        if (TryParseListIndex(nodeId, out var idx))
        {
            return idx >= fromListIdx && idx <= toListIdx;
        }

        return nodeById.TryGetValue(nodeId, out var node) &&
               node.Type is RoutePathNodeType.AUTO_WAYPOINT or RoutePathNodeType.MANUAL_WAYPOINT;
    }

    internal static IEnumerable<(int From, int To)> BuildLegPairs(
        IList<RouteStopItem> stops,
        int fromListIdx,
        int toListIdx)
    {
        var anchors = BuildAnchorIndices(stops, fromListIdx, toListIdx);
        if (anchors.Count >= 2)
        {
            for (var i = 0; i < anchors.Count - 1; i++)
            {
                yield return (anchors[i], anchors[i + 1]);
            }

            yield break;
        }

        for (var legFrom = fromListIdx; legFrom < toListIdx; legFrom++)
        {
            yield return (legFrom, legFrom + 1);
        }
    }

    internal static List<int> BuildAnchorIndices(IList<RouteStopItem> stops, int fromListIdx, int toListIdx)
    {
        var anchors = new List<int>();
        for (var i = fromListIdx; i <= toListIdx && i < stops.Count; i++)
        {
            if (!stops[i].IsWaypoint)
            {
                anchors.Add(i);
            }
        }

        return anchors;
    }

    internal static bool IsSnapped(RoutePathDraft draft, RoutePathSegment segment)
    {
        var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
        return draft.RoadSnappedEdgeKeys.Contains(key) ||
               draft.RoadSegmentPolylines.ContainsKey(key);
    }

    internal static bool HasNavData(RoutePathDraft draft, RoutePathSegment segment)
    {
        var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
        return draft.RoadSnappedEdgeKeys.Contains(key) ||
               draft.RoadBusStraightEdgeKeys.Contains(key) ||
               draft.RoadSegmentPolylines.ContainsKey(key) ||
               draft.RoadSegmentManeuvers.ContainsKey(key);
    }

    private static List<RoutePathSegment> FindBestLegPath(
        RoutePathDraft draft,
        IReadOnlyDictionary<string, RoutePathNode> nodeById,
        int fromListIdx,
        int toListIdx)
    {
        var startNodes = new[]
            {
                $"stop_{fromListIdx}",
                $"announcement_{fromListIdx}"
            }
            .Where(id => nodeById.ContainsKey(id))
            .ToList();

        if (startNodes.Count == 0)
        {
            return [];
        }

        var goalNodes = new HashSet<string>(StringComparer.Ordinal);
        var stopGoal = $"stop_{toListIdx}";
        var annGoal = $"announcement_{toListIdx}";
        if (nodeById.ContainsKey(stopGoal))
        {
            goalNodes.Add(stopGoal);
        }

        if (nodeById.ContainsKey(annGoal))
        {
            goalNodes.Add(annGoal);
        }

        if (goalNodes.Count == 0)
        {
            return [];
        }

        var queue = new Queue<(string NodeId, List<RoutePathSegment> Path)>();
        List<RoutePathSegment>? bestPath = null;
        var bestNavScore = -1;
        var bestEndsAtStop = false;

        foreach (var start in startNodes)
        {
            queue.Enqueue((start, []));
        }

        while (queue.Count > 0)
        {
            var (nodeId, path) = queue.Dequeue();
            if (path.Count > MaxPathSegments)
            {
                continue;
            }

            if (goalNodes.Contains(nodeId) && path.Count > 0)
            {
                var navScore = path.Count(seg => HasNavData(draft, seg));
                var endsAtStop = string.Equals(nodeId, stopGoal, StringComparison.Ordinal);
                if (navScore > bestNavScore ||
                    (navScore == bestNavScore && endsAtStop && !bestEndsAtStop))
                {
                    bestNavScore = navScore;
                    bestEndsAtStop = endsAtStop;
                    bestPath = path;
                }

                continue;
            }

            var visitedOnPath = BuildVisitedOnPath(path, nodeId);
            foreach (var (seg, forward) in IncidentSegments(draft, nodeId))
            {
                var nextNodeId = forward ? seg.ToNodeId : seg.FromNodeId;
                if (!CanTraverseTo(nodeById, nextNodeId, fromListIdx, toListIdx, goalNodes))
                {
                    continue;
                }

                if (visitedOnPath.Contains(nextNodeId))
                {
                    continue;
                }

                var nextPath = new List<RoutePathSegment>(path) { seg };
                queue.Enqueue((nextNodeId, nextPath));
            }
        }

        return bestPath ?? [];
    }

    private static IEnumerable<(RoutePathSegment Segment, bool Forward)> IncidentSegments(
        RoutePathDraft draft,
        string nodeId)
    {
        foreach (var seg in draft.Segments)
        {
            if (string.Equals(seg.FromNodeId, nodeId, StringComparison.Ordinal))
            {
                yield return (seg, true);
            }
            else if (string.Equals(seg.ToNodeId, nodeId, StringComparison.Ordinal))
            {
                yield return (seg, false);
            }
        }
    }

    private static HashSet<string> BuildVisitedOnPath(IReadOnlyList<RoutePathSegment> path, string currentNodeId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { currentNodeId };
        foreach (var seg in path)
        {
            visited.Add(seg.FromNodeId);
            visited.Add(seg.ToNodeId);
        }

        return visited;
    }

    private static bool CanTraverseTo(
        IReadOnlyDictionary<string, RoutePathNode> nodeById,
        string toNodeId,
        int fromListIdx,
        int toListIdx,
        IReadOnlySet<string> goalNodes)
    {
        if (goalNodes.Contains(toNodeId))
        {
            return true;
        }

        if (TryParseListIndex(toNodeId, out var idx))
        {
            return idx > fromListIdx && idx < toListIdx;
        }

        return nodeById.TryGetValue(toNodeId, out var node) &&
               node.Type is RoutePathNodeType.AUTO_WAYPOINT or RoutePathNodeType.MANUAL_WAYPOINT;
    }

    internal static bool TryParseListIndex(string nodeId, out int index)
    {
        index = -1;
        if (!nodeId.StartsWith("stop_", StringComparison.Ordinal) &&
            !nodeId.StartsWith("announcement_", StringComparison.Ordinal))
        {
            return false;
        }

        var lastUnderscore = nodeId.LastIndexOf('_');
        if (lastUnderscore < 0 || lastUnderscore >= nodeId.Length - 1)
        {
            return false;
        }

        return int.TryParse(
            nodeId[(lastUnderscore + 1)..],
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out index);
    }
}
