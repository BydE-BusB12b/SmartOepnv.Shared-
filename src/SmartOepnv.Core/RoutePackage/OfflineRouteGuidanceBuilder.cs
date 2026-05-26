using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Entspricht <c>OfflineRouteGuidanceBuilder</c> (GPSAnsagen).</summary>
public static class OfflineRouteGuidanceBuilder
{
    private sealed class GuidanceDraft(string kind, string text)
    {
        public string Kind { get; } = kind;
        public string Text { get; } = text;
        public double Distance { get; set; } = -1;
        public int StopIndex { get; set; } = -1;
    }

    private sealed record ParsedDirectionLine(string Kind, string Text);

    private sealed record GuidanceSegment(int StartIdx, int EndIdx, double D0, double D1);

    public static OfflineRouteGuidance? Build(string routeKey, string directionsText, IEnumerable<RouteStopItem> allStops)
    {
        var stopsOnRoute = allStops.Where(s => s.RouteName == routeKey).ToList();
        var poly = stopsOnRoute
            .Select(s => ParsePoint(s.GpsCoordinates) ?? ParsePoint(s.StopCoordinates))
            .Where(p => p is not null)
            .Cast<OfflineGuidancePoint>()
            .ToList();

        if (poly.Count == 0)
        {
            return null;
        }

        var cumAtVertex = CumulativeDistances(poly);
        var totalLen = cumAtVertex.Length > 0 ? cumAtVertex[^1] : 0;

        var parsedLines = ParseDirectionLines(directionsText);
        if (parsedLines.Count == 0)
        {
            return null;
        }

        var drafts = new List<GuidanceDraft>();
        var lastStopIdx = -1;
        foreach (var line in parsedLines)
        {
            switch (line.Kind)
            {
                case "H":
                {
                    var idx = MatchStopIndex(line.Text, stopsOnRoute, lastStopIdx + 1);
                    if (idx >= 0)
                    {
                        lastStopIdx = idx;
                        var d = idx < cumAtVertex.Length ? cumAtVertex[idx] : totalLen;
                        drafts.Add(new GuidanceDraft("H", line.Text) { Distance = d, StopIndex = idx });
                    }
                    else
                    {
                        drafts.Add(new GuidanceDraft("H", line.Text));
                    }

                    break;
                }
                case "T":
                    drafts.Add(new GuidanceDraft("T", line.Text));
                    break;
                default:
                    drafts.Add(new GuidanceDraft("W", line.Text));
                    break;
            }
        }

        ResolveUnknownHalts(drafts, totalLen);
        SpaceTurnsBetweenAnchors(drafts, totalLen);

        var maneuvers = drafts
            .Where(d => d.Distance >= 0)
            .OrderBy(d => d.Distance)
            .Select(d => new OfflineGuidanceManeuver(d.Kind, d.Text, d.Distance))
            .ToList();

        if (maneuvers.Count == 0)
        {
            return null;
        }

        return new OfflineRouteGuidance
        {
            RouteKey = routeKey,
            Polyline = poly,
            Maneuvers = maneuvers,
            TotalLengthMeters = totalLen
        };
    }

    private static void ResolveUnknownHalts(List<GuidanceDraft> drafts, double totalLen)
    {
        var unknownIdx = drafts
            .Select((d, i) => (d, i))
            .Where(x => x.d.Kind == "H" && x.d.Distance < 0)
            .Select(x => x.i)
            .ToList();

        if (unknownIdx.Count == 0)
        {
            return;
        }

        var known = drafts
            .Select((d, i) => (d, i))
            .Where(x => x.d.Kind == "H" && x.d.Distance >= 0)
            .Select(x => (x.i, x.d.Distance))
            .OrderBy(x => x.Item2)
            .ToList();

        foreach (var u in unknownIdx)
        {
            var prevKnown = known.LastOrDefault(x => x.Item1 < u).Item2;
            var nextKnown = known.FirstOrDefault(x => x.Item1 > u).Item2;
            if (nextKnown == 0 && !known.Any(x => x.Item1 > u))
            {
                nextKnown = totalLen;
            }

            drafts[u].Distance = (prevKnown + nextKnown) / 2.0;
        }
    }

    private static void SpaceTurnsBetweenAnchors(List<GuidanceDraft> drafts, double totalLen)
    {
        var anchorIndices = drafts
            .Select((d, i) => (d, i))
            .Where(x => x.d.Kind == "H" && x.d.Distance >= 0)
            .Select(x => x.i)
            .ToList();

        var segments = new List<GuidanceSegment>();
        if (anchorIndices.Count == 0)
        {
            segments.Add(new GuidanceSegment(-1, drafts.Count, 0, totalLen));
        }
        else
        {
            segments.Add(new GuidanceSegment(-1, anchorIndices[0], 0, drafts[anchorIndices[0]].Distance));
            for (var a = 0; a < anchorIndices.Count - 1; a++)
            {
                var i0 = anchorIndices[a];
                var i1 = anchorIndices[a + 1];
                segments.Add(new GuidanceSegment(i0, i1, drafts[i0].Distance, drafts[i1].Distance));
            }

            segments.Add(new GuidanceSegment(
                anchorIndices[^1],
                drafts.Count,
                drafts[anchorIndices[^1]].Distance,
                totalLen));
        }

        foreach (var seg in segments)
        {
            var turnIndices = new List<int>();
            for (var i = seg.StartIdx + 1; i < seg.EndIdx; i++)
            {
                if (drafts[i].Kind != "H" && drafts[i].Distance < 0)
                {
                    turnIndices.Add(i);
                }
            }

            if (turnIndices.Count == 0)
            {
                continue;
            }

            var span = Math.Max(1.0, seg.D1 - seg.D0);
            for (var k = 0; k < turnIndices.Count; k++)
            {
                var frac = (k + 1.0) / (turnIndices.Count + 1.0);
                drafts[turnIndices[k]].Distance = seg.D0 + frac * span;
            }
        }
    }

    private static List<ParsedDirectionLine> ParseDirectionLines(string directionsText)
    {
        var outLines = new List<ParsedDirectionLine>();
        foreach (var raw in directionsText.Split('\n', '\r'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[H]", StringComparison.Ordinal))
            {
                outLines.Add(new ParsedDirectionLine("H", line[3..].Trim()));
                continue;
            }

            if (line.StartsWith("➡️", StringComparison.Ordinal))
            {
                outLines.Add(new ParsedDirectionLine("T", line[2..].Trim()));
                continue;
            }

            if (line.StartsWith("📍", StringComparison.Ordinal))
            {
                outLines.Add(new ParsedDirectionLine("W", line[2..].Trim()));
                continue;
            }

            outLines.Add(new ParsedDirectionLine("W", line));
        }

        return outLines;
    }

    private static string NormalizeName(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"\s+", " ").Trim();

    private static int MatchStopIndex(string haltName, IList<RouteStopItem> stops, int startFrom)
    {
        var n = NormalizeName(haltName);
        if (n.Length == 0)
        {
            return -1;
        }

        for (var i = Math.Max(0, startFrom); i < stops.Count; i++)
        {
            var sn = NormalizeName(stops[i].Name);
            if (sn == n || sn.Contains(n, StringComparison.Ordinal) || n.Contains(sn, StringComparison.Ordinal))
            {
                return i;
            }

            var disp = NormalizeName(stops[i].StopDisplay);
            if (disp.Length > 0 &&
                (disp == n || disp.Contains(n, StringComparison.Ordinal) || n.Contains(disp, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }

    private static OfflineGuidancePoint? ParsePoint(string? coords)
    {
        if (string.IsNullOrWhiteSpace(coords))
        {
            return null;
        }

        try
        {
            var parts = coords.Split(',');
            if (parts.Length < 2)
            {
                return null;
            }

            return new OfflineGuidancePoint(
                double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture),
                double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }

    private static double[] CumulativeDistances(IReadOnlyList<OfflineGuidancePoint> poly)
    {
        var arr = new double[poly.Count];
        for (var i = 1; i < poly.Count; i++)
        {
            arr[i] = arr[i - 1] + DistanceMeters(poly[i - 1], poly[i]);
        }

        return arr;
    }

    private static double DistanceMeters(OfflineGuidancePoint a, OfflineGuidancePoint b)
    {
        const double r = 6371000;
        var dLat = (b.Lat - a.Lat) * Math.PI / 180;
        var dLng = (b.Lng - a.Lng) * Math.PI / 180;
        var lat1 = a.Lat * Math.PI / 180;
        var lat2 = b.Lat * Math.PI / 180;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }
}
