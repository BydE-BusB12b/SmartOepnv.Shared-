using System.Text.Json.Nodes;
using static SmartOepnv.Core.JsonNodeExtensions;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Lernt aus manuellen Navi-Korrekturen (Symbole, Waypoints) und wendet sie
/// beim nächsten Snap an vergleichbaren Stellen wieder an.
/// Persistenz: <c>packageRoot["navCorrectionMemory"]</c>.
/// </summary>
public static class NavCorrectionMemory
{
    public const string RootKey = "navCorrectionMemory";
    private const int MaxEntries = 2500;
    private const double SymbolMatchRadiusM = 38;
    private const double WaypointCorridorRadiusM = 55;
    private const double BearingToleranceDeg = 55;

    public sealed class Entry
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        /// <summary>Anfahrtsrichtung in ° (0–359), optional.</summary>
        public int? BearingDeg { get; set; }
        /// <summary><c>symbol</c> oder <c>waypoint</c>.</summary>
        public string Kind { get; set; } = "symbol";
        public string? NavSymbolType { get; set; }
        public string? Instruction { get; set; }
        public int HitCount { get; set; } = 1;
        public long UpdatedAtEpochMs { get; set; }
    }

    public static void RecordSymbolCorrection(
        JsonObject? packageRoot,
        double lat,
        double lon,
        int? bearingDeg,
        string? navSymbolType,
        string? instruction)
    {
        if (packageRoot is null || !IsFinite(lat, lon))
        {
            return;
        }

        var symbol = (navSymbolType ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(symbol))
        {
            return;
        }

        var entries = Load(packageRoot);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var existing = FindNearest(entries, lat, lon, bearingDeg, "symbol", SymbolMatchRadiusM);
        if (existing is not null)
        {
            existing.Lat = lat;
            existing.Lon = lon;
            existing.BearingDeg = bearingDeg ?? existing.BearingDeg;
            existing.NavSymbolType = symbol;
            existing.Instruction = string.IsNullOrWhiteSpace(instruction) ? existing.Instruction : instruction.Trim();
            existing.HitCount = Math.Min(existing.HitCount + 1, 999);
            existing.UpdatedAtEpochMs = now;
        }
        else
        {
            entries.Add(new Entry
            {
                Lat = lat,
                Lon = lon,
                BearingDeg = bearingDeg,
                Kind = "symbol",
                NavSymbolType = symbol,
                Instruction = string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim(),
                HitCount = 1,
                UpdatedAtEpochMs = now
            });
        }

        Save(packageRoot, entries);
    }

    public static void RecordWaypoint(JsonObject? packageRoot, double lat, double lon, int? bearingDeg = null)
    {
        if (packageRoot is null || !IsFinite(lat, lon))
        {
            return;
        }

        var entries = Load(packageRoot);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var existing = FindNearest(entries, lat, lon, bearingDeg, "waypoint", SymbolMatchRadiusM);
        if (existing is not null)
        {
            existing.Lat = lat;
            existing.Lon = lon;
            existing.BearingDeg = bearingDeg ?? existing.BearingDeg;
            existing.HitCount = Math.Min(existing.HitCount + 1, 999);
            existing.UpdatedAtEpochMs = now;
        }
        else
        {
            entries.Add(new Entry
            {
                Lat = lat,
                Lon = lon,
                BearingDeg = bearingDeg,
                Kind = "waypoint",
                HitCount = 1,
                UpdatedAtEpochMs = now
            });
        }

        Save(packageRoot, entries);
    }

    /// <summary>Alle manuellen Waypoints und gesetzten Symbole einer Route ins Gedächtnis übernehmen.</summary>
    public static void HarvestFromDraft(JsonObject? packageRoot, RoutePathDraft draft)
    {
        if (packageRoot is null || draft is null)
        {
            return;
        }

        foreach (var node in draft.Nodes)
        {
            if (node.Type != RoutePathNodeType.MANUAL_WAYPOINT || !IsFinite(node.Lat, node.Lon))
            {
                continue;
            }

            RecordWaypoint(packageRoot, node.Lat, node.Lon);
        }

        foreach (var (key, mans) in draft.RoadSegmentManeuvers)
        {
            if (!draft.RoadSegmentPolylines.TryGetValue(key, out var poly) || poly.Count < 2 || mans.Count == 0)
            {
                continue;
            }

            foreach (var man in mans)
            {
                var symbol = (man.NavSymbolType ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(symbol) ||
                    string.Equals(symbol, NavSymbolCatalog.Hidden, StringComparison.Ordinal))
                {
                    continue;
                }

                // Nur echte Korrekturen / manuelle Hinweise – nicht jedes OSRM-Standard-left/right.
                var isManual = NavManeuverHelper.IsManualManeuver(man);
                var isRichSymbol = !IsBasicOsrmSymbol(symbol);
                if (!isManual && !isRichSymbol)
                {
                    continue;
                }

                var at = PointAlongPolyline(poly, man.DistanceM);
                if (at is null)
                {
                    continue;
                }

                var bearing = BearingNearDistance(poly, man.DistanceM);
                RecordSymbolCorrection(
                    packageRoot,
                    at.Lat,
                    at.Lon,
                    bearing,
                    symbol,
                    man.Instruction);
            }
        }
    }

    /// <summary>Nach OSRM-Snap: gelernte Symbole auf Manöver in der Nähe anwenden.</summary>
    public static void ApplySymbolCorrections(
        JsonObject? packageRoot,
        IReadOnlyList<RoutePathLatLng> polyline,
        IList<RoutePathSnapManeuver> maneuvers)
    {
        if (packageRoot is null || polyline.Count < 2 || maneuvers.Count == 0)
        {
            return;
        }

        var entries = Load(packageRoot);
        var symbols = entries.Where(e => e.Kind == "symbol").ToList();
        if (symbols.Count == 0)
        {
            return;
        }

        var touched = false;
        foreach (var man in maneuvers)
        {
            var at = PointAlongPolyline(polyline, man.DistanceM);
            if (at is null)
            {
                continue;
            }

            var bearing = BearingNearDistance(polyline, man.DistanceM);
            var match = FindNearest(symbols, at.Lat, at.Lon, bearing, "symbol", SymbolMatchRadiusM);
            if (match is null || string.IsNullOrWhiteSpace(match.NavSymbolType))
            {
                continue;
            }

            man.NavSymbolType = match.NavSymbolType;
            if (!string.IsNullOrWhiteSpace(match.Instruction))
            {
                man.Instruction = match.Instruction!;
            }

            match.HitCount = Math.Min(match.HitCount + 1, 999);
            match.UpdatedAtEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            touched = true;
        }

        if (touched)
        {
            Save(packageRoot, entries);
        }
    }

    /// <summary>
    /// Gelernte Waypoints, die im Korridor zwischen From/To liegen, als Zwischenpunkte einfügen.
    /// </summary>
    public static IReadOnlyList<RoutePathLatLng> SuggestWaypointsAlongSegment(
        JsonObject? packageRoot,
        RoutePathLatLng from,
        RoutePathLatLng to,
        IReadOnlyList<RoutePathLatLng> existingWaypoints)
    {
        if (packageRoot is null)
        {
            return existingWaypoints;
        }

        var remembered = Load(packageRoot).Where(e => e.Kind == "waypoint").ToList();
        if (remembered.Count == 0)
        {
            return existingWaypoints;
        }

        var span = RoutePathGeo.HaversineMeters(from, to);
        if (span < 40)
        {
            return existingWaypoints;
        }

        var travelBearing = RoutePathGeo.BearingDegrees(from, to);
        var candidates = new List<(RoutePathLatLng Pt, double DistFromStart)>();
        foreach (var entry in remembered)
        {
            var pt = new RoutePathLatLng { Lat = entry.Lat, Lon = entry.Lon };
            var distToChord = DistancePointToSegmentMeters(pt, from, to);
            if (distToChord > WaypointCorridorRadiusM)
            {
                continue;
            }

            if (entry.BearingDeg is int b &&
                RoutePathGeo.AngleDiffDegrees(b, travelBearing) > BearingToleranceDeg)
            {
                continue;
            }

            var along = ProjectedDistanceAlongSegment(pt, from, to);
            if (along < 25 || along > span - 25)
            {
                continue;
            }

            // Schon vorhandener Waypoint in der Nähe?
            if (existingWaypoints.Any(w => RoutePathGeo.HaversineMeters(w, pt) < 18))
            {
                continue;
            }

            candidates.Add((pt, along));
        }

        if (candidates.Count == 0)
        {
            return existingWaypoints;
        }

        var merged = existingWaypoints.ToList();
        foreach (var c in candidates.OrderBy(c => c.DistFromStart))
        {
            // Zwischen passenden Nachbarn einfügen (nach Entfernung vom Start)
            var insertAt = 1;
            for (var i = 1; i < merged.Count; i++)
            {
                var d = RoutePathGeo.HaversineMeters(from, merged[i]);
                if (d < c.DistFromStart)
                {
                    insertAt = i + 1;
                }
            }

            if (insertAt >= merged.Count)
            {
                merged.Insert(merged.Count - 1, c.Pt);
            }
            else
            {
                merged.Insert(insertAt, c.Pt);
            }
        }

        return merged;
    }

    public static RoutePathLatLng? PointAlongPolyline(IReadOnlyList<RoutePathLatLng> poly, double distanceM)
    {
        if (poly.Count == 0)
        {
            return null;
        }

        if (poly.Count == 1 || distanceM <= 0)
        {
            return new RoutePathLatLng { Lat = poly[0].Lat, Lon = poly[0].Lon };
        }

        var remain = Math.Max(0, distanceM);
        for (var i = 1; i < poly.Count; i++)
        {
            var segLen = RoutePathGeo.HaversineMeters(poly[i - 1], poly[i]);
            if (remain <= segLen || i == poly.Count - 1)
            {
                if (segLen < 0.5)
                {
                    return new RoutePathLatLng { Lat = poly[i].Lat, Lon = poly[i].Lon };
                }

                var t = Math.Clamp(remain / segLen, 0, 1);
                return new RoutePathLatLng
                {
                    Lat = poly[i - 1].Lat + t * (poly[i].Lat - poly[i - 1].Lat),
                    Lon = poly[i - 1].Lon + t * (poly[i].Lon - poly[i - 1].Lon)
                };
            }

            remain -= segLen;
        }

        return new RoutePathLatLng { Lat = poly[^1].Lat, Lon = poly[^1].Lon };
    }

    public static int? BearingNearDistance(IReadOnlyList<RoutePathLatLng> poly, double distanceM)
    {
        if (poly.Count < 2)
        {
            return null;
        }

        var remain = Math.Max(0, distanceM);
        for (var i = 1; i < poly.Count; i++)
        {
            var segLen = RoutePathGeo.HaversineMeters(poly[i - 1], poly[i]);
            if (remain <= segLen || i == poly.Count - 1)
            {
                return RoutePathGeo.BearingDegrees(poly[i - 1], poly[i]);
            }

            remain -= segLen;
        }

        return RoutePathGeo.BearingDegrees(poly[^2], poly[^1]);
    }

    public static List<Entry> Load(JsonObject packageRoot)
    {
        var list = new List<Entry>();
        if (packageRoot[RootKey] is not JsonArray arr)
        {
            return list;
        }

        foreach (var node in arr)
        {
            if (node is not JsonObject o)
            {
                continue;
            }

            var lat = o["lat"]?.GetValue<double>() ?? double.NaN;
            var lon = o["lon"]?.GetValue<double>() ?? double.NaN;
            if (!IsFinite(lat, lon))
            {
                continue;
            }

            list.Add(new Entry
            {
                Lat = lat,
                Lon = lon,
                BearingDeg = o["brg"]?.GetValue<int>(),
                Kind = (o["k"]?.GetValue<string>() ?? "symbol").Trim().ToLowerInvariant(),
                NavSymbolType = o["sym"]?.GetValue<string>(),
                Instruction = o["ins"]?.GetValue<string>(),
                HitCount = Math.Max(1, o["n"]?.GetValue<int>() ?? 1),
                UpdatedAtEpochMs = o["t"]?.GetValue<long>() ?? 0L
            });
        }

        return list;
    }

    public static void Save(JsonObject packageRoot, IList<Entry> entries)
    {
        var pruned = entries
            .OrderByDescending(e => e.UpdatedAtEpochMs)
            .ThenByDescending(e => e.HitCount)
            .Take(MaxEntries)
            .ToList();

        var arr = new JsonArray();
        foreach (var e in pruned)
        {
            var o = new JsonObject
            {
                ["lat"] = e.Lat,
                ["lon"] = e.Lon,
                ["k"] = e.Kind,
                ["n"] = e.HitCount,
                ["t"] = e.UpdatedAtEpochMs
            };
            if (e.BearingDeg is int b)
            {
                o["brg"] = b;
            }

            if (!string.IsNullOrWhiteSpace(e.NavSymbolType))
            {
                o["sym"] = e.NavSymbolType;
            }

            if (!string.IsNullOrWhiteSpace(e.Instruction))
            {
                o["ins"] = e.Instruction;
            }

            arr.Add(o);
        }

        packageRoot[RootKey] = arr;
    }

    private static Entry? FindNearest(
        IList<Entry> entries,
        double lat,
        double lon,
        int? bearingDeg,
        string kind,
        double radiusM)
    {
        Entry? best = null;
        var bestDist = double.MaxValue;
        var probe = new RoutePathLatLng { Lat = lat, Lon = lon };
        foreach (var e in entries)
        {
            if (!string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var d = RoutePathGeo.HaversineMeters(probe, new RoutePathLatLng { Lat = e.Lat, Lon = e.Lon });
            if (d > radiusM || d >= bestDist)
            {
                continue;
            }

            if (bearingDeg is int want && e.BearingDeg is int have &&
                RoutePathGeo.AngleDiffDegrees(want, have) > BearingToleranceDeg)
            {
                continue;
            }

            best = e;
            bestDist = d;
        }

        return best;
    }

    private static bool IsBasicOsrmSymbol(string symbol)
    {
        return symbol is "left" or "right" or "straight" or "slight_left" or "slight_right"
            or "u_turn" or "u_turn_custom";
    }

    private static bool IsFinite(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) &&
        Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180 &&
        !(lat == 0 && lon == 0);

    private static double DistancePointToSegmentMeters(
        RoutePathLatLng point,
        RoutePathLatLng a,
        RoutePathLatLng b)
    {
        var lat0 = (a.Lat + b.Lat) / 2 * Math.PI / 180;
        var bx = (b.Lon - a.Lon) * Math.Cos(lat0) * 111320;
        var by = (b.Lat - a.Lat) * 110540;
        var px = (point.Lon - a.Lon) * Math.Cos(lat0) * 111320;
        var py = (point.Lat - a.Lat) * 110540;
        var lenSq = bx * bx + by * by;
        if (lenSq < 1)
        {
            return RoutePathGeo.HaversineMeters(point, a);
        }

        var t = Math.Clamp((px * bx + py * by) / lenSq, 0, 1);
        var proj = new RoutePathLatLng
        {
            Lat = a.Lat + t * (b.Lat - a.Lat),
            Lon = a.Lon + t * (b.Lon - a.Lon)
        };
        return RoutePathGeo.HaversineMeters(point, proj);
    }

    private static double ProjectedDistanceAlongSegment(
        RoutePathLatLng point,
        RoutePathLatLng a,
        RoutePathLatLng b)
    {
        var span = RoutePathGeo.HaversineMeters(a, b);
        if (span < 1)
        {
            return 0;
        }

        var lat0 = (a.Lat + b.Lat) / 2 * Math.PI / 180;
        var bx = (b.Lon - a.Lon) * Math.Cos(lat0) * 111320;
        var by = (b.Lat - a.Lat) * 110540;
        var px = (point.Lon - a.Lon) * Math.Cos(lat0) * 111320;
        var py = (point.Lat - a.Lat) * 110540;
        var lenSq = bx * bx + by * by;
        if (lenSq < 1)
        {
            return 0;
        }

        var t = Math.Clamp((px * bx + py * by) / lenSq, 0, 1);
        return t * span;
    }
}
