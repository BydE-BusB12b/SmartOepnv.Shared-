namespace SmartOepnv.Core.RoutePath;

public static class RoutePathDraftCloner
{
    public static RoutePathDraft Clone(RoutePathDraft source)
    {
        return new RoutePathDraft
        {
            RouteName = source.RouteName,
            CreatedAtEpochMs = source.CreatedAtEpochMs,
            UpdatedAtEpochMs = source.UpdatedAtEpochMs,
            Notes = source.Notes,
            RouteLineColor = source.RouteLineColor,
            Nodes = source.Nodes.Select(n => new RoutePathNode
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                SourceStopName = n.SourceStopName,
                Lat = n.Lat,
                Lon = n.Lon
            }).ToList(),
            Segments = source.Segments.Select(s => new RoutePathSegment
            {
                Order = s.Order,
                FromNodeId = s.FromNodeId,
                ToNodeId = s.ToNodeId
            }).ToList(),
            SnappedShape = source.SnappedShape.Select(p => new RoutePathLatLng { Lat = p.Lat, Lon = p.Lon }).ToList(),
            SnappedManeuvers = source.SnappedManeuvers.Select(m => new RoutePathSnapManeuver
            {
                DistanceM = m.DistanceM,
                Instruction = m.Instruction,
                CurrentStreet = m.CurrentStreet,
                NextStreet = m.NextStreet,
                NavSymbolType = m.NavSymbolType
            }).ToList(),
            RoadSnappedEdgeKeys = source.RoadSnappedEdgeKeys.ToHashSet(StringComparer.Ordinal),
            RoadBusStraightEdgeKeys = source.RoadBusStraightEdgeKeys.ToHashSet(StringComparer.Ordinal),
            RoadSegmentPolylines = source.RoadSegmentPolylines.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(p => new RoutePathLatLng { Lat = p.Lat, Lon = p.Lon }).ToList(),
                StringComparer.Ordinal),
            RoadSegmentManeuvers = source.RoadSegmentManeuvers.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(m => new RoutePathSnapManeuver
                {
                    DistanceM = m.DistanceM,
                    Instruction = m.Instruction,
                    CurrentStreet = m.CurrentStreet,
                    NextStreet = m.NextStreet,
                    NavSymbolType = m.NavSymbolType
                }).ToList(),
                StringComparer.Ordinal)
        };
    }
}
