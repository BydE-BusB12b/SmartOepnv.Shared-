using System.Globalization;
using System.Text.Json.Nodes;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

public static class NavSymbolCatalog
{
    public const string Hidden = "__nav_hidden__";

    public static IReadOnlyList<(string Id, string Label)> All { get; } =
    [
        ("straight", "Geradeaus"),
        ("left", "Kreuzung links"),
        ("right", "Kreuzung rechts"),
        ("t_left", "T-Kreuzung links"),
        ("t_right", "T-Kreuzung rechts"),
        ("cross_4_left", "4-armig links"),
        ("cross_4_right", "4-armig rechts"),
        ("cross_4_straight", "Kreuzung geradeaus"),
        ("cross_5", "5-armig"),
        ("cross_5_left", "5-armig halb links"),
        ("cross_5_right", "5-armig halb rechts"),
        ("fork_left", "Gabelung links"),
        ("fork_right", "Gabelung rechts"),
        ("slight_left", "Leicht links"),
        ("slight_right", "Leicht rechts"),
        ("roundabout_1_4", "Kreisverkehr 1. Ausf. (4-arm)"),
        ("roundabout_2_4", "Kreisverkehr 2. Ausf. (4-arm)"),
        ("roundabout_3_4", "Kreisverkehr 3. Ausf. (4-arm)"),
        ("roundabout_4_4", "Kreisverkehr 4. Ausf. (4-arm)"),
        ("u_turn_custom", "U-Turn / Wenden"),
        ("goal", "Ziel / Endhaltestelle"),
        ("straight_stop", "Haltestelle geradeaus"),
        ("off_route", "Linienweg verlassen")
    ];
}

public static class RoutePathDraftSerializer
{
    public static string ToJson(RoutePathDraft draft) =>
        ToJsonNode(draft).ToJsonString();

    public static RoutePathDraft FromJson(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("Ungültiges JSON.");
        return FromJsonNode(node.AsObject());
    }

    public static RoutePathDraft FromJsonNode(JsonObject obj)
    {
        var draft = new RoutePathDraft
        {
            RouteName = obj["routeName"]?.GetValue<string>() ?? string.Empty,
            CreatedAtEpochMs = obj["createdAtEpochMs"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedAtEpochMs = obj["updatedAtEpochMs"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Notes = obj["notes"]?.GetValue<string>() ?? RoutePathDraft.DefaultNotes,
            RouteLineColor = NormalizeRouteLineColor(obj["routeLineColor"]?.GetValue<string>())
        };

        if (obj["nodes"] is JsonArray nodes)
        {
            foreach (var n in nodes.OfType<JsonObject>())
            {
                var lat = n["lat"]?.GetValue<double>() ?? double.NaN;
                var lon = n["lon"]?.GetValue<double>() ?? double.NaN;
                if (!double.IsFinite(lat) || !double.IsFinite(lon)) continue;
                Enum.TryParse(n["type"]?.GetValue<string>(), out RoutePathNodeType type);
                draft.Nodes.Add(new RoutePathNode
                {
                    Id = n["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    Type = type,
                    Title = n["title"]?.GetValue<string>() ?? "Punkt",
                    SourceStopName = n["sourceStopName"]?.GetValue<string>(),
                    Lat = lat,
                    Lon = lon
                });
            }
        }

        if (obj["segments"] is JsonArray segments)
        {
            foreach (var s in segments.OfType<JsonObject>())
            {
                var from = s["fromNodeId"]?.GetValue<string>()?.Trim();
                var to = s["toNodeId"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) continue;
                draft.Segments.Add(new RoutePathSegment
                {
                    Order = s["order"]?.GetValue<int>() ?? draft.Segments.Count + 1,
                    FromNodeId = from,
                    ToNodeId = to
                });
            }
        }

        draft.Segments = draft.Segments.OrderBy(s => s.Order).ToList();

        if (obj["snappedShape"] is JsonArray shape)
        {
            draft.SnappedShape.AddRange(ParsePoints(shape));
        }

        if (obj["snappedManeuvers"] is JsonArray mans)
        {
            draft.SnappedManeuvers.AddRange(ParseManeuvers(mans));
        }

        if (obj["roadSnappedEdgeKeys"] is JsonArray keys)
        {
            foreach (var k in keys)
            {
                var s = k?.GetValue<string>()?.Trim();
                if (!string.IsNullOrEmpty(s)) draft.RoadSnappedEdgeKeys.Add(s);
            }
        }

        if (obj["roadBusStraightEdgeKeys"] is JsonArray busKeys)
        {
            foreach (var k in busKeys)
            {
                var s = k?.GetValue<string>()?.Trim();
                if (!string.IsNullOrEmpty(s)) draft.RoadBusStraightEdgeKeys.Add(s);
            }
        }

        if (obj["segmentSnaps"] is JsonArray snaps)
        {
            foreach (var snap in snaps.OfType<JsonObject>())
            {
                var from = snap["fromNodeId"]?.GetValue<string>()?.Trim();
                var to = snap["toNodeId"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) continue;
                var key = RoutePathDraft.SegmentEdgeKey(from, to);
                var pts = snap["points"] is JsonArray ptsArr ? ParsePoints(ptsArr) : [];
                if (pts.Count >= 2) draft.RoadSegmentPolylines[key] = pts;
                if (snap["maneuvers"] is JsonArray manArr)
                {
                    var parsed = ParseManeuvers(manArr);
                    if (parsed.Count > 0) draft.RoadSegmentManeuvers[key] = parsed;
                }
            }
        }

        if (draft.RoadSnappedEdgeKeys.Count == 0 && draft.SnappedShape.Count >= 2 && draft.Segments.Count > 0)
        {
            foreach (var seg in draft.Segments)
            {
                draft.RoadSnappedEdgeKeys.Add(RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId));
            }
        }

        return draft;
    }

    public static JsonObject ToJsonNode(RoutePathDraft draft)
    {
        var obj = new JsonObject
        {
            ["routeName"] = draft.RouteName,
            ["createdAtEpochMs"] = draft.CreatedAtEpochMs,
            ["updatedAtEpochMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["notes"] = draft.Notes,
            ["routeLineColor"] = NormalizeRouteLineColor(draft.RouteLineColor)
        };

        var nodes = new JsonArray();
        foreach (var node in draft.Nodes)
        {
            nodes.Add(new JsonObject
            {
                ["id"] = node.Id,
                ["type"] = node.Type.ToString(),
                ["title"] = node.Title,
                ["sourceStopName"] = node.SourceStopName,
                ["lat"] = node.Lat,
                ["lon"] = node.Lon
            });
        }
        obj["nodes"] = nodes;

        var segments = new JsonArray();
        foreach (var seg in draft.Segments.OrderBy(s => s.Order))
        {
            segments.Add(new JsonObject
            {
                ["order"] = seg.Order,
                ["fromNodeId"] = seg.FromNodeId,
                ["toNodeId"] = seg.ToNodeId
            });
        }
        obj["segments"] = segments;

        obj["snappedShape"] = WritePoints(draft.SnappedShape);
        obj["snappedManeuvers"] = WriteManeuvers(draft.SnappedManeuvers);

        var edgeKeys = new JsonArray();
        foreach (var key in draft.RoadSnappedEdgeKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            edgeKeys.AddString(key);
        }
        obj["roadSnappedEdgeKeys"] = edgeKeys;

        var busKeys = new JsonArray();
        foreach (var key in draft.RoadBusStraightEdgeKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            busKeys.AddString(key);
        }
        obj["roadBusStraightEdgeKeys"] = busKeys;

        var snaps = new JsonArray();
        foreach (var (key, pts) in draft.RoadSegmentPolylines)
        {
            var parts = key.Split('\u0001', 2);
            if (parts.Length != 2) continue;
            var snapObj = new JsonObject
            {
                ["fromNodeId"] = parts[0],
                ["toNodeId"] = parts[1],
                ["points"] = WritePoints(pts)
            };
            if (draft.RoadSegmentManeuvers.TryGetValue(key, out var mans))
            {
                snapObj["maneuvers"] = WriteManeuvers(mans);
            }
            snaps.Add(snapObj);
        }
        obj["segmentSnaps"] = snaps;

        return obj;
    }

    private static List<RoutePathLatLng> ParsePoints(JsonArray arr)
    {
        var list = new List<RoutePathLatLng>();
        foreach (var p in arr.OfType<JsonObject>())
        {
            var lat = p["lat"]?.GetValue<double>() ?? double.NaN;
            var lon = p["lon"]?.GetValue<double>() ?? double.NaN;
            if (!double.IsFinite(lat) || !double.IsFinite(lon)) continue;
            list.Add(new RoutePathLatLng { Lat = lat, Lon = lon });
        }
        return list;
    }

    private static JsonArray WritePoints(IEnumerable<RoutePathLatLng> points)
    {
        var arr = new JsonArray();
        foreach (var p in points)
        {
            arr.Add(new JsonObject { ["lat"] = p.Lat, ["lon"] = p.Lon });
        }
        return arr;
    }

    private static List<RoutePathSnapManeuver> ParseManeuvers(JsonArray arr)
    {
        var list = new List<RoutePathSnapManeuver>();
        foreach (var m in arr.OfType<JsonObject>())
        {
            var d = m["distanceM"]?.GetValue<double>() ?? double.NaN;
            var instruction = m["instruction"]?.GetValue<string>()?.Trim();
            if (!double.IsFinite(d) || string.IsNullOrEmpty(instruction)) continue;
            list.Add(new RoutePathSnapManeuver
            {
                DistanceM = d,
                Instruction = instruction,
                CurrentStreet = m["currentStreet"]?.GetValue<string>(),
                NextStreet = m["nextStreet"]?.GetValue<string>(),
                NavSymbolType = m["navSymbolType"]?.GetValue<string>()
            });
        }
        return list;
    }

    private static string NormalizeRouteLineColor(string? raw)
    {
        var s = raw?.Trim() ?? string.Empty;
        if (s.StartsWith('#') && s.Length is 4 or 7)
        {
            return s.ToLowerInvariant();
        }

        return "#2196f3";
    }

    private static JsonArray WriteManeuvers(IEnumerable<RoutePathSnapManeuver> maneuvers)
    {
        var arr = new JsonArray();
        foreach (var m in maneuvers)
        {
            arr.Add(new JsonObject
            {
                ["distanceM"] = m.DistanceM,
                ["instruction"] = m.Instruction,
                ["currentStreet"] = m.CurrentStreet,
                ["nextStreet"] = m.NextStreet,
                ["navSymbolType"] = m.NavSymbolType
            });
        }
        return arr;
    }
}

public static class RoutePathDraftBuilder
{
    public static RoutePathDraft CreateSeed(string routeName, IList<RouteStopItem> stops)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new RoutePathDraft
        {
            RouteName = routeName,
            CreatedAtEpochMs = now,
            UpdatedAtEpochMs = now,
            Nodes = BuildSeedNodes(stops),
            Notes = RoutePathDraft.DefaultNotes
        };
    }

    public static List<RoutePathNode> BuildSeedNodes(IList<RouteStopItem> stops)
    {
        var nodes = new List<RoutePathNode>();
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            if (TryParseLatLng(stop.StopCoordinates, out var stopLat, out var stopLon) ||
                TryParseLatLng(stop.GpsCoordinates, out stopLat, out stopLon))
            {
                nodes.Add(new RoutePathNode
                {
                    Id = $"stop_{i}",
                    Type = RoutePathNodeType.STOP,
                    Title = stop.Name,
                    SourceStopName = stop.Name,
                    Lat = stopLat,
                    Lon = stopLon
                });
            }

            if (TryParseLatLng(stop.GpsCoordinates, out var annLat, out var annLon))
            {
                nodes.Add(new RoutePathNode
                {
                    Id = $"announcement_{i}",
                    Type = RoutePathNodeType.ANNOUNCEMENT,
                    Title = $"Ansage: {stop.Name}",
                    SourceStopName = stop.Name,
                    Lat = annLat,
                    Lon = annLon
                });
            }
        }
        return nodes;
    }

    public static List<RoutePathSegment> BuildAutoSegments(IList<RoutePathNode> nodes)
    {
        var ordered = nodes
            .Where(n => n.Type is RoutePathNodeType.STOP or RoutePathNodeType.ANNOUNCEMENT)
            .OrderBy(NodeSortKey)
            .ToList();

        var firstStop = ordered.FirstOrDefault(n => n.Type == RoutePathNodeType.STOP)?.Id;
        var lastStop = ordered.LastOrDefault(n => n.Type == RoutePathNodeType.STOP)?.Id;
        var firstAnn = ordered.FirstOrDefault(n => n.Type == RoutePathNodeType.ANNOUNCEMENT)?.Id;
        var lastAnn = ordered.LastOrDefault(n => n.Type == RoutePathNodeType.ANNOUNCEMENT)?.Id;

        var segments = new List<RoutePathSegment>();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var from = ordered[i].Id;
            var to = ordered[i + 1].Id;
            if (IsForbiddenWrap(from, to, firstStop, lastStop) || IsForbiddenWrap(from, to, firstAnn, lastAnn))
            {
                continue;
            }
            segments.Add(new RoutePathSegment { Order = segments.Count + 1, FromNodeId = from, ToNodeId = to });
        }
        return segments;
    }

    private static bool IsForbiddenWrap(string from, string to, string? first, string? last) =>
        first is not null && last is not null && first != last &&
        ((from == first && to == last) || (from == last && to == first));

    private static int NodeSortKey(RoutePathNode node)
    {
        var parts = node.Id.Split('_');
        if (parts.Length >= 2 && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
        {
            return idx * 10 + (node.Type == RoutePathNodeType.STOP ? 0 : 1);
        }
        return int.MaxValue;
    }

    private static bool TryParseLatLng(string? raw, out double lat, out double lon) =>
        RouteCoordinateParser.TryParse(raw, out lat, out lon);
}
