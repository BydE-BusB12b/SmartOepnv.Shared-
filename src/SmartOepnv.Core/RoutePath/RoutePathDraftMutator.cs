namespace SmartOepnv.Core.RoutePath;

public static class RoutePathDraftMutator
{
    public static void ClearSegmentsAndSnap(RoutePathDraft draft)
    {
        draft.Segments.Clear();
        ClearSnapData(draft);
    }

    public static void ClearSnapData(RoutePathDraft draft)
    {
        draft.SnappedShape.Clear();
        draft.SnappedManeuvers.Clear();
        draft.RoadSnappedEdgeKeys.Clear();
        draft.RoadBusStraightEdgeKeys.Clear();
        draft.RoadSegmentPolylines.Clear();
        draft.RoadSegmentManeuvers.Clear();
    }

    public static bool DeleteSegment(RoutePathDraft draft, string fromNodeId, string toNodeId)
    {
        var target = draft.Segments.FirstOrDefault(s =>
            s.FromNodeId == fromNodeId && s.ToNodeId == toNodeId);
        if (target is null)
        {
            return false;
        }

        var removedKey = RoutePathDraft.SegmentEdgeKey(fromNodeId, toNodeId);
        draft.Segments = draft.Segments
            .Where(s => !(s.FromNodeId == fromNodeId && s.ToNodeId == toNodeId))
            .OrderBy(s => s.Order)
            .Select((s, idx) => new RoutePathSegment
            {
                Order = idx + 1,
                FromNodeId = s.FromNodeId,
                ToNodeId = s.ToNodeId
            })
            .ToList();

        var updatedKeys = draft.Segments
            .Select(s => RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId))
            .ToHashSet(StringComparer.Ordinal);

        draft.RoadSnappedEdgeKeys.Remove(removedKey);
        draft.RoadBusStraightEdgeKeys.Remove(removedKey);
        draft.RoadSegmentPolylines.Remove(removedKey);
        draft.RoadSegmentManeuvers.Remove(removedKey);

        var keysToRemove = draft.RoadSegmentPolylines.Keys
            .Concat(draft.RoadSegmentManeuvers.Keys)
            .Where(k => !updatedKeys.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var key in keysToRemove)
        {
            draft.RoadSegmentPolylines.Remove(key);
            draft.RoadSegmentManeuvers.Remove(key);
            draft.RoadSnappedEdgeKeys.Remove(key);
            draft.RoadBusStraightEdgeKeys.Remove(key);
        }

        draft.RoadSnappedEdgeKeys.IntersectWith(updatedKeys);
        draft.RoadBusStraightEdgeKeys.IntersectWith(updatedKeys);

        RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(draft);
        return true;
    }

    /// <summary>Doppelte Kanten (gleiches From/To) entfernen – behält gesnappte Variante.</summary>
    public static void DeduplicateSegmentsByEdge(RoutePathDraft draft)
    {
        if (draft.Segments.Count <= 1)
        {
            return;
        }

        var kept = new List<RoutePathSegment>();
        foreach (var group in draft.Segments.GroupBy(s =>
                     RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId), StringComparer.Ordinal))
        {
            var list = group.ToList();
            if (list.Count == 1)
            {
                kept.Add(list[0]);
                continue;
            }

            var best = list
                .OrderByDescending(s =>
                    draft.RoadSnappedEdgeKeys.Contains(
                        RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId))
                        ? 1
                        : 0)
                .ThenByDescending(s =>
                    draft.RoadBusStraightEdgeKeys.Contains(
                        RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId))
                        ? 1
                        : 0)
                .ThenByDescending(s => s.Order)
                .First();
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

    /// <summary>Doppelte Manöver auf derselben Kante entfernen (gleiche Position + Inhalt).</summary>
    public static void DeduplicateManeuversPerEdge(RoutePathDraft draft)
    {
        foreach (var key in draft.RoadSegmentManeuvers.Keys.ToList())
        {
            if (!draft.RoadSegmentManeuvers.TryGetValue(key, out var list) || list.Count <= 1)
            {
                continue;
            }

            var deduped = new List<RoutePathSnapManeuver>();
            foreach (var maneuver in list.OrderBy(m => m.DistanceM))
            {
                if (deduped.Any(existing => AreDuplicateManeuvers(existing, maneuver)))
                {
                    continue;
                }

                deduped.Add(maneuver);
            }

            if (deduped.Count != list.Count)
            {
                draft.RoadSegmentManeuvers[key] = deduped;
            }
        }
    }

    /// <summary>Busspur-Markierung wiederherstellen, wenn Manöver/Geometrie darauf hindeuten (z. B. nach Karten-Sync).</summary>
    public static void EnsureBusStraightEdgeKeys(RoutePathDraft draft)
    {
        foreach (var key in draft.RoadSegmentManeuvers.Keys)
        {
            if (draft.RoadBusStraightEdgeKeys.Contains(key))
            {
                continue;
            }

            if (!draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) || mans.Count == 0)
            {
                continue;
            }

            var hasBusInstruction = mans.Any(m =>
                (m.Instruction ?? string.Empty).Contains("Busspur", StringComparison.OrdinalIgnoreCase));
            var hasMultipleManual = mans.Count > 1 &&
                                    mans.All(m =>
                                        NavManeuverHelper.IsManualManeuver(m) ||
                                        (m.Instruction ?? string.Empty).Contains(
                                            "Busspur",
                                            StringComparison.OrdinalIgnoreCase));
            if (hasBusInstruction || hasMultipleManual)
            {
                draft.RoadBusStraightEdgeKeys.Add(key);
            }
        }
    }

    /// <summary>
    /// Karten-JSON enthält die bisherige Kanten-Liste zweimal hintereinander (Race nach „Symbol übernehmen“),
    /// nicht legitimes Hinzufügen von Manövern (z. B. 2 → 4 auf Busspur).
    /// </summary>
    public static bool IsConcatenatedDuplicateOfPrevious(
        IReadOnlyList<RoutePathSnapManeuver> previous,
        IReadOnlyList<RoutePathSnapManeuver> incoming)
    {
        if (previous.Count == 0 || incoming.Count != previous.Count * 2)
        {
            return false;
        }

        for (var i = 0; i < previous.Count; i++)
        {
            if (!AreDuplicateManeuvers(previous[i], incoming[i]) ||
                !AreDuplicateManeuvers(previous[i], incoming[i + previous.Count]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreDuplicateManeuvers(RoutePathSnapManeuver a, RoutePathSnapManeuver b)
    {
        if (Math.Abs(a.DistanceM - b.DistanceM) > 2)
        {
            return false;
        }

        var symA = (a.NavSymbolType ?? string.Empty).Trim();
        var symB = (b.NavSymbolType ?? string.Empty).Trim();
        if (!string.Equals(symA, symB, StringComparison.Ordinal))
        {
            return false;
        }

        var insA = (a.Instruction ?? string.Empty).Trim();
        var insB = (b.Instruction ?? string.Empty).Trim();
        return string.Equals(insA, insB, StringComparison.Ordinal);
    }
}
