namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Reihenfolge der Segmente für die zusammengeführte Shape (nicht nur Zeitstempel der Erstellung).
/// Neue Kante A→H vor bestehender Kante H→… einfügen, damit OSRM-Strecken an Knoten H sauber anstoßen.
/// </summary>
public static class RoutePathSegmentOrdering
{
    /// <summary>
    /// Schlägt Order vor: neue Kante endet an Knoten mit bereits abgehenden gesnappten Kanten → davor einordnen.
    /// </summary>
    public static int SuggestOrderForNewEdge(RoutePathDraft draft, string fromNodeId, string toNodeId)
    {
        if (draft.Segments.Count == 0)
        {
            return 1;
        }

        var outgoingFromTarget = draft.Segments
            .Where(s => s.FromNodeId == toNodeId)
            .Select(s => s.Order)
            .ToList();
        if (outgoingFromTarget.Count > 0)
        {
            return outgoingFromTarget.Min();
        }

        var incomingToTarget = draft.Segments
            .Where(s => s.ToNodeId == toNodeId)
            .Select(s => s.Order)
            .ToList();
        if (incomingToTarget.Count > 0)
        {
            return incomingToTarget.Max() + 1;
        }

        var outgoingFromSource = draft.Segments
            .Where(s => s.FromNodeId == fromNodeId)
            .Select(s => s.Order)
            .ToList();
        if (outgoingFromSource.Count > 0)
        {
            return outgoingFromSource.Min();
        }

        var incomingToSource = draft.Segments
            .Where(s => s.ToNodeId == fromNodeId)
            .Select(s => s.Order)
            .ToList();
        if (incomingToSource.Count > 0)
        {
            return incomingToSource.Max() + 1;
        }

        return draft.Segments.Max(s => s.Order) + 1;
    }

    public static void ApplyOrderForNewEdge(RoutePathDraft draft, string fromNodeId, string toNodeId)
    {
        var seg = draft.Segments.FirstOrDefault(s =>
            s.FromNodeId == fromNodeId && s.ToNodeId == toNodeId);
        if (seg is null)
        {
            return;
        }

        var desired = SuggestOrderForNewEdge(draft, fromNodeId, toNodeId);
        if (seg.Order == desired)
        {
            RenumberContiguous(draft);
            return;
        }

        foreach (var other in draft.Segments)
        {
            if (other == seg)
            {
                continue;
            }

            if (other.Order >= desired)
            {
                other.Order++;
            }
        }

        seg.Order = desired;
        RenumberContiguous(draft);
    }

    public static void RenumberContiguous(RoutePathDraft draft)
    {
        var ordered = draft.Segments.OrderBy(s => s.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i + 1;
        }

        draft.Segments = ordered;
    }
}
