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
}
