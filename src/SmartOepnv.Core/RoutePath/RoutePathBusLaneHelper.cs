namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Busspur / direkte Linie ohne OSRM-PKW-Umwege (z. B. Bussonderstreifen, PKW-gesperrte Bereiche).
/// </summary>
public static class RoutePathBusLaneHelper
{
    public static List<RoutePathLatLng> InterpolateStraight(double fromLat, double fromLon, double toLat, double toLon)
    {
        var distMeters = Math.Max(1.0, HaversineMeters(fromLat, fromLon, toLat, toLon));
        var steps = (int)Math.Round(distMeters / 25.0);
        steps = Math.Clamp(steps, 8, 80);
        var list = new List<RoutePathLatLng>(steps);
        var denom = Math.Max(1, steps - 1);
        for (var i = 0; i < steps; i++)
        {
            var t = i / (double)denom;
            list.Add(new RoutePathLatLng
            {
                Lat = fromLat + (toLat - fromLat) * t,
                Lon = fromLon + (toLon - fromLon) * t
            });
        }

        return list;
    }

    public static List<RoutePathSnapManeuver> BusStraightManeuvers() =>
    [
        new RoutePathSnapManeuver
        {
            DistanceM = 0,
            Instruction = "Geradeaus (Busspur / direkt)",
            NavSymbolType = "straight"
        }
    ];

    public static void ApplyBusStraightToSegment(
        RoutePathDraft draft,
        RoutePathSegment segment,
        bool preserveExistingManeuvers = false)
    {
        var nodeMap = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        if (!nodeMap.TryGetValue(segment.FromNodeId, out var from) ||
            !nodeMap.TryGetValue(segment.ToNodeId, out var to))
        {
            return;
        }

        var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
        draft.RoadSegmentPolylines[key] = InterpolateStraight(from.Lat, from.Lon, to.Lat, to.Lon);
        if (!preserveExistingManeuvers ||
            !draft.RoadSegmentManeuvers.TryGetValue(key, out var existing) ||
            existing.Count == 0)
        {
            draft.RoadSegmentManeuvers[key] = BusStraightManeuvers();
        }

        draft.RoadSnappedEdgeKeys.Add(key);
        draft.RoadBusStraightEdgeKeys.Add(key);
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var aLat = lat1 * Math.PI / 180;
        var bLat = lat2 * Math.PI / 180;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(aLat) * Math.Cos(bLat) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
