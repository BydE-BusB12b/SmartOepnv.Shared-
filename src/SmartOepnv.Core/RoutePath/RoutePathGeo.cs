namespace SmartOepnv.Core.RoutePath;

internal readonly record struct TravelLaneBand(
    RoutePathLatLng AxisOrigin,
    int TravelBearing,
    double PreferredLateralMeters,
    double ToleranceMeters = 7.5);

internal static class RoutePathGeo
{
    public static int BearingDegrees(RoutePathLatLng from, RoutePathLatLng to)
    {
        var dLon = (to.Lon - from.Lon) * Math.PI / 180;
        var lat1 = from.Lat * Math.PI / 180;
        var lat2 = to.Lat * Math.PI / 180;
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var bearing = Math.Atan2(y, x) * 180 / Math.PI;
        return (int)Math.Round((bearing + 360) % 360);
    }

    public static int NormalizeBearing(int bearing) => (bearing % 360 + 360) % 360;

    public static int OppositeBearing(int bearing) => NormalizeBearing(bearing + 180);

    public static double AngleDiffDegrees(int a, int b)
    {
        var diff = Math.Abs(a - b) % 360;
        return diff > 180 ? 360 - diff : diff;
    }

    public static double HaversineMeters(RoutePathLatLng a, RoutePathLatLng b)
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

    public static double LateralOffsetMeters(RoutePathLatLng axisOrigin, int bearingDeg, RoutePathLatLng point)
    {
        var lat0 = axisOrigin.Lat * Math.PI / 180;
        var eastM = (point.Lon - axisOrigin.Lon) * Math.Cos(lat0) * 111320;
        var northM = (point.Lat - axisOrigin.Lat) * 110540;
        var bearingRad = bearingDeg * Math.PI / 180;
        var axisEast = Math.Sin(bearingRad);
        var axisNorth = Math.Cos(bearingRad);
        return axisEast * northM - axisNorth * eastM;
    }

    public static RoutePathLatLng SelectRoadAnchor(
        RoutePathLatLng marker,
        IReadOnlyList<RoutePathLatLng> candidates,
        int travelBearing)
    {
        if (candidates.Count == 0)
        {
            return marker;
        }

        var aligned = candidates
            .Select(c => new
            {
                Point = c,
                Dist = HaversineMeters(marker, c),
                Angle = AngleDiffDegrees(BearingDegrees(marker, c), travelBearing)
            })
            .Where(x => x.Dist < 28 && x.Angle < 62)
            .OrderBy(x => x.Angle * 2.5 + x.Dist)
            .Select(x => x.Point)
            .FirstOrDefault();

        return aligned ?? candidates.MinBy(c => HaversineMeters(marker, c))!;
    }

    public static TravelLaneBand CreateLaneBand(
        RoutePathLatLng marker,
        int travelBearing,
        IReadOnlyList<RoutePathLatLng> nearestCandidates,
        double toleranceMeters = 7.5)
    {
        var anchor = SelectRoadAnchor(marker, nearestCandidates, travelBearing);
        var preferred = LateralOffsetMeters(marker, travelBearing, anchor);
        return new TravelLaneBand(marker, travelBearing, preferred, toleranceMeters);
    }

    public static int? InferRoadBearingFromCandidates(
        RoutePathLatLng origin,
        IReadOnlyList<RoutePathLatLng> candidates,
        int? bearingHint = null)
    {
        if (candidates.Count < 2)
        {
            return bearingHint;
        }

        var ordered = candidates
            .OrderBy(c => HaversineMeters(origin, c))
            .Take(6)
            .ToList();
        int? best = null;
        var bestDist = 0.0;
        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var dist = HaversineMeters(ordered[i], ordered[j]);
                if (dist < 20 || dist > 450)
                {
                    continue;
                }

                if (dist > bestDist)
                {
                    bestDist = dist;
                    best = BearingDegrees(ordered[i], ordered[j]);
                }
            }
        }

        if (best is null)
        {
            return bearingHint ?? BearingDegrees(ordered[0], ordered[1]);
        }

        if (bearingHint is int hint)
        {
            var reverse = OppositeBearing(best.Value);
            return AngleDiffDegrees(best.Value, hint) <= AngleDiffDegrees(reverse, hint)
                ? best.Value
                : reverse;
        }

        return best;
    }

    public static IEnumerable<int> BearingsToTry(RoutePathLatLng from, RoutePathLatLng to, int? bearingHint)
    {
        if (bearingHint is int hint)
        {
            yield return NormalizeBearing(hint);
            yield return OppositeBearing(hint);
            yield break;
        }

        var chord = BearingDegrees(from, to);
        yield return chord;
        yield return OppositeBearing(chord);
    }

    public static bool UsesLaneBandConstraint(IReadOnlyList<RoutePathLatLng> waypoints)
    {
        if (waypoints.Count != 2)
        {
            return false;
        }

        var span = HaversineMeters(waypoints[0], waypoints[^1]);
        return span is >= 70 and <= 650;
    }

    public static bool InLaneBand(TravelLaneBand band, RoutePathLatLng point)
        => Math.Abs(LateralOffsetMeters(band.AxisOrigin, band.TravelBearing, point) - band.PreferredLateralMeters)
           <= band.ToleranceMeters;

    public static bool RouteStaysInLaneBand(IReadOnlyList<RoutePathLatLng> route, TravelLaneBand band)
    {
        if (route.Count < 2)
        {
            return false;
        }

        var spanMeters = HaversineMeters(band.AxisOrigin, route[^1]);
        var tolerance = spanMeters > 400
            ? band.ToleranceMeters + Math.Min(4, (spanMeters - 400) / 150)
            : band.ToleranceMeters;

        foreach (var point in route)
        {
            if (Math.Abs(LateralOffsetMeters(band.AxisOrigin, band.TravelBearing, point) - band.PreferredLateralMeters)
                > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    public static double MaxDeviationFromWaypointChain(
        IReadOnlyList<RoutePathLatLng> route,
        IReadOnlyList<RoutePathLatLng> waypoints)
    {
        if (route.Count == 0 || waypoints.Count == 0)
        {
            return double.MaxValue;
        }

        var max = 0.0;
        foreach (var point in route)
        {
            max = Math.Max(max, DistancePointToPolylineMeters(point, waypoints));
        }

        return max;
    }

    public static bool RouteStaysNearWaypointChain(
        IReadOnlyList<RoutePathLatLng> route,
        IReadOnlyList<RoutePathLatLng> waypoints,
        double maxDistMeters = 35)
        => MaxDeviationFromWaypointChain(route, waypoints) <= maxDistMeters;

    public static bool MatchesTravelDirection(int travelBearing, int segmentBearing, double maxDiff = 50)
        => AngleDiffDegrees(segmentBearing, travelBearing) <= maxDiff;

    public static double PathLengthMeters(IReadOnlyList<RoutePathLatLng> points)
    {
        var sum = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            sum += HaversineMeters(points[i - 1], points[i]);
        }

        return sum;
    }

    /// <summary>Echte Straßengeometrie (nicht nur Luftlinie mit Snap-Markierung).</summary>
    public static bool IsRealRoadPolyline(
        IReadOnlyList<RoutePathLatLng> polyline,
        RoutePathLatLng from,
        RoutePathLatLng to)
    {
        if (polyline.Count < 2)
        {
            return false;
        }

        // OSRM liefert typisch viele Punkte entlang der Straße – auch auf geraden Abschnitten.
        if (polyline.Count >= 3)
        {
            return true;
        }

        var air = HaversineMeters(from, to);
        var path = PathLengthMeters(polyline);
        return path > air * 1.15 + 8;
    }

    private static double DistancePointToPolylineMeters(
        RoutePathLatLng point,
        IReadOnlyList<RoutePathLatLng> polyline)
    {
        if (polyline.Count == 0)
        {
            return double.MaxValue;
        }

        if (polyline.Count == 1)
        {
            return HaversineMeters(point, polyline[0]);
        }

        var min = double.MaxValue;
        for (var i = 1; i < polyline.Count; i++)
        {
            min = Math.Min(min, DistancePointToSegmentMeters(point, polyline[i - 1], polyline[i]));
        }

        return min;
    }

    private static double DistancePointToSegmentMeters(
        RoutePathLatLng point,
        RoutePathLatLng a,
        RoutePathLatLng b)
    {
        var lat0 = (a.Lat + b.Lat) / 2 * Math.PI / 180;
        var ax = 0.0;
        var ay = 0.0;
        var bx = (b.Lon - a.Lon) * Math.Cos(lat0) * 111320;
        var by = (b.Lat - a.Lat) * 110540;
        var px = (point.Lon - a.Lon) * Math.Cos(lat0) * 111320;
        var py = (point.Lat - a.Lat) * 110540;
        var lenSq = bx * bx + by * by;
        if (lenSq < 1)
        {
            return HaversineMeters(point, a);
        }

        var t = Math.Clamp((px * bx + py * by) / lenSq, 0, 1);
        var projLon = a.Lon + (t * (b.Lon - a.Lon));
        var projLat = a.Lat + (t * (b.Lat - a.Lat));
        return HaversineMeters(point, new RoutePathLatLng { Lat = projLat, Lon = projLon });
    }
}
