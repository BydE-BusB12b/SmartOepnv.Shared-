namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Bereinigt Fahrwege nach Nav-Übernahme/Kopieren:
/// doppelte <c>reuse_</c>-Präfixe, logische Doppelkanten, eine Hauptkette ohne Rest-Segmente,
/// überlange Segment-Polylines, Shape-Neubau.
/// </summary>
public static class RoutePathDraftRepair
{
    /// <summary>Segment-Polyline länger als Luftlinie × Faktor → Snap verwerfen.</summary>
    private const double MaxPolylineToAirRatio = 2.8;

    /// <summary>Zusätzlicher Puffer (m) über der Luftlinie je Segment.</summary>
    private const double MaxPolylineExtraMeters = 2_000;

    /// <summary>
    /// <c>reuse_reuse_manual_…</c> → <c>reuse_manual_…</c> (Knoten, Segmente, Edge-Keys).
    /// </summary>
    public static int NormalizeReuseNodeIds(RoutePathDraft draft)
    {
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in draft.Nodes)
        {
            var normalized = CollapseReusePrefix(node.Id);
            if (!string.Equals(normalized, node.Id, StringComparison.Ordinal))
            {
                idMap[node.Id] = normalized;
            }
        }

        if (idMap.Count == 0)
        {
            return 0;
        }

        foreach (var node in draft.Nodes.ToList())
        {
            if (!idMap.TryGetValue(node.Id, out var newId))
            {
                continue;
            }

            var existing = draft.Nodes.FirstOrDefault(n =>
                !ReferenceEquals(n, node) &&
                string.Equals(n.Id, newId, StringComparison.Ordinal));
            if (existing is not null)
            {
                draft.Nodes.Remove(node);
                continue;
            }

            node.Id = newId;
        }

        RemapSegmentsAndKeys(draft, id => idMap.TryGetValue(id, out var m) ? m : id);
        return idMap.Count;
    }

    /// <summary>
    /// Entfernt parallele Kanten, die sich nur durch <c>reuse_</c>-Präfixe unterscheiden.
    /// </summary>
    public static void DeduplicateLogicalEdges(RoutePathDraft draft)
    {
        if (draft.Segments.Count <= 1)
        {
            return;
        }

        var kept = new List<RoutePathSegment>();
        foreach (var group in draft.Segments.GroupBy(
                     s => LogicalEdgeKey(s.FromNodeId, s.ToNodeId),
                     StringComparer.Ordinal))
        {
            var list = group.ToList();
            var best = list
                .OrderByDescending(s => HasSnap(draft, s) ? 1 : 0)
                .ThenByDescending(s => HasBus(draft, s) ? 1 : 0)
                .ThenBy(s => CountReusePrefix(s.FromNodeId) + CountReusePrefix(s.ToNodeId))
                .ThenByDescending(s => s.Order)
                .First();

            // Snap-Daten von Alternativen übernehmen, falls Best noch keine hat.
            var bestKey = RoutePathDraft.SegmentEdgeKey(best.FromNodeId, best.ToNodeId);
            foreach (var other in list.Where(s => !ReferenceEquals(s, best)))
            {
                var otherKey = RoutePathDraft.SegmentEdgeKey(other.FromNodeId, other.ToNodeId);
                if (string.Equals(bestKey, otherKey, StringComparison.Ordinal))
                {
                    continue;
                }

                MigrateSnapIfMissing(draft, otherKey, bestKey);
            }

            kept.Add(best);
        }

        draft.Segments = kept
            .OrderBy(s => s.Order)
            .Select((s, idx) => new RoutePathSegment
            {
                Order = idx + 1,
                FromNodeId = s.FromNodeId,
                ToNodeId = s.ToNodeId
            })
            .ToList();
    }

    /// <summary>
    /// Ordnet Segmente als eine Kette ab dem Startknoten; Rest-Zweige werden verworfen
    /// (früher angehängt → Shape verdoppelt sich).
    /// </summary>
    public static void ReorderSegmentsAsSinglePath(RoutePathDraft draft)
    {
        if (draft.Segments.Count <= 1)
        {
            return;
        }

        var outgoing = draft.Segments
            .GroupBy(s => s.FromNodeId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var start = PreferPathStartNodeId(draft);
        if (start is null)
        {
            return;
        }

        var nodeMap = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var ordered = new List<RoutePathSegment>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var current = start;
        var guard = 0;
        while (guard++ < draft.Segments.Count + 2)
        {
            if (!outgoing.TryGetValue(current, out var outs) || outs.Count == 0)
            {
                break;
            }

            var candidates = outs
                .Where(s => !used.Contains(RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId)))
                .ToList();
            if (candidates.Count == 0)
            {
                break;
            }

            var next = PickShortestContinuation(draft, nodeMap, candidates);
            var key = RoutePathDraft.SegmentEdgeKey(next.FromNodeId, next.ToNodeId);
            used.Add(key);
            ordered.Add(next);
            current = next.ToNodeId;
        }

        // Lücken schließen: kurze Restkanten dürfen folgen; lange Zweige (Nav-Müll) nicht.
        var stopChain = StopChainLengthMeters(draft);
        var leftoverBudget = Math.Max(800, stopChain * 0.2);
        foreach (var seg in draft.Segments.OrderBy(s => s.Order))
        {
            var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
            if (!used.Add(key))
            {
                continue;
            }

            var len = EstimateEdgeLengthMeters(draft, nodeMap, seg);
            if (len > leftoverBudget)
            {
                used.Remove(key);
                continue;
            }

            leftoverBudget -= len;
            ordered.Add(seg);
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i + 1;
        }

        draft.Segments = ordered;
    }

    /// <summary>
    /// Verwirft Segment-Polylines, die deutlich länger sind als die Luftlinie der Endpunkte
    /// (typisch nach Nav-Übernahme einer anderen Route).
    /// </summary>
    public static int ClearBloatedEdgePolylines(RoutePathDraft draft)
    {
        var nodeMap = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var cleared = 0;
        foreach (var seg in draft.Segments)
        {
            var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
            if (!draft.RoadSegmentPolylines.TryGetValue(key, out var poly) || poly.Count < 2)
            {
                continue;
            }

            if (!nodeMap.TryGetValue(seg.FromNodeId, out var from) ||
                !nodeMap.TryGetValue(seg.ToNodeId, out var to))
            {
                continue;
            }

            var air = RoutePathGeo.HaversineMeters(
                new RoutePathLatLng { Lat = from.Lat, Lon = from.Lon },
                new RoutePathLatLng { Lat = to.Lat, Lon = to.Lon });
            if (air < 30)
            {
                continue;
            }

            var path = RoutePathGeo.PathLengthMeters(poly);
            var limit = Math.Max(air * MaxPolylineToAirRatio, air + MaxPolylineExtraMeters);
            if (path <= limit)
            {
                continue;
            }

            RoutePathDraftMutator.ClearEdgeSnap(draft, key);
            cleared++;
        }

        return cleared;
    }

    /// <summary>
    /// Volle Bereinigung inkl. Shape-Neubau. true = Integritätsprüfung danach ok.
    /// </summary>
    public static bool TryRepair(RoutePathDraft draft)
    {
        NormalizeReuseNodeIds(draft);
        PruneFarManualWaypoints(draft);
        RoutePathDraftMutator.DeduplicateSegmentsByEdge(draft);
        DeduplicateLogicalEdges(draft);
        ReorderSegmentsAsSinglePath(draft);
        ClearBloatedEdgePolylines(draft);
        RoutePathDraftMutator.PruneOrphanEdgeSnaps(draft);
        RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(draft);

        // Falls Shape weiter zu lang: überlange Kanten strippen und Shape neu bauen.
        if (RoutePathDraftIntegrity.Evaluate(draft).Any(f => f.Code == "SHAPE_TOO_LONG"))
        {
            ClearBloatedEdgePolylines(draft);
            ClearSnapsThatInflateTotal(draft);
            RoutePathDraftMutator.PruneOrphanEdgeSnaps(draft);
            RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(draft);
        }

        return RoutePathDraftIntegrity.Evaluate(draft).Count == 0;
    }

    /// <summary>
    /// Manual-/reuse-Wegpunkte weit abseits der Halteliste (Nav von anderer Route) entfernen.
    /// </summary>
    public static int PruneFarManualWaypoints(RoutePathDraft draft, double maxDistFromNearestStopMeters = 3_000)
    {
        var stops = draft.Nodes.Where(n => n.Type == RoutePathNodeType.STOP).ToList();
        if (stops.Count == 0)
        {
            return 0;
        }

        var farIds = draft.Nodes
            .Where(n => n.Type == RoutePathNodeType.MANUAL_WAYPOINT)
            .Where(n =>
            {
                var min = stops.Min(s => RoutePathGeo.HaversineMeters(
                    new RoutePathLatLng { Lat = n.Lat, Lon = n.Lon },
                    new RoutePathLatLng { Lat = s.Lat, Lon = s.Lon }));
                return min > maxDistFromNearestStopMeters;
            })
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (farIds.Count == 0)
        {
            return 0;
        }

        draft.Nodes.RemoveAll(n => farIds.Contains(n.Id));
        draft.Segments = draft.Segments
            .Where(s => !farIds.Contains(s.FromNodeId) && !farIds.Contains(s.ToNodeId))
            .OrderBy(s => s.Order)
            .Select((s, idx) => new RoutePathSegment
            {
                Order = idx + 1,
                FromNodeId = s.FromNodeId,
                ToNodeId = s.ToNodeId
            })
            .ToList();
        RoutePathDraftMutator.ClearEdgesTouchingNodes(draft, farIds);
        return farIds.Count;
    }

    public static string CollapseReusePrefix(string nodeId)
    {
        var id = nodeId.Trim();
        while (CountReusePrefix(id) > 1)
        {
            id = id["reuse_".Length..];
        }

        return id;
    }

    private static void ClearSnapsThatInflateTotal(RoutePathDraft draft)
    {
        var stopChain = StopChainLengthMeters(draft);
        if (stopChain < 50)
        {
            return;
        }

        var limit = Math.Max(
            stopChain * RoutePathDraftIntegrity.MaxShapeToStopChainRatio,
            stopChain + RoutePathDraftIntegrity.MaxShapeExtraMeters);

        var nodeMap = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        double CurrentShapeEstimate()
        {
            var sum = 0.0;
            foreach (var s in draft.Segments)
            {
                sum += EstimateEdgeLengthMeters(draft, nodeMap, s);
            }

            return sum;
        }

        var ranked = draft.Segments
            .Select(s =>
            {
                var key = RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId);
                var polyLen = draft.RoadSegmentPolylines.TryGetValue(key, out var poly) && poly.Count >= 2
                    ? RoutePathGeo.PathLengthMeters(poly)
                    : 0.0;
                var air = 0.0;
                if (nodeMap.TryGetValue(s.FromNodeId, out var from) &&
                    nodeMap.TryGetValue(s.ToNodeId, out var to))
                {
                    air = RoutePathGeo.HaversineMeters(
                        new RoutePathLatLng { Lat = from.Lat, Lon = from.Lon },
                        new RoutePathLatLng { Lat = to.Lat, Lon = to.Lon });
                }

                return (Key: key, Excess: Math.Max(0, polyLen - Math.Max(air, 1)));
            })
            .Where(x => x.Excess > 200)
            .OrderByDescending(x => x.Excess)
            .ToList();

        foreach (var (key, _) in ranked)
        {
            if (CurrentShapeEstimate() <= limit)
            {
                break;
            }

            if (!draft.RoadSegmentPolylines.ContainsKey(key))
            {
                continue;
            }

            RoutePathDraftMutator.ClearEdgeSnap(draft, key);
        }
    }

    private static double StopChainLengthMeters(RoutePathDraft draft)
    {
        var stops = draft.Nodes
            .Where(n => n.Type == RoutePathNodeType.STOP)
            .OrderBy(n =>
            {
                const string prefix = "stop_";
                if (n.Id.StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(n.Id.AsSpan(prefix.Length), out var idx))
                {
                    return idx;
                }

                return int.MaxValue;
            })
            .ToList();

        if (stops.Count < 2)
        {
            return 0;
        }

        var sum = 0.0;
        for (var i = 1; i < stops.Count; i++)
        {
            sum += RoutePathGeo.HaversineMeters(
                new RoutePathLatLng { Lat = stops[i - 1].Lat, Lon = stops[i - 1].Lon },
                new RoutePathLatLng { Lat = stops[i].Lat, Lon = stops[i].Lon });
        }

        return sum;
    }

    private static RoutePathSegment PickShortestContinuation(
        RoutePathDraft draft,
        IReadOnlyDictionary<string, RoutePathNode> nodeMap,
        List<RoutePathSegment> candidates)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        return candidates
            .OrderBy(s => EstimateEdgeLengthMeters(draft, nodeMap, s))
            .ThenBy(s => TargetProgressIndex(s.ToNodeId))
            .First();
    }

    private static double EstimateEdgeLengthMeters(
        RoutePathDraft draft,
        IReadOnlyDictionary<string, RoutePathNode> nodeMap,
        RoutePathSegment seg)
    {
        var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
        if (draft.RoadSegmentPolylines.TryGetValue(key, out var poly) && poly.Count >= 2)
        {
            return RoutePathGeo.PathLengthMeters(poly);
        }

        if (nodeMap.TryGetValue(seg.FromNodeId, out var from) &&
            nodeMap.TryGetValue(seg.ToNodeId, out var to))
        {
            return RoutePathGeo.HaversineMeters(
                new RoutePathLatLng { Lat = from.Lat, Lon = from.Lon },
                new RoutePathLatLng { Lat = to.Lat, Lon = to.Lon });
        }

        return double.MaxValue;
    }

    private static int TargetProgressIndex(string nodeId)
    {
        if (nodeId.StartsWith("stop_", StringComparison.Ordinal) &&
            int.TryParse(nodeId.AsSpan("stop_".Length), out var stopIdx))
        {
            return stopIdx;
        }

        if (nodeId.StartsWith("announcement_", StringComparison.Ordinal) &&
            int.TryParse(nodeId.AsSpan("announcement_".Length), out var annIdx))
        {
            return annIdx;
        }

        return int.MaxValue / 2;
    }

    private static string LogicalEdgeKey(string fromId, string toId) =>
        $"{StripAllReusePrefixes(fromId)}\u0001{StripAllReusePrefixes(toId)}";

    private static string StripAllReusePrefixes(string nodeId)
    {
        var id = nodeId.Trim();
        while (id.StartsWith("reuse_", StringComparison.OrdinalIgnoreCase))
        {
            id = id["reuse_".Length..];
        }

        return id;
    }

    private static bool HasSnap(RoutePathDraft draft, RoutePathSegment s) =>
        draft.RoadSnappedEdgeKeys.Contains(RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId)) ||
        (draft.RoadSegmentPolylines.TryGetValue(
             RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId), out var poly) &&
         poly.Count >= 2);

    private static bool HasBus(RoutePathDraft draft, RoutePathSegment s) =>
        draft.RoadBusStraightEdgeKeys.Contains(RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId));

    private static void MigrateSnapIfMissing(RoutePathDraft draft, string fromKey, string toKey)
    {
        if (!draft.RoadSegmentPolylines.ContainsKey(toKey) &&
            draft.RoadSegmentPolylines.TryGetValue(fromKey, out var poly))
        {
            draft.RoadSegmentPolylines[toKey] = poly;
        }

        if (!draft.RoadSegmentManeuvers.ContainsKey(toKey) &&
            draft.RoadSegmentManeuvers.TryGetValue(fromKey, out var mans))
        {
            draft.RoadSegmentManeuvers[toKey] = mans;
        }

        if (draft.RoadSnappedEdgeKeys.Contains(fromKey))
        {
            draft.RoadSnappedEdgeKeys.Add(toKey);
        }

        if (draft.RoadBusStraightEdgeKeys.Contains(fromKey))
        {
            draft.RoadBusStraightEdgeKeys.Add(toKey);
        }
    }

    private static int CountReusePrefix(string id)
    {
        var n = 0;
        var s = id;
        while (s.StartsWith("reuse_", StringComparison.OrdinalIgnoreCase))
        {
            n++;
            s = s["reuse_".Length..];
        }

        return n;
    }

    private static string? PreferPathStartNodeId(RoutePathDraft draft)
    {
        var ids = draft.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in new[] { "announcement_0", "stop_0" })
        {
            if (ids.Contains(candidate) &&
                draft.Segments.Any(s => s.FromNodeId == candidate))
            {
                return candidate;
            }
        }

        var withOutgoing = draft.Segments.Select(s => s.FromNodeId).ToHashSet(StringComparer.Ordinal);
        var withIncoming = draft.Segments.Select(s => s.ToNodeId).ToHashSet(StringComparer.Ordinal);
        var roots = withOutgoing.Where(id => !withIncoming.Contains(id)).OrderBy(id => id).ToList();
        return roots.FirstOrDefault() ?? draft.Segments.OrderBy(s => s.Order).FirstOrDefault()?.FromNodeId;
    }

    private static void RemapSegmentsAndKeys(RoutePathDraft draft, Func<string, string> mapId)
    {
        draft.Segments = draft.Segments
            .Select(s => new RoutePathSegment
            {
                Order = s.Order,
                FromNodeId = mapId(s.FromNodeId),
                ToNodeId = mapId(s.ToNodeId)
            })
            .ToList();

        RemapDict(draft.RoadSegmentPolylines, mapId);
        RemapDict(draft.RoadSegmentManeuvers, mapId);
        RemapSet(draft.RoadSnappedEdgeKeys, mapId);
        RemapSet(draft.RoadBusStraightEdgeKeys, mapId);
    }

    private static void RemapDict<T>(Dictionary<string, T> dict, Func<string, string> mapId)
    {
        if (dict.Count == 0)
        {
            return;
        }

        var next = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (oldKey, value) in dict)
        {
            var parts = oldKey.Split('\u0001', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var newKey = RoutePathDraft.SegmentEdgeKey(mapId(parts[0]), mapId(parts[1]));
            next.TryAdd(newKey, value);
        }

        dict.Clear();
        foreach (var (k, v) in next)
        {
            dict[k] = v;
        }
    }

    private static void RemapSet(HashSet<string> set, Func<string, string> mapId)
    {
        if (set.Count == 0)
        {
            return;
        }

        var next = new HashSet<string>(StringComparer.Ordinal);
        foreach (var oldKey in set)
        {
            var parts = oldKey.Split('\u0001', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            next.Add(RoutePathDraft.SegmentEdgeKey(mapId(parts[0]), mapId(parts[1])));
        }

        set.Clear();
        foreach (var k in next)
        {
            set.Add(k);
        }
    }
}
