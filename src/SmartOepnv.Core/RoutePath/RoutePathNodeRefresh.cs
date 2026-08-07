using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Hält Snap-Daten und Karten-Positionen an Halt-/Ansage-Knoten, wenn sich nur Listen-Indizes ändern
/// (stop_5 → stop_7 bei gleichem Halt-Namen).
/// </summary>
public static class RoutePathNodeRefresh
{
    /// <summary>
    /// Mapped Index-Knoten auf die aktuelle Halteliste (Typ + SourceStopName), schreibt Edge-Keys um.
    /// Behält Lat/Lon von Halt-/Ansage-Markern und die relative Knotenreihenfolge (inkl. Manuell-Punkte).
    /// </summary>
    public static void RefreshNodesFromStops(RoutePathDraft draft, IList<RouteStopItem> stops)
    {
        var oldNodes = draft.Nodes.ToList();
        var oldIndexNodes = oldNodes
            .Where(n => n.Type is RoutePathNodeType.STOP or RoutePathNodeType.ANNOUNCEMENT)
            .ToList();
        var seeded = RoutePathDraftBuilder.BuildSeedNodes(stops);
        var idMap = BuildIdRemap(oldIndexNodes, seeded);

        var oldByNewId = new Dictionary<string, RoutePathNode>(StringComparer.Ordinal);
        foreach (var old in oldIndexNodes)
        {
            if (idMap.TryGetValue(old.Id, out var newId))
            {
                oldByNewId[newId] = old;
            }
        }

        // Verschobene Halt-/Ansage-Marker auf der Karte behalten – nicht GPS aus Halteliste erzwingen.
        var seededById = seeded.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var (newId, old) in oldByNewId)
        {
            if (!seededById.TryGetValue(newId, out var seed))
            {
                continue;
            }

            if (double.IsFinite(old.Lat) && double.IsFinite(old.Lon))
            {
                seed.Lat = old.Lat;
                seed.Lon = old.Lon;
            }
        }

        draft.Nodes = MergePreservingOrder(oldNodes, seeded, idMap);
        RemapEdgeKeys(draft, idMap);

        var validIds = draft.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        draft.Segments = draft.Segments
            .Where(s => validIds.Contains(s.FromNodeId) && validIds.Contains(s.ToNodeId))
            .OrderBy(s => s.Order)
            .Select((s, idx) => new RoutePathSegment
            {
                Order = idx + 1,
                FromNodeId = s.FromNodeId,
                ToNodeId = s.ToNodeId
            })
            .ToList();

        RoutePathDraftMutator.PruneOrphanEdgeSnaps(draft);
        TryAlignPolylineEndpoints(draft);
        RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(draft);
    }

    /// <summary>
    /// Alte Reihenfolge behalten (Manuell zwischen Halten), neue Halte ans Ende hängen.
    /// </summary>
    private static List<RoutePathNode> MergePreservingOrder(
        IReadOnlyList<RoutePathNode> oldNodes,
        IReadOnlyList<RoutePathNode> seeded,
        IReadOnlyDictionary<string, string> idMap)
    {
        var seededById = seeded.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var usedSeedIds = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<RoutePathNode>(oldNodes.Count + seeded.Count);

        foreach (var old in oldNodes)
        {
            if (old.Type is RoutePathNodeType.AUTO_WAYPOINT or RoutePathNodeType.MANUAL_WAYPOINT)
            {
                merged.Add(old);
                continue;
            }

            if (!idMap.TryGetValue(old.Id, out var newId) ||
                !seededById.TryGetValue(newId, out var seed))
            {
                continue;
            }

            if (usedSeedIds.Add(newId))
            {
                merged.Add(seed);
            }
        }

        foreach (var seed in seeded)
        {
            if (usedSeedIds.Add(seed.Id))
            {
                merged.Add(seed);
            }
        }

        return merged;
    }

    private static Dictionary<string, string> BuildIdRemap(
        IReadOnlyList<RoutePathNode> oldNodes,
        IReadOnlyList<RoutePathNode> seeded)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedNewIds = new HashSet<string>(StringComparer.Ordinal);

        var seededById = seeded.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var old in oldNodes)
        {
            if (seededById.TryGetValue(old.Id, out var sameId) &&
                NamesMatch(old, sameId) &&
                usedNewIds.Add(sameId.Id))
            {
                map[old.Id] = sameId.Id;
            }
        }

        var remainingOld = oldNodes.Where(o => !map.ContainsKey(o.Id)).ToList();
        var remainingNew = seeded.Where(n => !usedNewIds.Contains(n.Id)).ToList();
        foreach (var old in remainingOld)
        {
            var match = remainingNew.FirstOrDefault(n =>
                n.Type == old.Type && NamesMatch(old, n));
            if (match is null)
            {
                continue;
            }

            map[old.Id] = match.Id;
            usedNewIds.Add(match.Id);
            remainingNew.Remove(match);
        }

        foreach (var old in oldNodes)
        {
            if (map.ContainsKey(old.Id))
            {
                continue;
            }

            if (seededById.ContainsKey(old.Id) && usedNewIds.Add(old.Id))
            {
                map[old.Id] = old.Id;
            }
        }

        return map;
    }

    private static bool NamesMatch(RoutePathNode a, RoutePathNode b) =>
        string.Equals(
            (a.SourceStopName ?? string.Empty).Trim(),
            (b.SourceStopName ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static void RemapEdgeKeys(RoutePathDraft draft, IReadOnlyDictionary<string, string> idMap)
    {
        if (idMap.Count == 0)
        {
            return;
        }

        string MapId(string id) => idMap.TryGetValue(id, out var mapped) ? mapped : id;

        draft.Segments = draft.Segments
            .Select(s => new RoutePathSegment
            {
                Order = s.Order,
                FromNodeId = MapId(s.FromNodeId),
                ToNodeId = MapId(s.ToNodeId)
            })
            .ToList();

        RemapKeyedDictionary(draft.RoadSegmentPolylines, MapId);
        RemapKeyedDictionary(draft.RoadSegmentManeuvers, MapId);
        RemapKeySet(draft.RoadSnappedEdgeKeys, MapId);
        RemapKeySet(draft.RoadBusStraightEdgeKeys, MapId);
    }

    private static void RemapKeyedDictionary<T>(
        Dictionary<string, T> dict,
        Func<string, string> mapId)
    {
        if (dict.Count == 0)
        {
            return;
        }

        var rewritten = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (oldKey, value) in dict)
        {
            var parts = oldKey.Split('\u0001', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var newKey = RoutePathDraft.SegmentEdgeKey(mapId(parts[0]), mapId(parts[1]));
            if (!rewritten.ContainsKey(newKey))
            {
                rewritten[newKey] = value;
            }
        }

        dict.Clear();
        foreach (var (key, value) in rewritten)
        {
            dict[key] = value;
        }
    }

    private static void RemapKeySet(HashSet<string> set, Func<string, string> mapId)
    {
        if (set.Count == 0)
        {
            return;
        }

        var rewritten = new HashSet<string>(StringComparer.Ordinal);
        foreach (var oldKey in set)
        {
            var parts = oldKey.Split('\u0001', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            rewritten.Add(RoutePathDraft.SegmentEdgeKey(mapId(parts[0]), mapId(parts[1])));
        }

        set.Clear();
        foreach (var key in rewritten)
        {
            set.Add(key);
        }
    }

    private static void TryAlignPolylineEndpoints(RoutePathDraft draft)
    {
        foreach (var key in draft.RoadSegmentPolylines.Keys.ToList())
        {
            RoutePathPolylineJoin.AlignSegmentEndpointsAtSharedNodes(draft, key);
        }
    }
}
