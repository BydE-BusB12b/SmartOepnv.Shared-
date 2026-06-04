namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Verbindet Segment-Polylines an gemeinsamen Knoten (Stitch nach OSRM / nach Absturz).
/// </summary>
public static class RoutePathPolylineJoin
{
    private const double SamePointMeters = 18.0;
    private const double StitchSearchMeters = 85.0;

    public static void AlignSegmentEndpointsAtSharedNodes(RoutePathDraft draft, string edgeKey)
    {
        if (!draft.RoadSegmentPolylines.TryGetValue(edgeKey, out var poly) || poly.Count < 2)
        {
            return;
        }

        var parts = edgeKey.Split('\u0001', 2);
        if (parts.Length != 2)
        {
            return;
        }

        AlignEnd(poly, parts[0], draft, edgeKey, atStart: true);
        AlignEnd(poly, parts[1], draft, edgeKey, atStart: false);
        draft.RoadSegmentPolylines[edgeKey] = poly;
    }

    public static RoutePathLatLng? FindNetworkPointAtNode(
        RoutePathDraft draft,
        string nodeId,
        string excludeEdgeKey)
    {
        if (!TryNodeCoordinate(draft, nodeId, out var nodePt))
        {
            return null;
        }

        RoutePathLatLng? best = null;
        var bestDist = StitchSearchMeters;

        foreach (var (key, poly) in draft.RoadSegmentPolylines)
        {
            if (key == excludeEdgeKey || poly.Count < 2)
            {
                continue;
            }

            if (!EdgeTouchesNode(key, nodeId))
            {
                continue;
            }

            var endpoint = EndpointAtNode(poly, key, nodeId);
            var d = HaversineMeters(nodePt, endpoint);
            if (d < bestDist)
            {
                bestDist = d;
                best = endpoint;
            }
        }

        return best;
    }

    private static void AlignEnd(
        List<RoutePathLatLng> poly,
        string nodeId,
        RoutePathDraft draft,
        string excludeEdgeKey,
        bool atStart)
    {
        var network = FindNetworkPointAtNode(draft, nodeId, excludeEdgeKey);
        if (network is null)
        {
            return;
        }

        var idx = atStart ? 0 : poly.Count - 1;
        if (HaversineMeters(poly[idx], network) <= StitchSearchMeters)
        {
            poly[idx] = new RoutePathLatLng { Lat = network.Lat, Lon = network.Lon };
        }
    }

    private static bool EdgeTouchesNode(string edgeKey, string nodeId)
    {
        var parts = edgeKey.Split('\u0001', 2);
        return parts.Length == 2 && (parts[0] == nodeId || parts[1] == nodeId);
    }

    private static RoutePathLatLng EndpointAtNode(IReadOnlyList<RoutePathLatLng> poly, string edgeKey, string nodeId)
    {
        var parts = edgeKey.Split('\u0001', 2);
        return parts[0] == nodeId ? poly[0] : poly[^1];
    }

    private static bool TryNodeCoordinate(RoutePathDraft draft, string nodeId, out RoutePathLatLng pt)
    {
        pt = new RoutePathLatLng();
        var node = draft.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || !double.IsFinite(node.Lat) || !double.IsFinite(node.Lon))
        {
            return false;
        }

        pt = new RoutePathLatLng { Lat = node.Lat, Lon = node.Lon };
        return true;
    }

    public static bool NearlySamePoint(RoutePathLatLng a, RoutePathLatLng b) =>
        HaversineMeters(a, b) <= SamePointMeters;

    private static double HaversineMeters(RoutePathLatLng a, RoutePathLatLng b)
    {
        const double r = 6371000;
        var dLat = (b.Lat - a.Lat) * Math.PI / 180;
        var dLon = (b.Lon - a.Lon) * Math.PI / 180;
        var aLat = a.Lat * Math.PI / 180;
        var bLat = b.Lat * Math.PI / 180;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(aLat) * Math.Cos(bLat) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
