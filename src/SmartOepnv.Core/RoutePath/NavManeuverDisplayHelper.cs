namespace SmartOepnv.Core.RoutePath;

/// <summary>Anzeige-Logik für Navi-Hinweise (abgestimmt mit route_path_map.html).</summary>
public static class NavManeuverDisplayHelper
{
    public static string EffectiveSymbolType(RoutePathSnapManeuver maneuver)
    {
        var explicitType = (maneuver.NavSymbolType ?? string.Empty).Trim();
        if (string.Equals(explicitType, NavSymbolCatalog.Hidden, StringComparison.Ordinal))
        {
            return NavSymbolCatalog.Hidden;
        }

        if (!string.IsNullOrEmpty(explicitType))
        {
            return explicitType;
        }

        return MapInstructionToSymbol(maneuver.Instruction);
    }

    public static bool ShouldShowOnMap(
        RoutePathSnapManeuver maneuver,
        string symbolType,
        double segmentLengthMeters)
    {
        if (string.Equals((maneuver.NavSymbolType ?? string.Empty).Trim(), NavSymbolCatalog.Hidden, StringComparison.Ordinal) ||
            string.Equals(symbolType, NavSymbolCatalog.Hidden, StringComparison.Ordinal))
        {
            return false;
        }

        var instruction = (maneuver.Instruction ?? string.Empty).Trim();
        if (instruction.Equals("start", StringComparison.OrdinalIgnoreCase) ||
            instruction.Equals("ziel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (NavManeuverHelper.IsManualManeuver(maneuver))
        {
            return true;
        }

        return !IsRedundantSegmentStartStraight(maneuver, symbolType, segmentLengthMeters);
    }

    public static double SegmentLengthMeters(RoutePathDraft draft, RoutePathSegment segment)
    {
        var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
        if (draft.RoadSegmentPolylines.TryGetValue(key, out var poly) && poly.Count >= 2)
        {
            return PolylineLengthMeters(poly);
        }

        var from = draft.Nodes.FirstOrDefault(n => n.Id == segment.FromNodeId);
        var to = draft.Nodes.FirstOrDefault(n => n.Id == segment.ToNodeId);
        if (from is null || to is null)
        {
            return 0;
        }

        return HaversineMeters(from.Lat, from.Lon, to.Lat, to.Lon);
    }

    private static bool IsRedundantSegmentStartStraight(
        RoutePathSnapManeuver maneuver,
        string symbolType,
        double segmentLengthMeters)
    {
        var sym = symbolType.ToLowerInvariant();
        if (sym is not "straight" and not "straight_stop")
        {
            return false;
        }

        var instruction = (maneuver.Instruction ?? string.Empty).Trim().ToLowerInvariant();
        var genericStraight = string.IsNullOrEmpty(instruction) ||
                              instruction is "geradeaus" or "gerade aus" or "start" ||
                              instruction.Contains("busspur / direkt", StringComparison.Ordinal);
        if (!genericStraight)
        {
            return false;
        }

        var dist = maneuver.DistanceM;
        var limit = segmentLengthMeters > 0
            ? Math.Min(35, segmentLengthMeters * 0.35)
            : 35;
        return dist <= limit;
    }

    private static double PolylineLengthMeters(IReadOnlyList<RoutePathLatLng> points)
    {
        double sum = 0;
        for (var i = 1; i < points.Count; i++)
        {
            sum += HaversineMeters(
                points[i - 1].Lat, points[i - 1].Lon,
                points[i].Lat, points[i].Lon);
        }

        return sum;
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string MapInstructionToSymbol(string? instruction)
    {
        var t = (instruction ?? string.Empty).ToLowerInvariant();
        if (t.Contains("linienweg verlassen", StringComparison.Ordinal) ||
            t.Contains("route verlassen", StringComparison.Ordinal))
        {
            return "off_route";
        }

        if (t.Contains("u-turn", StringComparison.Ordinal) ||
            t.Contains("uturn", StringComparison.Ordinal) ||
            t.Contains("wenden", StringComparison.Ordinal))
        {
            return "u_turn_custom";
        }

        if (t.Contains("kreisverkehr", StringComparison.Ordinal) ||
            t.Contains("ausfahrt", StringComparison.Ordinal))
        {
            if (t.Contains('5') || t.Contains("fünf", StringComparison.Ordinal))
            {
                if (t.Contains('4')) return "roundabout_4_5";
                if (t.Contains('3')) return "roundabout_3_5";
                if (t.Contains('2')) return "roundabout_2_5";
                return "roundabout_5_5";
            }

            if (t.Contains('4')) return "roundabout_4_4";
            if (t.Contains('3')) return "roundabout_3_4";
            if (t.Contains('2')) return "roundabout_2_4";
            return "roundabout_2_4";
        }

        var hasLeft = t.Contains("links", StringComparison.Ordinal);
        var hasRight = t.Contains("rechts", StringComparison.Ordinal);
        var hasHalfLeft = t.Contains("halb links", StringComparison.Ordinal) ||
                          t.Contains("leicht links", StringComparison.Ordinal);
        var hasHalfRight = t.Contains("halb rechts", StringComparison.Ordinal) ||
                           t.Contains("leicht rechts", StringComparison.Ordinal);
        if (hasHalfLeft) return "slight_left";
        if (hasHalfRight) return "slight_right";
        if (t.Contains("t-kreuz", StringComparison.Ordinal) || t.Contains("t kreuz", StringComparison.Ordinal))
        {
            return hasLeft ? "t_left" : "t_right";
        }

        if ((t.Contains("kreuzung", StringComparison.Ordinal) &&
             (t.Contains("gerade aus", StringComparison.Ordinal) || t.Contains("geradeaus", StringComparison.Ordinal))) &&
            !hasLeft && !hasRight)
        {
            return "cross_4_straight";
        }

        if (hasLeft && !hasRight) return "left";
        if (hasRight) return "right";
        if (t.Contains("gerade", StringComparison.Ordinal)) return "straight";
        return "straight";
    }

    /// <summary>Eine Kante = ein Segment (wie dedupeSegmentsForDraw auf der Karte).</summary>
    public static IReadOnlyList<RoutePathSegment> SegmentsForMapDisplay(RoutePathDraft draft)
    {
        var byEdge = new Dictionary<string, RoutePathSegment>(StringComparer.Ordinal);
        foreach (var seg in draft.Segments)
        {
            var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
            if (!byEdge.TryGetValue(key, out var prev))
            {
                byEdge[key] = seg;
                continue;
            }

            if (SegmentMapDisplayRank(draft, seg) > SegmentMapDisplayRank(draft, prev))
            {
                byEdge[key] = seg;
            }
        }

        return byEdge.Values.OrderBy(s => s.Order).ToList();
    }

    /// <summary>Sichtbare Navi-Hinweise in derselben Reihenfolge und Nummerierung wie auf der Karte.</summary>
    public static IEnumerable<VisibleNavManeuverEntry> EnumerateVisibleMapManeuvers(RoutePathDraft draft)
    {
        var displayNumber = 0;
        var seenMarkerKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var segment in SegmentsForMapDisplay(draft))
        {
            var edgeKey = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
            if (!draft.RoadSegmentManeuvers.TryGetValue(edgeKey, out var maneuvers) || maneuvers.Count == 0)
            {
                continue;
            }

            var segmentLength = SegmentLengthMeters(draft, segment);
            for (var i = 0; i < maneuvers.Count; i++)
            {
                var maneuver = maneuvers[i];
                var symbolType = EffectiveSymbolType(maneuver);
                if (!ShouldShowOnMap(maneuver, symbolType, segmentLength))
                {
                    continue;
                }

                var markerKey = $"{edgeKey}_m{i}";
                if (!seenMarkerKeys.Add(markerKey))
                {
                    continue;
                }

                displayNumber++;
                yield return new VisibleNavManeuverEntry(
                    segment,
                    i,
                    maneuver,
                    symbolType,
                    displayNumber,
                    markerKey);
            }
        }
    }

    private static int SegmentMapDisplayRank(RoutePathDraft draft, RoutePathSegment segment)
    {
        var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
        var rank = 0;
        if (draft.RoadSnappedEdgeKeys.Contains(key)) rank += 4;
        if (draft.RoadBusStraightEdgeKeys.Contains(key)) rank += 4;
        if (draft.RoadSegmentPolylines.TryGetValue(key, out var poly) && poly.Count >= 3) rank += 2;
        return rank * 1000 + segment.Order;
    }
}

public sealed record VisibleNavManeuverEntry(
    RoutePathSegment Segment,
    int ManeuverIndex,
    RoutePathSnapManeuver Maneuver,
    string SymbolType,
    int DisplayNumber,
    string MapMarkerKey);
