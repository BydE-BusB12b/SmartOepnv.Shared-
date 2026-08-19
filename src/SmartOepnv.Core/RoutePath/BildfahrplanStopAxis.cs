using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Y-Achse für den Bildfahrplan: Streckenmeter entlang der gesnappten Route je Halt.
/// </summary>
public static class BildfahrplanStopAxis
{
    public sealed record StopStation(
        string Name,
        int StopIndex,
        double DistanceMeters,
        bool FromSnappedPath);

    public sealed record AxisResult(
        IReadOnlyList<StopStation> Stations,
        double TotalMeters,
        bool UsedSnappedPath,
        string SourceRouteKey);

    /// <summary>
    /// Baut die Halt-Achse strikt in Fahrplan-Reihenfolge.
    /// Meter = kumulierte Segmentlänge zwischen aufeinanderfolgenden Halten (nie GPS-Neusortierung).
    /// Benannte Wegpunkte (z. B. Pause) gehören zur Achse.
    /// </summary>
    public static AxisResult Build(string routeKey, RoutePathDraft draft, IList<RouteStopItem> stops)
    {
        var timedStops = stops
            .Select((s, i) => (Stop: s, Index: i))
            .Where(x => IsAxisLabelStop(x.Stop))
            .ToList();

        if (timedStops.Count == 0)
        {
            return new AxisResult([], 0, false, routeKey);
        }

        var nodeById = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var usedSnap = false;
        var stations = new List<StopStation>(timedStops.Count);
        var cum = 0.0;

        for (var i = 0; i < timedStops.Count; i++)
        {
            var (stop, index) = timedStops[i];
            if (i > 0)
            {
                var prev = timedStops[i - 1].Stop;
                var leg = MeasureConsecutiveStopsMeters(draft, nodeById, prev, stop, index - 1, index, out var snap);
                if (snap)
                {
                    usedSnap = true;
                }

                // Mindestens etwas Abstand, Reihenfolge bleibt Listenreihenfolge
                cum += Math.Max(leg, 50);
            }

            stations.Add(new StopStation(AxisDisplayName(stop), index, cum, usedSnap));
        }

        // Monoton mit Mindestabstand (Listenreihenfolge!)
        const double minSep = 180;
        for (var i = 1; i < stations.Count; i++)
        {
            var min = stations[i - 1].DistanceMeters + minSep;
            if (stations[i].DistanceMeters < min)
            {
                stations[i] = stations[i] with { DistanceMeters = min };
            }
        }

        var total = stations[^1].DistanceMeters;
        return new AxisResult(stations, total, usedSnap, routeKey);
    }

    /// <summary>Fahrplanhalte und benannte Wegpunkte (Pause o. Ä.) für die Y-Achse.</summary>
    public static bool IsAxisLabelStop(RouteStopItem stop)
    {
        if (!stop.IsWaypoint)
        {
            return !string.IsNullOrWhiteSpace(stop.Name);
        }

        return !string.IsNullOrWhiteSpace(stop.Name) ||
               !string.IsNullOrWhiteSpace(stop.WaypointName);
    }

    public static string AxisDisplayName(RouteStopItem stop)
    {
        string raw;
        if (!stop.IsWaypoint)
        {
            raw = NormalizeName(stop.Name);
        }
        else
        {
            var wp = NormalizeName(stop.WaypointName);
            raw = string.IsNullOrEmpty(wp) ? NormalizeName(stop.Name) : wp;
        }

        return StripOperationalPrefix(raw);
    }

    /// <summary>„Wendefahrt Morper Str.“ → „Morper Str.“</summary>
    public static string StripOperationalPrefix(string name)
    {
        var n = NormalizeName(name);
        if (n.Length == 0)
        {
            return n;
        }

        n = System.Text.RegularExpressions.Regex.Replace(
            n,
            @"^(wendefahrt|leerfahrt|bereitstellung|einsatzfahrt)\s+",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return n.Trim();
    }

    /// <summary>Gesnappte (oder Fallback-)Länge zwischen zwei Halten der Stoppliste.</summary>
    public static double MeasureConsecutiveStopsMeters(
        RoutePathDraft draft,
        IReadOnlyDictionary<string, RoutePathNode> nodeById,
        RouteStopItem fromStop,
        RouteStopItem toStop,
        int fromIndex,
        int toIndex,
        out bool usedSnap)
    {
        usedSnap = false;
        var fromId = "stop_" + fromIndex;
        var toId = "stop_" + toIndex;
        if (nodeById.ContainsKey(fromId) && nodeById.ContainsKey(toId))
        {
            var leg = MeasureStopToStopMeters(draft, nodeById, fromId, toId, out usedSnap);
            if (leg > 1)
            {
                return leg;
            }
        }

        // Fallback: Luftlinie der Halt-Koordinaten
        if (TryParseLatLon(fromStop, out var lat1, out var lon1) &&
            TryParseLatLon(toStop, out var lat2, out var lon2))
        {
            return RoutePathGeo.HaversineMeters(
                new RoutePathLatLng { Lat = lat1, Lon = lon1 },
                new RoutePathLatLng { Lat = lat2, Lon = lon2 });
        }

        return 500;
    }

    /// <summary>
    /// Summe der Segmentlängen zwischen zwei Stop-Indizes (inkl. Zwischenhalte).
    /// </summary>
    public static double MeasureIndexSpanMeters(
        RoutePathDraft draft,
        IList<RouteStopItem> stops,
        int fromIndex,
        int toIndex,
        out bool usedSnap)
    {
        usedSnap = false;
        if (fromIndex == toIndex || stops.Count == 0)
        {
            return 0;
        }

        var lo = Math.Min(fromIndex, toIndex);
        var hi = Math.Max(fromIndex, toIndex);
        if (lo < 0 || hi >= stops.Count)
        {
            return 0;
        }

        var nodeById = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var sum = 0.0;
        var anySnap = false;
        for (var i = lo + 1; i <= hi; i++)
        {
            var leg = MeasureConsecutiveStopsMeters(
                draft, nodeById, stops[i - 1], stops[i], i - 1, i, out var snap);
            if (snap)
            {
                anySnap = true;
            }

            sum += Math.Max(0, leg);
        }

        usedSnap = anySnap;
        return sum;
    }

    private static AxisResult AlignToScheduleStops(
        string routeKey,
        List<(RouteStopItem Stop, int Index)> timedStops,
        IReadOnlyList<StopStation> pathStations,
        bool usedSnap)
    {
        // Veraltet – Reihenfolge kommt nur noch aus Build(); Methode für Aufrufer behalten.
        var byIndex = pathStations.ToDictionary(s => s.StopIndex, s => s);
        var result = new List<StopStation>(timedStops.Count);
        foreach (var (stop, index) in timedStops)
        {
            if (byIndex.TryGetValue(index, out var hit))
            {
                result.Add(new StopStation(NormalizeName(stop.Name), index, hit.DistanceMeters, usedSnap));
                continue;
            }

            var name = NormalizeName(stop.Name);
            var prev = result.LastOrDefault();
            result.Add(new StopStation(name, index, prev?.DistanceMeters ?? 0, usedSnap));
        }

        for (var i = 1; i < result.Count; i++)
        {
            if (result[i].DistanceMeters < result[i - 1].DistanceMeters)
            {
                result[i] = result[i] with { DistanceMeters = result[i - 1].DistanceMeters };
            }
        }

        var total = result.Count == 0 ? 0 : result[^1].DistanceMeters;
        return new AxisResult(result, total, usedSnap, routeKey);
    }

    private static bool TryBuildFromSegmentSnaps(
        RoutePathDraft draft,
        IReadOnlyList<RoutePathNode> pathStops,
        out List<StopStation> stations)
    {
        stations = [];
        if (pathStops.Count < 2)
        {
            return false;
        }

        var nodeById = draft.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var cum = 0.0;
        stations.Add(new StopStation(
            NormalizeName(pathStops[0].Title.Length > 0 ? pathStops[0].Title : pathStops[0].SourceStopName ?? pathStops[0].Id),
            StopListIndex(pathStops[0]),
            0,
            FromSnappedPath: true));

        var anySnap = false;
        for (var i = 1; i < pathStops.Count; i++)
        {
            var from = pathStops[i - 1];
            var to = pathStops[i];
            var leg = MeasureStopToStopMeters(draft, nodeById, from.Id, to.Id, out var snapped);
            if (snapped)
            {
                anySnap = true;
            }

            cum += Math.Max(0, leg);
            stations.Add(new StopStation(
                NormalizeName(to.Title.Length > 0 ? to.Title : to.SourceStopName ?? to.Id),
                StopListIndex(to),
                cum,
                FromSnappedPath: snapped));
        }

        return anySnap && stations.Count >= 2 && cum > 1;
    }

    private static double MeasureStopToStopMeters(
        RoutePathDraft draft,
        IReadOnlyDictionary<string, RoutePathNode> nodeById,
        string fromStopId,
        string toStopId,
        out bool usedSnap)
    {
        usedSnap = false;
        // Direkte Kante
        var directKey = RoutePathDraft.SegmentEdgeKey(fromStopId, toStopId);
        if (draft.RoadSegmentPolylines.TryGetValue(directKey, out var directPoly) && directPoly.Count >= 2)
        {
            usedSnap = true;
            return RoutePathGeo.PathLengthMeters(directPoly);
        }

        // Pfad über Zwischenknoten (Waypoints)
        var segs = CollectPathSegments(draft, fromStopId, toStopId);
        if (segs.Count > 0)
        {
            var sum = 0.0;
            var any = false;
            foreach (var seg in segs)
            {
                var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
                if (draft.RoadSegmentPolylines.TryGetValue(key, out var poly) && poly.Count >= 2)
                {
                    sum += RoutePathGeo.PathLengthMeters(poly);
                    any = true;
                    continue;
                }

                if (!nodeById.TryGetValue(seg.FromNodeId, out var a) ||
                    !nodeById.TryGetValue(seg.ToNodeId, out var b))
                {
                    continue;
                }

                sum += RoutePathGeo.HaversineMeters(
                    new RoutePathLatLng { Lat = a.Lat, Lon = a.Lon },
                    new RoutePathLatLng { Lat = b.Lat, Lon = b.Lon });
            }

            usedSnap = any;
            if (sum > 0)
            {
                return sum;
            }
        }

        if (!nodeById.TryGetValue(fromStopId, out var from) ||
            !nodeById.TryGetValue(toStopId, out var to))
        {
            return 0;
        }

        return RoutePathGeo.HaversineMeters(
            new RoutePathLatLng { Lat = from.Lat, Lon = from.Lon },
            new RoutePathLatLng { Lat = to.Lat, Lon = to.Lon });
    }

    private static List<RoutePathSegment> CollectPathSegments(
        RoutePathDraft draft,
        string fromStopId,
        string toStopId)
    {
        // BFS entlang Segments
        var adj = new Dictionary<string, List<RoutePathSegment>>(StringComparer.Ordinal);
        foreach (var seg in draft.Segments)
        {
            if (!adj.TryGetValue(seg.FromNodeId, out var list))
            {
                list = [];
                adj[seg.FromNodeId] = list;
            }

            list.Add(seg);
        }

        var queue = new Queue<(string Node, List<RoutePathSegment> Path)>();
        queue.Enqueue((fromStopId, []));
        var visited = new HashSet<string>(StringComparer.Ordinal) { fromStopId };
        while (queue.Count > 0)
        {
            var (node, path) = queue.Dequeue();
            if (node == toStopId)
            {
                return path;
            }

            if (!adj.TryGetValue(node, out var outs))
            {
                continue;
            }

            foreach (var seg in outs)
            {
                if (!visited.Add(seg.ToNodeId))
                {
                    continue;
                }

                var next = new List<RoutePathSegment>(path) { seg };
                queue.Enqueue((seg.ToNodeId, next));
            }
        }

        return [];
    }

    private static List<StopStation> ProjectStopsOntoShape(
        IReadOnlyList<RoutePathLatLng> shape,
        IReadOnlyList<RoutePathNode> pathStops)
    {
        var stations = new List<StopStation>(pathStops.Count);
        var last = -1.0;
        foreach (var stop in pathStops)
        {
            var along = DistanceAlongPolyline(
                shape,
                new RoutePathLatLng { Lat = stop.Lat, Lon = stop.Lon });
            if (along < last)
            {
                along = last;
            }

            last = along;
            stations.Add(new StopStation(
                NormalizeName(stop.Title.Length > 0 ? stop.Title : stop.SourceStopName ?? stop.Id),
                StopListIndex(stop),
                along,
                FromSnappedPath: true));
        }

        return stations;
    }

    /// <summary>Kumulierte Meter bis zur Projektion des Punkts auf die Polyline.</summary>
    public static double DistanceAlongPolyline(IReadOnlyList<RoutePathLatLng> path, RoutePathLatLng point) =>
        TryDistanceAlongPolyline(path, point, out var along, out _) ? along : 0;

    /// <summary>Projektion auf Polyline inkl. Abstand zur Linie.</summary>
    public static bool TryDistanceAlongPolyline(
        IReadOnlyList<RoutePathLatLng> path,
        RoutePathLatLng point,
        out double metersAlong,
        out double distToPathMeters)
    {
        metersAlong = 0;
        distToPathMeters = double.MaxValue;
        if (path.Count == 0)
        {
            return false;
        }

        if (path.Count == 1)
        {
            distToPathMeters = RoutePathGeo.HaversineMeters(point, path[0]);
            return true;
        }

        var bestDist = double.MaxValue;
        var bestAlong = 0.0;
        var cum = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            var segLen = RoutePathGeo.HaversineMeters(a, b);
            ProjectOnSegment(point, a, b, out var t, out var dist);
            var along = cum + t * segLen;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestAlong = along;
            }

            cum += segLen;
        }

        metersAlong = bestAlong;
        distToPathMeters = bestDist;
        return true;
    }

    private static void ProjectOnSegment(
        RoutePathLatLng point,
        RoutePathLatLng a,
        RoutePathLatLng b,
        out double t,
        out double distMeters)
    {
        var lat0 = (a.Lat + b.Lat) / 2 * Math.PI / 180;
        var bx = (b.Lon - a.Lon) * Math.Cos(lat0) * 111320;
        var by = (b.Lat - a.Lat) * 110540;
        var px = (point.Lon - a.Lon) * Math.Cos(lat0) * 111320;
        var py = (point.Lat - a.Lat) * 110540;
        var lenSq = bx * bx + by * by;
        if (lenSq < 1)
        {
            t = 0;
            distMeters = RoutePathGeo.HaversineMeters(point, a);
            return;
        }

        t = Math.Clamp((px * bx + py * by) / lenSq, 0, 1);
        var proj = new RoutePathLatLng
        {
            Lat = a.Lat + t * (b.Lat - a.Lat),
            Lon = a.Lon + t * (b.Lon - a.Lon)
        };
        distMeters = RoutePathGeo.HaversineMeters(point, proj);
    }

    private static bool TryParseLatLon(RouteStopItem stop, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        var raw = !string.IsNullOrWhiteSpace(stop.StopCoordinates)
            ? stop.StopCoordinates
            : stop.GpsCoordinates;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 &&
               double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out lat) &&
               double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out lon);
    }

    private static int StopListIndex(RoutePathNode node)
    {
        const string prefix = "stop_";
        if (node.Id.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(node.Id.AsSpan(prefix.Length), out var idx))
        {
            return idx;
        }

        return int.MaxValue;
    }

    public static string NormalizeName(string? name) =>
        (name ?? string.Empty).Trim();

    /// <summary>Vergleichsschlüssel (Straße/Str. vereinheitlicht).</summary>
    public static string MatchKey(string? name)
    {
        var n = NormalizeName(name);
        if (n.Length == 0)
        {
            return n;
        }

        n = System.Text.RegularExpressions.Regex.Replace(
            n,
            @"\b(stra[ßs]e|str\.?)\b",
            "str",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return n.Trim();
    }

    /// <summary>
    /// Gleicher Halt trotz Kurz-/Langname, z. B. „Neanderthal“ ↔ „Mettmann Neanderthal“.
    /// </summary>
    public static bool NamesReferToSameStop(string? left, string? right)
    {
        var a = MatchKey(left);
        var b = MatchKey(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // „Vohwinkel Pause“ ≠ „Vohwinkel“ / „PP … Vohwinkel“
        var aPause = a.Contains("pause", StringComparison.OrdinalIgnoreCase);
        var bPause = b.Contains("pause", StringComparison.OrdinalIgnoreCase);
        if (aPause != bPause)
        {
            return false;
        }

        return IsSignificantSuffix(a, b) || IsSignificantSuffix(b, a);
    }

    private static bool IsSignificantSuffix(string shorter, string longer)
    {
        // Mind. 6 Zeichen, damit „Nord“ nicht „Erkrath Nord“ schluckt
        if (shorter.Length < 6 || longer.Length <= shorter.Length)
        {
            return false;
        }

        if (!longer.EndsWith(shorter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var before = longer.Length - shorter.Length - 1;
        return before >= 0 && longer[before] == ' ';
    }

    public static string PreferDisplayName(string existing, string candidate)
    {
        var a = NormalizeName(existing);
        var b = NormalizeName(candidate);
        return b.Length > a.Length ? b : a;
    }
}
