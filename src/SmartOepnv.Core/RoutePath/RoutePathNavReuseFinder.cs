using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Findet in anderen Routen passende Haltestellenfolgen mit gesnappten Nav-Verbindungen (auch Teilabschnitte).
/// </summary>
public static class RoutePathNavReuseFinder
{
    private sealed record StopAnchor(int ListIndex, string Key, string Label);

    public static IReadOnlyList<RoutePathNavReuseCandidate> Find(
        EditableRoutePackage editor,
        JsonObject? packageRoot,
        string targetRouteKey,
        int minStopsInSegment = 2)
    {
        if (packageRoot is null || string.IsNullOrWhiteSpace(targetRouteKey))
        {
            return [];
        }

        var targetStops = editor.GetStops(targetRouteKey);
        var targetSeq = BuildStopSequence(targetStops);
        if (targetSeq.Count < minStopsInSegment)
        {
            return [];
        }

        var results = new List<RoutePathNavReuseCandidate>();
        foreach (var sourceRouteKey in editor.RouteNames)
        {
            if (string.Equals(sourceRouteKey, targetRouteKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceJson = RoutePathDraftRepository.TryGetDraftJson(packageRoot, sourceRouteKey);
            if (string.IsNullOrWhiteSpace(sourceJson))
            {
                continue;
            }

            RoutePathDraft sourceDraft;
            try
            {
                sourceDraft = RoutePathDraftSerializer.FromJson(sourceJson);
            }
            catch
            {
                continue;
            }

            var sourceStops = editor.GetStops(sourceRouteKey);
            var sourceSeq = BuildStopSequence(sourceStops);
            if (sourceSeq.Count < minStopsInSegment)
            {
                continue;
            }

            AppendMatchesForPair(
                results,
                sourceRouteKey,
                sourceDraft,
                sourceStops,
                sourceSeq,
                targetSeq,
                minStopsInSegment);
        }

        return SelectCandidatesForDialog(results);
    }

    private static void AppendMatchesForPair(
        List<RoutePathNavReuseCandidate> results,
        string sourceRouteKey,
        RoutePathDraft sourceDraft,
        IList<RouteStopItem> sourceStops,
        IReadOnlyList<StopAnchor> sourceSeq,
        IReadOnlyList<StopAnchor> targetSeq,
        int minStops)
    {
        for (var tgtLen = targetSeq.Count; tgtLen >= minStops; tgtLen--)
        {
            for (var tgtStart = 0; tgtStart <= targetSeq.Count - tgtLen; tgtStart++)
            {
                var tgtSlice = targetSeq.Skip(tgtStart).Take(tgtLen).Select(s => s.Key).ToList();
                for (var srcStart = 0; srcStart <= sourceSeq.Count - tgtLen; srcStart++)
                {
                    var matches = true;
                    for (var i = 0; i < tgtLen; i++)
                    {
                        if (!string.Equals(sourceSeq[srcStart + i].Key, tgtSlice[i], StringComparison.OrdinalIgnoreCase))
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (!matches)
                    {
                        continue;
                    }

                    var srcFirst = sourceSeq[srcStart].ListIndex;
                    var srcLast = sourceSeq[srcStart + tgtLen - 1].ListIndex;
                    var tgtFirst = targetSeq[tgtStart].ListIndex;
                    var tgtLast = targetSeq[tgtStart + tgtLen - 1].ListIndex;
                    var edgeCount = RoutePathNavReuseGraph.CountSnappedSegmentsBetween(
                        sourceDraft,
                        sourceStops,
                        srcFirst,
                        srcLast);
                    if (edgeCount == 0)
                    {
                        continue;
                    }

                    results.Add(new RoutePathNavReuseCandidate
                    {
                        SourceRouteKey = sourceRouteKey,
                        TargetFirstListIndex = tgtFirst,
                        TargetLastListIndex = tgtLast,
                        SourceFirstListIndex = srcFirst,
                        SourceLastListIndex = srcLast,
                        StopLabels = targetSeq.Skip(tgtStart).Take(tgtLen).Select(s => s.Label).ToList(),
                        SnappedEdgeCount = edgeCount
                    });
                }
            }
        }
    }

    /// <summary>
    /// Pro Ziel-Abschnitt alle Quell-Routen (sortiert nach Qualität). Kürzere überlappende
    /// Abschnitte entfallen; bei gleichem Abschnitt bleiben Alternativen (z. B. Fahrt 2112 vs. 2064).
    /// </summary>
    private static List<RoutePathNavReuseCandidate> SelectCandidatesForDialog(
        List<RoutePathNavReuseCandidate> all)
    {
        var byTargetRange = all
            .GroupBy(m => (m.TargetFirstListIndex, m.TargetLastListIndex))
            .OrderByDescending(g => g.First().StopLabels.Count)
            .ThenBy(g => g.Key.TargetFirstListIndex)
            .ToList();

        var occupied = new List<(int From, int To)>();
        var selected = new List<RoutePathNavReuseCandidate>();

        foreach (var group in byTargetRange)
        {
            var from = group.Key.TargetFirstListIndex;
            var to = group.Key.TargetLastListIndex;
            if (occupied.Any(r => RangesOverlap(r.From, r.To, from, to)))
            {
                continue;
            }

            var orderedSources = group
                .GroupBy(m => m.SourceRouteKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(m => m.SnappedEdgeCount)
                    .ThenByDescending(m => m.StopLabels.Count)
                    .First())
                .OrderByDescending(m => m.SnappedEdgeCount)
                .ThenByDescending(m => m.StopLabels.Count)
                .ThenBy(m => m.SourceRouteKey, StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToList();

            for (var i = 0; i < orderedSources.Count; i++)
            {
                var m = orderedSources[i];
                selected.Add(new RoutePathNavReuseCandidate
                {
                    SourceRouteKey = m.SourceRouteKey,
                    TargetFirstListIndex = m.TargetFirstListIndex,
                    TargetLastListIndex = m.TargetLastListIndex,
                    SourceFirstListIndex = m.SourceFirstListIndex,
                    SourceLastListIndex = m.SourceLastListIndex,
                    StopLabels = m.StopLabels,
                    SnappedEdgeCount = m.SnappedEdgeCount,
                    IsPreferredDefault = i == 0
                });
            }

            occupied.Add((from, to));
        }

        return selected
            .OrderBy(m => m.TargetFirstListIndex)
            .ThenByDescending(m => m.IsPreferredDefault)
            .ThenByDescending(m => m.SnappedEdgeCount)
            .ToList();
    }

    /// <summary>
    /// Beim Übernehmen: bei überlappenden Ziel-Abschnitten nur die beste Auswahl behalten.
    /// </summary>
    public static List<RoutePathNavReuseCandidate> SelectNonOverlappingBest(
        IEnumerable<RoutePathNavReuseCandidate> matches)
    {
        var ordered = matches
            .OrderByDescending(m => m.StopLabels.Count)
            .ThenByDescending(m => m.SnappedEdgeCount)
            .ThenBy(m => m.TargetFirstListIndex)
            .ToList();

        var picked = new List<RoutePathNavReuseCandidate>();
        var occupied = new List<(int From, int To)>();

        foreach (var candidate in ordered)
        {
            var from = candidate.TargetFirstListIndex;
            var to = candidate.TargetLastListIndex;
            if (occupied.Any(r => RangesOverlap(r.From, r.To, from, to)))
            {
                continue;
            }

            picked.Add(candidate);
            occupied.Add((from, to));
        }

        return picked.OrderBy(m => m.TargetFirstListIndex).ToList();
    }

    private static bool RangesOverlap(int aFrom, int aTo, int bFrom, int bTo) =>
        aFrom <= bTo && bFrom <= aTo;

    private static List<StopAnchor> BuildStopSequence(IList<RouteStopItem> stops)
    {
        var list = new List<StopAnchor>();
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            if (stop.IsWaypoint)
            {
                continue;
            }

            var key = ResolveStopKey(stop);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            list.Add(new StopAnchor(i, key, string.IsNullOrWhiteSpace(stop.Name) ? key : stop.Name.Trim()));
        }

        return list;
    }

    internal static string ResolveStopKey(RouteStopItem stop)
    {
        var code = PlannerStopCode.Normalize(stop.PlannerStopCode);
        if (PlannerStopCode.IsValid(code))
        {
            return $"code:{code}";
        }

        var name = NormalizeName(stop.Name);
        return string.IsNullOrWhiteSpace(name) ? string.Empty : $"name:{name}";
    }

    private static string NormalizeName(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();

    internal static HashSet<string> BuildNodeIdSet(int fromListIdx, int toListIdx)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        for (var i = fromListIdx; i <= toListIdx; i++)
        {
            allowed.Add($"stop_{i}");
            allowed.Add($"announcement_{i}");
        }

        return allowed;
    }
}
