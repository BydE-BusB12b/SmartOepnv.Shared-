using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;
using static SmartOepnv.Core.JsonNodeExtensions;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Erkennt kaputte Fahrwege (z. B. nach Nav-Übernahme verdoppelte Segmente):
/// Shape viel länger als Halteliste, Ende nicht an letzter Hst., parallele reuse-Kanten.
/// </summary>
public static class RoutePathDraftIntegrity
{
    /// <summary>Shape länger als Halteluftlinie × Faktor → Warnung.</summary>
    public const double MaxShapeToStopChainRatio = 2.4;

    /// <summary>Zusätzlicher Puffer über der Halteluftlinie (m).</summary>
    public const double MaxShapeExtraMeters = 4_500;

    /// <summary>Letzter Shape-Punkt darf max. so weit von letzter STOP-Hst. liegen.</summary>
    public const double MaxEndToLastStopMeters = 450;

    public sealed record Finding(
        string Code,
        string Message,
        double ShapeLengthMeters,
        double StopChainMeters);

    public static IReadOnlyList<Finding> Evaluate(RoutePathDraft? draft)
    {
        if (draft is null)
        {
            return [];
        }

        var findings = new List<Finding>();
        var shapeLen = PolylineLengthMeters(draft.SnappedShape);
        var stops = draft.Nodes
            .Where(n => n.Type == RoutePathNodeType.STOP)
            .OrderBy(StopListIndex)
            .ToList();
        var stopChain = StopChainLengthMeters(stops);

        if (shapeLen >= 50 && stopChain >= 50)
        {
            var limit = Math.Max(stopChain * MaxShapeToStopChainRatio, stopChain + MaxShapeExtraMeters);
            if (shapeLen > limit)
            {
                findings.Add(new Finding(
                    "SHAPE_TOO_LONG",
                    $"Fahrweg {FormatKm(shapeLen)} ist deutlich länger als die Halteliste (~{FormatKm(stopChain)}). " +
                    "Vermutlich doppelte/übernommene Segmente – neu snappen oder Navi bereinigen.",
                    shapeLen,
                    stopChain));
            }
        }

        if (draft.SnappedShape.Count >= 2 && stops.Count >= 1)
        {
            var lastStop = stops[^1];
            var end = draft.SnappedShape[^1];
            var endDist = RoutePathGeo.HaversineMeters(
                new RoutePathLatLng { Lat = end.Lat, Lon = end.Lon },
                new RoutePathLatLng { Lat = lastStop.Lat, Lon = lastStop.Lon });
            if (endDist > MaxEndToLastStopMeters)
            {
                findings.Add(new Finding(
                    "SHAPE_END_MISMATCH",
                    $"Fahrweg endet {endDist:0} m neben der letzten Haltestelle „{lastStop.SourceStopName ?? lastStop.Title}“ " +
                    $"(erwartet < {MaxEndToLastStopMeters:0} m). Shape stimmt nicht zur Halteliste.",
                    shapeLen,
                    stopChain));
            }
        }

        var duplicateLogicalEdges = CountDuplicateLogicalEdges(draft);
        if (duplicateLogicalEdges > 0)
        {
            findings.Add(new Finding(
                "DUPLICATE_SEGMENTS",
                $"{duplicateLogicalEdges} parallele Verbindung(en) (inkl. reuse_… nach Nav-Übernahme). " +
                "Doppelte Kanten verlängern den Fahrweg – bitte bereinigen und neu snappen.",
                shapeLen,
                stopChain));
        }

        return findings;
    }

    public static string? FormatWarning(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
        {
            return null;
        }

        return "⚠ Fahrweg-Prüfung: " + string.Join(" · ", findings.Select(f => f.Message));
    }

    public static string FormatLengthSummary(RoutePathDraft? draft)
    {
        if (draft is null || draft.SnappedShape.Count < 2)
        {
            return "Fahrweg: –";
        }

        var shapeLen = PolylineLengthMeters(draft.SnappedShape);
        var stops = draft.Nodes
            .Where(n => n.Type == RoutePathNodeType.STOP)
            .OrderBy(StopListIndex)
            .ToList();
        var chain = StopChainLengthMeters(stops);
        return chain >= 50
            ? $"Fahrweg {FormatKm(shapeLen)} (Halteluftlinie ~{FormatKm(chain)})"
            : $"Fahrweg {FormatKm(shapeLen)}";
    }

    /// <summary>Scannt alle Drafts im Paket; liefert RouteKey + erstes Finding je problematischer Route.</summary>
    public static IReadOnlyList<(string RouteKey, Finding Finding)> ScanPackage(JsonObject? packageRoot)
    {
        if (packageRoot?["routePathDrafts"] is not JsonObject drafts || drafts.Count == 0)
        {
            return [];
        }

        var list = new List<(string, Finding)>();
        foreach (var entry in drafts)
        {
            var text = DraftNodeToJsonText(entry.Value);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            RoutePathDraft draft;
            try
            {
                draft = RoutePathDraftSerializer.FromJson(text);
            }
            catch
            {
                continue;
            }

            var findings = Evaluate(draft);
            if (findings.Count == 0)
            {
                continue;
            }

            list.Add((entry.Key, findings[0]));
        }

        return list;
    }

    public static IReadOnlyList<Finding> EvaluatePackageRoute(JsonObject? packageRoot, string routeKey)
    {
        var json = RoutePathDraftRepository.TryGetDraftJson(packageRoot, routeKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return Evaluate(RoutePathDraftSerializer.FromJson(json));
        }
        catch
        {
            return [];
        }
    }

    public static double PolylineLengthMeters(IReadOnlyList<RoutePathLatLng> shape)
    {
        if (shape.Count < 2)
        {
            return 0;
        }

        var sum = 0.0;
        for (var i = 1; i < shape.Count; i++)
        {
            sum += RoutePathGeo.HaversineMeters(shape[i - 1], shape[i]);
        }

        return sum;
    }

    private static double StopChainLengthMeters(IReadOnlyList<RoutePathNode> stops)
    {
        if (stops.Count < 2)
        {
            return 0;
        }

        var sum = 0.0;
        for (var i = 1; i < stops.Count; i++)
        {
            sum += RoutePathGeo.HaversineMeters(
                new RoutePathLatLng { Lat = stops[i - 1].Lat, Lon = stops[i - 1].Lon },
                new RoutePathLatLng { Lat = stops[i].Lat, Lon = stops[i].Lon });
        }

        return sum;
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

    private static int CountDuplicateLogicalEdges(RoutePathDraft draft)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var seg in draft.Segments)
        {
            var key = $"{NormalizeNodeId(seg.FromNodeId)}\u0001{NormalizeNodeId(seg.ToNodeId)}";
            counts.TryGetValue(key, out var n);
            counts[key] = n + 1;
        }

        return counts.Values.Count(c => c > 1);
    }

    /// <summary>reuse_manual_123 und manual_123 gelten als dieselbe logische Kante.</summary>
    private static string NormalizeNodeId(string nodeId)
    {
        var id = nodeId.Trim();
        while (id.StartsWith("reuse_", StringComparison.OrdinalIgnoreCase))
        {
            id = id["reuse_".Length..];
        }

        return id;
    }

    private static string FormatKm(double meters) =>
        meters >= 1000 ? $"{meters / 1000.0:0.##} km" : $"{meters:0} m";
}
