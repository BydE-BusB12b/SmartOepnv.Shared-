namespace SmartOepnv.Core.RoutePath;

public static class RoutePathSnapOrchestrator
{
    public static async Task SnapSegmentAsync(
        RoutePathDraft draft,
        string fromNodeId,
        string toNodeId,
        OsrmSnapService osrm,
        CancellationToken ct = default)
    {
        var segment = draft.Segments.FirstOrDefault(s =>
            s.FromNodeId == fromNodeId && s.ToNodeId == toNodeId);
        if (segment is null)
        {
            throw new InvalidOperationException("Segment nicht gefunden.");
        }

        var key = RoutePathDraft.SegmentEdgeKey(fromNodeId, toNodeId);
        if (draft.RoadBusStraightEdgeKeys.Contains(key))
        {
            var preserveMans = draft.RoadSegmentManeuvers.TryGetValue(key, out var existing) &&
                               existing.Count > 0 &&
                               (existing.Count > 1 || HasCustomBusManeuvers(existing));
            RoutePathBusLaneHelper.ApplyBusStraightToSegment(draft, segment, preserveMans);
            RebuildMergedShapeAndManeuvers(draft);
            return;
        }

        var waypoints = BuildSegmentWaypoints(draft, segment, key);
        if (waypoints.Count < 2)
        {
            throw new InvalidOperationException("Segment-Endpunkte haben keine gültigen Koordinaten.");
        }

        var snap = await SnapSegmentPathWithFallbacksAsync(draft, segment, key, waypoints, osrm, ct);
        if (!snap.IsRoadRoute || snap.Points.Count < 2)
        {
            throw new InvalidOperationException(snap.Error ?? "OSRM-Snap fehlgeschlagen.");
        }

        draft.RoadSegmentPolylines[key] = snap.Points.ToList();
        draft.RoadSegmentManeuvers[key] = snap.Maneuvers.ToList();
        draft.RoadSnappedEdgeKeys.Add(key);
        draft.RoadBusStraightEdgeKeys.Remove(key);
        RoutePathPolylineJoin.AlignSegmentEndpointsAtSharedNodes(draft, key);
        RoutePathDraftMutator.DeduplicateSegmentsByEdge(draft);
        RebuildMergedShapeAndManeuvers(draft);
    }

    public static async Task SnapAllSegmentsAsync(
        RoutePathDraft draft,
        OsrmSnapService osrm,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var segment in draft.Segments.OrderBy(s => s.Order))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await SnapSegmentAsync(draft, segment.FromNodeId, segment.ToNodeId, osrm, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"#{segment.Order}: {ex.Message}");
            }
        }

        if (errors.Count > 0 && draft.RoadSnappedEdgeKeys.Count == 0)
        {
            throw new InvalidOperationException(string.Join(" | ", errors));
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"{draft.RoadSnappedEdgeKeys.Count} Segmente gesnappt, {errors.Count} fehlgeschlagen: " +
                errors[0]);
        }
    }

    public static bool SegmentHasRealRoadGeometry(RoutePathDraft draft, RoutePathSegment segment)
    {
        var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
        if (draft.RoadBusStraightEdgeKeys.Contains(key))
        {
            return true;
        }

        if (!draft.RoadSegmentPolylines.TryGetValue(key, out var pts) || pts.Count < 2)
        {
            return false;
        }

        var from = draft.Nodes.FirstOrDefault(n => n.Id == segment.FromNodeId);
        var to = draft.Nodes.FirstOrDefault(n => n.Id == segment.ToNodeId);
        if (from is null || to is null)
        {
            return pts.Count >= 3;
        }

        return RoutePathGeo.IsRealRoadPolyline(
            pts,
            new RoutePathLatLng { Lat = from.Lat, Lon = from.Lon },
            new RoutePathLatLng { Lat = to.Lat, Lon = to.Lon });
    }

    public static void RebuildMergedShapeAndManeuvers(RoutePathDraft draft)
    {
        PruneStaleSnapKeys(draft);
        var nodeMap = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var shape = new List<RoutePathLatLng>();
        var mergedManeuvers = new List<RoutePathSnapManeuver>();
        var cumulative = 0.0;

        foreach (var segment in draft.Segments.OrderBy(s => s.Order))
        {
            var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
            List<RoutePathLatLng> pts;
            if (draft.RoadSegmentPolylines.TryGetValue(key, out var poly) && poly.Count >= 2)
            {
                pts = poly;
            }
            else if (nodeMap.TryGetValue(segment.FromNodeId, out var from) &&
                     nodeMap.TryGetValue(segment.ToNodeId, out var to))
            {
                pts = [new RoutePathLatLng { Lat = from.Lat, Lon = from.Lon }, new RoutePathLatLng { Lat = to.Lat, Lon = to.Lon }];
            }
            else
            {
                continue;
            }

            AppendSnappedPolyline(shape, pts);

            if (draft.RoadSegmentManeuvers.TryGetValue(key, out var mans))
            {
                foreach (var m in mans)
                {
                    mergedManeuvers.Add(new RoutePathSnapManeuver
                    {
                        DistanceM = cumulative + m.DistanceM,
                        Instruction = m.Instruction,
                        CurrentStreet = m.CurrentStreet,
                        NextStreet = m.NextStreet,
                        NavSymbolType = m.NavSymbolType
                    });
                }
            }

            cumulative += PathLengthMeters(pts);
        }

        draft.SnappedShape = shape;
        draft.SnappedManeuvers = mergedManeuvers;
    }

    private static List<RoutePathLatLng> BuildSegmentWaypoints(
        RoutePathDraft draft,
        RoutePathSegment segment,
        string? excludeEdgeKey = null)
    {
        var edgeKey = excludeEdgeKey ??
                      RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
        var nodeMap = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        if (!nodeMap.TryGetValue(segment.FromNodeId, out var fromNode) ||
            !nodeMap.TryGetValue(segment.ToNodeId, out var toNode))
        {
            return [];
        }

        var stopNodes = draft.Nodes.Where(n => n.Type == RoutePathNodeType.STOP).ToList();
        var involvesManual = fromNode.Type == RoutePathNodeType.MANUAL_WAYPOINT ||
                             toNode.Type == RoutePathNodeType.MANUAL_WAYPOINT;
        var waypoints = new List<RoutePathLatLng>
        {
            new() { Lat = fromNode.Lat, Lon = fromNode.Lon }
        };

        if (!involvesManual)
        {
            var stitchFrom = RoutePathPolylineJoin.FindNetworkPointAtNode(draft, segment.FromNodeId, edgeKey);
            if (stitchFrom is not null && !RoutePathPolylineJoin.NearlySamePoint(waypoints[^1], stitchFrom))
            {
                waypoints.Add(stitchFrom);
            }
        }

        if (!involvesManual &&
            fromNode.Type == RoutePathNodeType.ANNOUNCEMENT &&
            ShouldRouteViaPairedStop(fromNode, toNode))
        {
            var paired = ResolvePairedStopNode(fromNode, stopNodes);
            if (paired is not null)
            {
                var pairedPt = new RoutePathLatLng { Lat = paired.Lat, Lon = paired.Lon };
                if (!RoutePathPolylineJoin.NearlySamePoint(waypoints[^1], pairedPt))
                {
                    waypoints.Add(pairedPt);
                }
            }
        }

        if (!involvesManual &&
            toNode.Type == RoutePathNodeType.ANNOUNCEMENT &&
            ShouldRouteViaPairedStop(toNode, fromNode))
        {
            var paired = ResolvePairedStopNode(toNode, stopNodes);
            if (paired is not null)
            {
                var pairedPt = new RoutePathLatLng { Lat = paired.Lat, Lon = paired.Lon };
                if (!RoutePathPolylineJoin.NearlySamePoint(waypoints[^1], pairedPt))
                {
                    waypoints.Add(pairedPt);
                }
            }
        }

        var endPt = new RoutePathLatLng { Lat = toNode.Lat, Lon = toNode.Lon };
        if (!involvesManual)
        {
            var stitchTo = RoutePathPolylineJoin.FindNetworkPointAtNode(draft, segment.ToNodeId, edgeKey);
            if (stitchTo is not null && !RoutePathPolylineJoin.NearlySamePoint(endPt, stitchTo))
            {
                waypoints.Add(stitchTo);
            }
        }

        if (!RoutePathPolylineJoin.NearlySamePoint(waypoints[^1], endPt))
        {
            waypoints.Add(endPt);
        }

        return DedupeConsecutiveWaypoints(waypoints);
    }

    private static List<RoutePathLatLng> DedupeConsecutiveWaypoints(List<RoutePathLatLng> waypoints)
    {
        var list = new List<RoutePathLatLng>();
        RoutePathLatLng? last = null;
        foreach (var p in waypoints)
        {
            if (last is not null && RoutePathPolylineJoin.NearlySamePoint(last, p))
            {
                continue;
            }

            list.Add(p);
            last = p;
        }

        return list;
    }

    private static bool ShouldRouteViaPairedStop(RoutePathNode announcementNode, RoutePathNode otherNode)
    {
        if (otherNode.Type is RoutePathNodeType.MANUAL_WAYPOINT or RoutePathNodeType.AUTO_WAYPOINT)
        {
            return false;
        }

        var annIdx = ExtractPairIndex(announcementNode.Id);
        var otherIdx = ExtractPairIndex(otherNode.Id);
        return otherNode.Type == RoutePathNodeType.ANNOUNCEMENT || annIdx == otherIdx;
    }

    private static List<RoutePathLatLng> BuildNodeEndpointWaypoints(RoutePathDraft draft, RoutePathSegment segment)
    {
        var from = draft.Nodes.FirstOrDefault(n => n.Id == segment.FromNodeId);
        var to = draft.Nodes.FirstOrDefault(n => n.Id == segment.ToNodeId);
        if (from is null || to is null)
        {
            return [];
        }

        return
        [
            new RoutePathLatLng { Lat = from.Lat, Lon = from.Lon },
            new RoutePathLatLng { Lat = to.Lat, Lon = to.Lon }
        ];
    }

    private static async Task<OsrmSnapResult> SnapSegmentPathWithFallbacksAsync(
        RoutePathDraft draft,
        RoutePathSegment segment,
        string edgeKey,
        IReadOnlyList<RoutePathLatLng> primaryWaypoints,
        OsrmSnapService osrm,
        CancellationToken ct)
    {
        var attempts = new List<IReadOnlyList<RoutePathLatLng>> { primaryWaypoints };

        var endpoints = BuildNodeEndpointWaypoints(draft, segment);
        if (endpoints.Count >= 2 && !WaypointListsEquivalent(endpoints, primaryWaypoints))
        {
            attempts.Add(endpoints);
        }

        var stitchStart = RoutePathPolylineJoin.FindNetworkPointAtNode(draft, segment.FromNodeId, edgeKey);
        if (stitchStart is not null && endpoints.Count >= 2)
        {
            var viaStitch =
                new List<RoutePathLatLng> { stitchStart, endpoints[^1] };
            if (!WaypointListsEquivalent(viaStitch, primaryWaypoints) &&
                !WaypointListsEquivalent(viaStitch, endpoints))
            {
                attempts.Add(viaStitch);
            }
        }

        OsrmSnapResult? last = null;
        foreach (var waypoints in attempts)
        {
            ct.ThrowIfCancellationRequested();
            var snap = await osrm.SnapPathAsync(waypoints, ct: ct);
            last = snap;
            if (snap.IsRoadRoute && snap.Points.Count >= 2)
            {
                return snap;
            }
        }

        return last ?? OsrmSnapResult.Failed(endpoints, "OSRM-Snap fehlgeschlagen.");
    }

    private static bool WaypointListsEquivalent(
        IReadOnlyList<RoutePathLatLng> a,
        IReadOnlyList<RoutePathLatLng> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!RoutePathPolylineJoin.NearlySamePoint(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static RoutePathNode? ResolvePairedStopNode(RoutePathNode announcementNode, IList<RoutePathNode> stopNodes)
    {
        var pairIdx = ExtractPairIndex(announcementNode.Id);
        var byIndex = stopNodes.FirstOrDefault(s => ExtractPairIndex(s.Id) == pairIdx);
        if (byIndex is not null)
        {
            return byIndex;
        }

        var byName = announcementNode.SourceStopName?.Trim();
        if (string.IsNullOrEmpty(byName))
        {
            return null;
        }

        return stopNodes
            .Where(s => string.Equals(s.SourceStopName?.Trim(), byName, StringComparison.OrdinalIgnoreCase))
            .MinBy(s => HaversineMeters(announcementNode.Lat, announcementNode.Lon, s.Lat, s.Lon));
    }

    private static int ExtractPairIndex(string nodeId)
    {
        var idx = nodeId.LastIndexOf('_');
        if (idx < 0 || idx >= nodeId.Length - 1)
        {
            return 0;
        }

        return int.TryParse(nodeId[(idx + 1)..], out var parsed) ? parsed : 0;
    }

    private static void AppendSnappedPolyline(List<RoutePathLatLng> acc, IReadOnlyList<RoutePathLatLng> pts)
    {
        if (pts.Count == 0)
        {
            return;
        }

        if (acc.Count > 0)
        {
            var first = pts[0];
            var last = acc[^1];
            if (RoutePathPolylineJoin.NearlySamePoint(first, last))
            {
                acc.AddRange(pts.Skip(1));
                return;
            }
        }

        acc.AddRange(pts);
    }

    private static double PathLengthMeters(IReadOnlyList<RoutePathLatLng> points)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        var sum = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            sum += HaversineMeters(points[i - 1].Lat, points[i - 1].Lon, points[i].Lat, points[i].Lon);
        }

        return sum;
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

    private static bool HasCustomBusManeuvers(IReadOnlyList<RoutePathSnapManeuver> maneuvers) =>
        maneuvers.Count > 1 ||
        maneuvers.Any(m =>
            NavManeuverHelper.IsManualManeuver(m) ||
            !string.Equals(m.NavSymbolType, "straight", StringComparison.OrdinalIgnoreCase));

    private static void PruneStaleSnapKeys(RoutePathDraft draft)
    {
        var nodeMap = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var key in draft.RoadSnappedEdgeKeys.ToList())
        {
            if (draft.RoadBusStraightEdgeKeys.Contains(key))
            {
                continue;
            }

            if (!draft.RoadSegmentPolylines.TryGetValue(key, out var pts) || pts.Count < 2)
            {
                draft.RoadSnappedEdgeKeys.Remove(key);
                continue;
            }

            var parts = key.Split('\u0001', 2);
            if (parts.Length != 2 ||
                !nodeMap.TryGetValue(parts[0], out var from) ||
                !nodeMap.TryGetValue(parts[1], out var to))
            {
                continue;
            }

            if (!RoutePathGeo.IsRealRoadPolyline(
                    pts,
                    new RoutePathLatLng { Lat = from.Lat, Lon = from.Lon },
                    new RoutePathLatLng { Lat = to.Lat, Lon = to.Lon }))
            {
                draft.RoadSnappedEdgeKeys.Remove(key);
            }
        }
    }
}
