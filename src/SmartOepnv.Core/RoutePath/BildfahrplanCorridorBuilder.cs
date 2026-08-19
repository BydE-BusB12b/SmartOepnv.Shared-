using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Korridor-Achse für den Bildfahrplan unter Einbeziehung der Routenwechsel-Verknüpfungen.
/// Y-Beschriftung = nur Halte der Referenzfahrt (keine Fremdäste wie Kaarst/Neuss).
/// </summary>
public static class BildfahrplanCorridorBuilder
{
    /// <summary>Max. Abstand zur Snap-Linie, damit ein Halt für Plot-Meter zählt.</summary>
    private const double MaxSnapProjectionMeters = 120;

    /// <summary>Mindestabstand zweier Achsen-Halte (sonst lesbare Labels übereinander).</summary>
    private const double MinAxisSeparationMeters = 180;

    /// <summary>Fallback, wenn kein Snap zwischen Bahnhof und Pause messbar ist.</summary>
    private const double DefaultPauseParentSnapMeters = 650;

    /// <summary>Fallback Depotausfahrt/Depoteinfahrt ↔ benachbarter Halt.</summary>
    private const double DefaultDepotGapMeters = 500;

    public sealed record StopOnAxis(
        string DisplayName,
        double DistanceMeters,
        string? PlannerStopCode,
        string? VrrStopId);

    public sealed record CorridorResult(
        IReadOnlyList<StopOnAxis> Stations,
        double TotalMeters,
        bool UsedSnappedPath,
        string ReferenceRouteKey,
        bool FlippedForChain,
        IReadOnlyList<IReadOnlyList<string>> Chains,
        IReadOnlyDictionary<string, double> MetersByLookupKey);

    public static CorridorResult Build(EditableRoutePackage editor, string corridor, IReadOnlyList<string> routeKeys)
    {
        var chains = BuildChains(editor, routeKeys);
        var longestChain = chains
            .OrderByDescending(c => c.Count)
            .ThenByDescending(c => SumSnapLength(editor, c))
            .FirstOrDefault() ?? routeKeys.ToList();

        var refPool = longestChain.Count > 0 ? longestChain : routeKeys.ToList();
        // Bevorzuge Fahrt mit den meisten zeitlichen Halten + gutem Snap (Hauptkorridor)
        var (refKey, refDraft, refStops, snapLen) = PickBestCorridorReference(editor, refPool);
        if (refKey is null)
        {
            (refKey, refDraft, refStops, snapLen) = PickBestCorridorReference(editor, routeKeys);
        }

        refKey ??= routeKeys[0];
        refStops ??= editor.GetStops(refKey);
        refDraft ??= RoutePathDraftRepository.LoadOrCreate(refKey, refStops, editor.PackageRoot);

        var baseAxis = BildfahrplanStopAxis.Build(refKey, refDraft, refStops);
        var meters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var stations = new List<StopOnAxis>();

        // Nur Referenz-Halte als Achsenbeschriftung
        var referenceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in baseAxis.Stations)
        {
            var name = BildfahrplanStopAxis.NormalizeName(s.Name);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            meters[name] = s.DistanceMeters;
            stations.Add(new StopOnAxis(name, s.DistanceMeters, null, null));
            referenceKeys.Add(BildfahrplanStopAxis.MatchKey(name));
        }

        // Lookup-Keys der Referenzhalte ergänzen
        foreach (var stop in refStops.Where(BildfahrplanStopAxis.IsAxisLabelStop))
        {
            var name = BildfahrplanStopAxis.AxisDisplayName(stop);
            if (string.IsNullOrEmpty(name) || !meters.TryGetValue(name, out var m))
            {
                continue;
            }

            foreach (var key in LookupKeys(stop))
            {
                meters[key] = m;
            }
        }

        // Fehlende Korridor-Halte (Morper Str., Pause, Depot, …) – keine Fremdäste (Neuss/Kaarst)
        MergeMissingCorridorStops(editor, routeKeys, stations, meters, refDraft.SnappedShape, referenceKeys);
        PruneOffCorridorStations(stations, meters, referenceKeys);

        DeduplicateAliasStations(stations, meters);
        PlacePauseStopsAfterParent(editor, routeKeys, stations, meters);
        stations = EnforceMinimumSeparation(stations);

        // Lookup neu aufbauen (nach Separation), damit Plot-Meter = Label-Meter
        RebuildMetersLookup(editor, routeKeys, refStops, stations, meters);

        var shape = refDraft.SnappedShape;
        var total = Math.Max(
            baseAxis.TotalMeters,
            stations.Count == 0 ? 0 : stations.Max(s => s.DistanceMeters));
        if (total < 1)
        {
            total = Math.Max(1, stations.Count - 1) * 1000;
        }

        // Letzten Halt an total koppeln (Separation kann über baseAxis hinausgehen)
        if (stations.Count > 0 && stations[^1].DistanceMeters > total)
        {
            total = stations[^1].DistanceMeters;
        }

        // Orientierung: Start der längsten Schnur oben
        var flip = false;
        if (longestChain.Count > 0)
        {
            var firstTripStops = editor.GetStops(longestChain[0])
                .Where(BildfahrplanStopAxis.IsAxisLabelStop)
                .ToList();
            var first = firstTripStops.FirstOrDefault();
            if (first is not null && TryResolveMeters(first, meters, shape, out var firstM))
            {
                if (firstM < total * 0.45)
                {
                    flip = true;
                }
            }
        }

        if (flip)
        {
            // Meter spiegeln, Listenreihenfolge behalten (kein OrderBy – sonst wieder verdreht)
            stations = stations
                .Select(s => s with { DistanceMeters = total - s.DistanceMeters })
                .ToList();
            var flipped = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in meters)
            {
                flipped[pair.Key] = total - pair.Value;
            }

            meters = flipped;
        }

        // Nach Orientierung: Pause, Morper, Depot festsetzen (nach Flip-Meter)
        PlacePauseBeyondParentAfterOrient(editor, routeKeys, stations, meters, total);
        FixMorperBeyondGerresheim(stations, meters);
        EnsureDepotStopsOnAxis(editor, routeKeys, stations, meters);
        PlaceDepotStopsBeyondNeighbor(editor, routeKeys, stations, meters);
        stations = EnforceMinimumSeparation(stations);
        // Pause/Morper/Depot-Abstände nach Separation erneut absichern
        PlacePauseBeyondParentAfterOrient(editor, routeKeys, stations, meters, Math.Max(total, stations.Max(s => s.DistanceMeters)));
        FixMorperBeyondGerresheim(stations, meters);
        RebuildMetersLookup(editor, routeKeys, refStops, stations, meters);

        if (stations.Count > 0)
        {
            total = Math.Max(total, stations.Max(s => s.DistanceMeters));
        }

        return new CorridorResult(
            stations,
            total,
            baseAxis.UsedSnappedPath || snapLen > 0,
            refKey,
            flip,
            chains,
            meters);
    }

    /// <summary>Mindestabstand entlang der Achse – Reihenfolge der Liste bleibt erhalten.</summary>
    private static List<StopOnAxis> EnforceMinimumSeparation(List<StopOnAxis> stations)
    {
        if (stations.Count < 2)
        {
            return stations;
        }

        // Nicht nach Meter sortieren – das verdreht Zoo/Pause etc.
        // Richtung aus Mehrheit der Intervalle (nicht nur erstem/letztem Halt –
        // sonst kippt ein Depot am Ende die Erkennung und Quetscht die Achse).
        var ordered = stations.ToList();
        var up = 0;
        var down = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            var d = ordered[i].DistanceMeters - ordered[i - 1].DistanceMeters;
            if (d > 1)
            {
                up++;
            }
            else if (d < -1)
            {
                down++;
            }
        }

        var increasing = up >= down;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (increasing)
            {
                var min = ordered[i - 1].DistanceMeters + MinAxisSeparationMeters;
                if (ordered[i].DistanceMeters < min)
                {
                    ordered[i] = ordered[i] with { DistanceMeters = min };
                }
            }
            else
            {
                var max = ordered[i - 1].DistanceMeters - MinAxisSeparationMeters;
                if (ordered[i].DistanceMeters > max)
                {
                    ordered[i] = ordered[i] with { DistanceMeters = max };
                }
            }
        }

        // Negative Meter nach fallender Separation vermeiden: auf ≥0 schieben
        if (!increasing)
        {
            var minM = ordered.Min(s => s.DistanceMeters);
            if (minM < 0)
            {
                ordered = ordered
                    .Select(s => s with { DistanceMeters = s.DistanceMeters - minM })
                    .ToList();
            }
        }

        return ordered;
    }

    /// <summary>
    /// Fügt fehlende Korridor-Halte ein:
    /// 1) per GPS zwischen zwei Achsenhalte,
    /// 2) per Fahrten-Nachbarn (auch vor dem ersten bekannten Halt, z. B. Wendefahrt Morper → Gerresheim).
    /// </summary>
    private static void MergeMissingCorridorStops(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        IReadOnlyList<RoutePathLatLng> snappedShape,
        HashSet<string> referenceKeys)
    {
        MergeBySnapBetweenAxisStops(editor, routeKeys, stations, meters, snappedShape, referenceKeys);
        MergeByTripNeighbors(editor, routeKeys, stations, meters, referenceKeys);
        MergeBySnapBetweenAxisStops(editor, routeKeys, stations, meters, snappedShape, referenceKeys);
    }

    private static void MergeBySnapBetweenAxisStops(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        IReadOnlyList<RoutePathLatLng> snappedShape,
        HashSet<string> referenceKeys)
    {
        if (snappedShape.Count < 2 || stations.Count < 2)
        {
            return;
        }

        foreach (var routeKey in routeKeys)
        {
            if (!TripOverlapsCorridor(editor, routeKey, stations) &&
                !TripHasDepotOrSpecial(editor, routeKey))
            {
                continue;
            }

            foreach (var stop in editor.GetStops(routeKey).Where(BildfahrplanStopAxis.IsAxisLabelStop))
            {
                var name = BildfahrplanStopAxis.AxisDisplayName(stop);
                if (string.IsNullOrEmpty(name) || AxisContainsName(stations, name))
                {
                    continue;
                }

                if (IsOffCorridorBranchName(name) &&
                    !referenceKeys.Contains(BildfahrplanStopAxis.MatchKey(name)))
                {
                    continue;
                }

                if (!TryParseLatLon(stop, out var lat, out var lon))
                {
                    continue;
                }

                if (!BildfahrplanStopAxis.TryDistanceAlongPolyline(
                        snappedShape,
                        new RoutePathLatLng { Lat = lat, Lon = lon },
                        out var along,
                        out var distToPath) ||
                    distToPath > MaxSnapProjectionMeters)
                {
                    continue;
                }

                // Wendefahrt Morper: nicht per GPS zwischen Gerresheim/Erkrath – Fahrtenreihenfolge gilt
                if (BildfahrplanStopAxis.MatchKey(name)
                        .Contains("morper", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var axisMin = stations.Min(s => s.DistanceMeters);
                var axisMax = stations.Max(s => s.DistanceMeters);

                // Nur Depot vor/hinter dem Achsenende per GPS; sonst Querschläger
                if (along < axisMin - 1 || along > axisMax + 1)
                {
                    if (!IsDepotLabel(name))
                    {
                        continue;
                    }

                    InsertStation(stations, meters, stop, name, along, InsertIndexForMeters(stations, along));
                    continue;
                }

                // Zwischen zwei aufeinanderfolgende Achsenhalte legen
                for (var i = 0; i < stations.Count - 1; i++)
                {
                    var a = stations[i].DistanceMeters;
                    var b = stations[i + 1].DistanceMeters;
                    var lo = Math.Min(a, b);
                    var hi = Math.Max(a, b);
                    if (along <= lo + 1 || along >= hi - 1)
                    {
                        continue;
                    }

                    InsertStation(stations, meters, stop, name, along, i + 1);
                    break;
                }
            }
        }
    }

    private static void MergeByTripNeighbors(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        HashSet<string> referenceKeys)
    {
        const int maxUnknownBetween = 4;
        var guard = 0;
        bool inserted;
        do
        {
            inserted = false;
            guard++;
            foreach (var routeKey in routeKeys)
            {
                if (!TripOverlapsCorridor(editor, routeKey, stations) &&
                    !TripHasDepotOrSpecial(editor, routeKey))
                {
                    continue;
                }

                var seq = editor.GetStops(routeKey)
                    .Where(BildfahrplanStopAxis.IsAxisLabelStop)
                    .ToList();
                if (seq.Count < 2)
                {
                    continue;
                }

                for (var i = 0; i < seq.Count; i++)
                {
                    var name = BildfahrplanStopAxis.AxisDisplayName(seq[i]);
                    if (string.IsNullOrEmpty(name) || AxisContainsName(stations, name))
                    {
                        continue;
                    }

                    if (IsOffCorridorBranchName(name) &&
                        !referenceKeys.Contains(BildfahrplanStopAxis.MatchKey(name)))
                    {
                        continue;
                    }

                    var prevTrip = -1;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        var pn = BildfahrplanStopAxis.AxisDisplayName(seq[j]);
                        if (AxisContainsName(stations, pn))
                        {
                            prevTrip = j;
                            break;
                        }
                    }

                    var nextTrip = -1;
                    for (var j = i + 1; j < seq.Count; j++)
                    {
                        var nn = BildfahrplanStopAxis.AxisDisplayName(seq[j]);
                        if (AxisContainsName(stations, nn))
                        {
                            nextTrip = j;
                            break;
                        }
                    }

                    // Fall A: zwischen zwei bekannten Achsenhalten
                    if (prevTrip >= 0 && nextTrip >= 0)
                    {
                        var unknownCount = nextTrip - prevTrip - 1;
                        if (unknownCount < 1 || unknownCount > maxUnknownBetween)
                        {
                            continue;
                        }

                        var prevName = BildfahrplanStopAxis.AxisDisplayName(seq[prevTrip]);
                        var nextName = BildfahrplanStopAxis.AxisDisplayName(seq[nextTrip]);
                        var prevAxis = IndexOfName(stations, prevName);
                        var nextAxis = IndexOfName(stations, nextName);
                        if (prevAxis < 0 || nextAxis < 0 || Math.Abs(nextAxis - prevAxis) != 1)
                        {
                            continue;
                        }

                        if (InsertBetweenAxis(stations, meters, seq, prevTrip, nextTrip, prevAxis, nextAxis))
                        {
                            inserted = true;
                            break;
                        }
                    }

                    // Fall B: Wendefahrt vor erstem bekannten Halt (Morper → Gerresheim)
                    if (prevTrip < 0 && nextTrip >= 0)
                    {
                        var nextName = BildfahrplanStopAxis.AxisDisplayName(seq[nextTrip]);
                        var nextAxis = IndexOfName(stations, nextName);
                        if (nextAxis < 0 || i != nextTrip - 1)
                        {
                            continue;
                        }

                        if (TryInsertDeadheadBeforeKnown(
                                stations, meters, seq[i], name, nextName, nextAxis))
                        {
                            inserted = true;
                            break;
                        }
                    }

                    // Fall C: nach letztem bekannten Halt (Depoteinfahrt o. Ä.)
                    if (prevTrip >= 0 && nextTrip < 0)
                    {
                        var prevName = BildfahrplanStopAxis.AxisDisplayName(seq[prevTrip]);
                        var prevAxis = IndexOfName(stations, prevName);
                        if (prevAxis < 0 || i != prevTrip + 1)
                        {
                            continue;
                        }

                        if (TryInsertDeadheadAfterKnown(
                                stations, meters, seq[i], name, prevAxis))
                        {
                            inserted = true;
                            break;
                        }
                    }
                }

                if (inserted)
                {
                    break;
                }
            }
        } while (inserted && guard < 40);
    }

    /// <summary>
    /// Wendefahrt vor dem ersten bekannten Halt (Fahrtenreihenfolge): auf der Achse
    /// auf der Seite von Gerresheim, die von Erkrath weg zeigt (Richtung Düsseldorf),
    /// nicht zwischen Gerresheim und Erkrath.
    /// </summary>
    private static bool TryInsertDeadheadBeforeKnown(
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        RouteStopItem stop,
        string displayName,
        string knownName,
        int knownAxisIndex)
    {
        if (knownAxisIndex < 0 || knownAxisIndex >= stations.Count)
        {
            return false;
        }

        var key = BildfahrplanStopAxis.MatchKey(displayName);
        var knownKey = BildfahrplanStopAxis.MatchKey(knownName);
        var mKnown = stations[knownAxisIndex].DistanceMeters;

        // Morper vor Gerresheim: auf der Düsseldorf-Seite von Gerresheim (weg von Erkrath)
        if (key.Contains("morper", StringComparison.OrdinalIgnoreCase) &&
            knownKey.Contains("gerresheim", StringComparison.OrdinalIgnoreCase))
        {
            var erkrath = IndexOfNameContaining(stations, "erkrath");
            var flingern = IndexOfNameContaining(stations, "flingern");
            var hbf = IndexOfNameContaining(stations, "hbf");
            var towardDuesseldorf = 1.0;
            if (erkrath >= 0)
            {
                var mErk = stations[erkrath].DistanceMeters;
                towardDuesseldorf = Math.Sign(mKnown - mErk);
                if (towardDuesseldorf == 0)
                {
                    towardDuesseldorf = 1;
                }
            }

            double m;
            int insertAt;
            // Bevorzugt Mitte zwischen Flingern/Hbf und Gerresheim
            var outer = flingern >= 0 ? flingern : hbf;
            if (outer >= 0)
            {
                var mOuter = stations[outer].DistanceMeters;
                m = (mOuter + mKnown) / 2.0;
                insertAt = InsertIndexBetweenMeters(stations, mOuter, mKnown);
            }
            else
            {
                m = mKnown + towardDuesseldorf * MinAxisSeparationMeters;
                insertAt = InsertIndexForMeters(stations, m);
            }

            InsertStation(stations, meters, stop, displayName, m, insertAt);
            return true;
        }

        // Allgemein: Vorgänger vor dem bekannten Halt in Fahrtenreihenfolge
        // → auf der Seite weg vom nächsten Achsen-Nachbarn Richtung Strecke
        double mGeneral;
        int insertGeneral;
        if (knownAxisIndex > 0)
        {
            var mPrev = stations[knownAxisIndex - 1].DistanceMeters;
            // „Vor“ in Listenrichtung: zwischen prev und known nur wenn prev nicht Erkrath-Seite ist
            mGeneral = mKnown + Math.Sign(mKnown - mPrev) * MinAxisSeparationMeters;
            insertGeneral = InsertIndexForMeters(stations, mGeneral);
        }
        else if (stations.Count > 1)
        {
            var mNext = stations[1].DistanceMeters;
            mGeneral = mKnown + Math.Sign(mKnown - mNext) * MinAxisSeparationMeters;
            insertGeneral = InsertIndexForMeters(stations, mGeneral);
        }
        else
        {
            mGeneral = Math.Max(0, mKnown - MinAxisSeparationMeters);
            insertGeneral = 0;
        }

        InsertStation(stations, meters, stop, displayName, mGeneral, insertGeneral);
        return true;
    }

    /// <summary>
    /// Halt nach dem letzten bekannten Achsenhalt (z. B. Depoteinfahrt) – über das Achsenende hinaus.
    /// </summary>
    private static bool TryInsertDeadheadAfterKnown(
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        RouteStopItem stop,
        string displayName,
        int knownAxisIndex)
    {
        if (knownAxisIndex < 0 || knownAxisIndex >= stations.Count)
        {
            return false;
        }

        var mKnown = stations[knownAxisIndex].DistanceMeters;
        var corridorMid = stations.Average(s => s.DistanceMeters);
        var dir = Math.Sign(mKnown - corridorMid);
        if (dir == 0)
        {
            dir = knownAxisIndex >= stations.Count / 2 ? 1 : -1;
        }

        var gap = IsDepotLabel(displayName) ? DefaultDepotGapMeters : MinAxisSeparationMeters;
        var m = mKnown + dir * gap;
        var insertAt = InsertIndexForMeters(stations, m);
        InsertStation(stations, meters, stop, displayName, m, insertAt);
        return true;
    }

    /// <summary>
    /// Depotausfahrt/Depoteinfahrt klar jenseits des benachbarten Linienhalts (nicht auf Hbf).
    /// </summary>
    private static void PlaceDepotStopsBeyondNeighbor(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        List<StopOnAxis> stations,
        Dictionary<string, double> meters)
    {
        if (stations.Count < 2)
        {
            return;
        }

        var lineStops = stations.Where(s => !IsDepotLabel(s.DisplayName)).ToList();
        if (lineStops.Count == 0)
        {
            return;
        }

        var corridorMid = lineStops.Average(s => s.DistanceMeters);

        for (var i = 0; i < stations.Count; i++)
        {
            var depotName = stations[i].DisplayName;
            if (!IsDepotLabel(depotName))
            {
                continue;
            }

            var neighborIdx = FindNearestNonDepotIndex(stations, i);
            if (neighborIdx < 0)
            {
                continue;
            }

            var neighbor = stations[neighborIdx];
            var neighborM = neighbor.DistanceMeters;
            // Weg vom Korridormittelpunkt = „außerhalb“ (Depot hinter Hbf)
            var dir = Math.Sign(neighborM - corridorMid);
            if (dir == 0)
            {
                dir = IsDepotEinfahrt(depotName) ? -1 : 1;
            }

            var measured = TryMeasureSnapBetweenStops(
                editor, routeKeys, neighbor.DisplayName, depotName);
            var gap = measured is >= 80 ? measured.Value : DefaultDepotGapMeters;

            var depotM = neighborM + dir * gap;
            var depot = stations[i] with { DistanceMeters = depotM };
            stations.RemoveAt(i);
            if (i < neighborIdx)
            {
                neighborIdx--;
            }

            var insertAt = InsertIndexForMeters(stations, depotM);
            // Direkt außen neben dem Nachbarn halten
            if (depotM >= neighborM)
            {
                insertAt = Math.Max(insertAt, neighborIdx + 1);
            }
            else
            {
                insertAt = Math.Min(insertAt, neighborIdx);
            }

            insertAt = Math.Clamp(insertAt, 0, stations.Count);
            stations.Insert(insertAt, depot);
            meters[depot.DisplayName] = depotM;
            meters[BildfahrplanStopAxis.MatchKey(depot.DisplayName)] = depotM;
            i = Math.Max(i, insertAt);
        }
    }

    private static int FindNearestNonDepotIndex(List<StopOnAxis> stations, int depotIndex)
    {
        var best = -1;
        var bestDist = double.MaxValue;
        for (var p = 0; p < stations.Count; p++)
        {
            if (p == depotIndex || IsDepotLabel(stations[p].DisplayName))
            {
                continue;
            }

            var d = Math.Abs(stations[p].DistanceMeters - stations[depotIndex].DistanceMeters);
            // Listen-Nachbarn leicht bevorzugen
            if (Math.Abs(p - depotIndex) == 1)
            {
                d *= 0.5;
            }

            if (d < bestDist)
            {
                bestDist = d;
                best = p;
            }
        }

        return best;
    }

    /// <summary>Einfügeindex so, dass die Meter-Reihenfolge der Liste möglichst monoton bleibt.</summary>
    private static int InsertIndexForMeters(List<StopOnAxis> stations, double meters)
    {
        if (stations.Count == 0)
        {
            return 0;
        }

        var increasing = stations.Count < 2 ||
                         stations[^1].DistanceMeters >= stations[0].DistanceMeters - 1;
        if (increasing)
        {
            for (var i = 0; i < stations.Count; i++)
            {
                if (meters < stations[i].DistanceMeters)
                {
                    return i;
                }
            }

            return stations.Count;
        }

        for (var i = 0; i < stations.Count; i++)
        {
            if (meters > stations[i].DistanceMeters)
            {
                return i;
            }
        }

        return stations.Count;
    }

    private static int InsertIndexBetweenMeters(List<StopOnAxis> stations, double a, double b)
    {
        var mid = (a + b) / 2.0;
        return InsertIndexForMeters(stations, mid);
    }

    private static bool InsertBetweenAxis(
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        List<RouteStopItem> seq,
        int prevTrip,
        int nextTrip,
        int prevAxis,
        int nextAxis)
    {
        var lo = Math.Min(prevAxis, nextAxis);
        var hi = Math.Max(prevAxis, nextAxis);
        var m0 = stations[lo].DistanceMeters;
        var m1 = stations[hi].DistanceMeters;
        var missings = new List<RouteStopItem>();
        for (var u = prevTrip + 1; u < nextTrip; u++)
        {
            var un = BildfahrplanStopAxis.AxisDisplayName(seq[u]);
            if (!string.IsNullOrEmpty(un) && !AxisContainsName(stations, un))
            {
                missings.Add(seq[u]);
            }
        }

        if (missings.Count == 0)
        {
            return false;
        }

        if (prevAxis > nextAxis)
        {
            missings.Reverse();
        }

        var any = false;
        for (var mi = 0; mi < missings.Count; mi++)
        {
            var fraction = (mi + 1) / (double)(missings.Count + 1);
            var m = m0 + fraction * (m1 - m0);
            var dn = BildfahrplanStopAxis.AxisDisplayName(missings[mi]);
            if (AxisContainsName(stations, dn))
            {
                continue;
            }

            InsertStation(stations, meters, missings[mi], dn, m, lo + 1 + mi);
            any = true;
        }

        return any;
    }

    private static void InsertStation(
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        RouteStopItem stop,
        string displayName,
        double distanceMeters,
        int insertAt)
    {
        insertAt = Math.Clamp(insertAt, 0, stations.Count);
        var item = new StopOnAxis(
            displayName,
            distanceMeters,
            string.IsNullOrWhiteSpace(stop.PlannerStopCode) ? null : stop.PlannerStopCode.Trim(),
            string.IsNullOrWhiteSpace(stop.VrrStopId) ? null : stop.VrrStopId.Trim());
        stations.Insert(insertAt, item);
        meters[displayName] = distanceMeters;
        var match = BildfahrplanStopAxis.MatchKey(displayName);
        meters[match] = distanceMeters;
        foreach (var lk in LookupKeys(stop))
        {
            meters[lk] = distanceMeters;
        }
    }

    private static int IndexOfNameContaining(List<StopOnAxis> stations, string fragment)
    {
        for (var i = 0; i < stations.Count; i++)
        {
            if (BildfahrplanStopAxis.MatchKey(stations[i].DisplayName)
                .Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Nach Flip: Pause hinter dem Parent Richtung Wuppertal-Ende (Zoo/Steinbeck),
    /// Abstand = gesnappte Strecke (Fallback ~650 m). Nie „vor“ dem Bahnhof Richtung Düsseldorf.
    /// </summary>
    private static void PlacePauseBeyondParentAfterOrient(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        double totalMeters)
    {
        for (var i = 0; i < stations.Count; i++)
        {
            if (!IsPauseLabel(stations[i].DisplayName))
            {
                continue;
            }

            var parentKey = PauseParentMatchKey(stations[i].DisplayName);
            var parentIdx = FindParentStationIndex(stations, parentKey, i);
            if (parentIdx < 0)
            {
                continue;
            }

            var parentM = stations[parentIdx].DistanceMeters;
            var gap = ResolvePauseParentGapMeters(
                editor,
                routeKeys,
                stations[parentIdx].DisplayName,
                stations[i].DisplayName);
            if (gap < 200)
            {
                gap = DefaultPauseParentSnapMeters;
            }

            // Richtung: vom Korridor-Mittelpunkt weg bzw. zu Zoo/Steinbeck (Wendeende)
            var dir = ResolvePauseDirectionAwayFromCorridor(stations, parentIdx, parentM);
            var pauseM = parentM + dir * gap;
            pauseM = Math.Clamp(pauseM, 0, Math.Max(totalMeters, parentM + gap) + gap);

            var pause = stations[i] with { DistanceMeters = pauseM };
            stations.RemoveAt(i);
            if (i < parentIdx)
            {
                parentIdx--;
            }

            // In Listenreihenfolge direkt hinter dem Parent (Fahrtrichtung zum Wendeende)
            var insertAt = parentIdx + 1;
            stations.Insert(Math.Min(stations.Count, insertAt), pause);
            meters[pause.DisplayName] = pauseM;
            meters[BildfahrplanStopAxis.MatchKey(pause.DisplayName)] = pauseM;
            i = insertAt;
        }
    }

    /// <summary>
    /// Vorzeichen: Pause liegt jenseits von Vohwinkel Richtung Wuppertal-Ende (niedrige oder hohe Meter).
    /// </summary>
    private static double ResolvePauseDirectionAwayFromCorridor(
        List<StopOnAxis> stations,
        int parentIdx,
        double parentM)
    {
        var tip = IndexOfNameContaining(stations, "steinbeck");
        if (tip < 0)
        {
            tip = IndexOfNameContaining(stations, "zoo");
        }

        if (tip < 0)
        {
            tip = IndexOfNameContaining(stations, "hbf");
            // Mehrere Hbf: Wuppertal Hbf bevorzugen
            for (var t = 0; t < stations.Count; t++)
            {
                var k = BildfahrplanStopAxis.MatchKey(stations[t].DisplayName);
                if (k.Contains("wuppertal", StringComparison.OrdinalIgnoreCase) &&
                    k.Contains("hbf", StringComparison.OrdinalIgnoreCase))
                {
                    tip = t;
                    break;
                }
            }
        }

        if (tip >= 0 && tip != parentIdx)
        {
            var d = Math.Sign(stations[tip].DistanceMeters - parentM);
            if (d != 0)
            {
                return d;
            }
        }

        var line = stations
            .Where(s => !IsPauseLabel(s.DisplayName) && !IsDepotLabel(s.DisplayName))
            .Select(s => s.DistanceMeters)
            .ToList();
        if (line.Count == 0)
        {
            return parentM <= stations.Average(s => s.DistanceMeters) ? -1 : 1;
        }

        var mid = line.Average();
        var away = Math.Sign(parentM - mid);
        return away == 0 ? -1 : away;
    }

    /// <summary>Morper Str. klar auf Düsseldorf-Seite von Gerresheim (nicht deckungsgleich).</summary>
    private static void FixMorperBeyondGerresheim(
        List<StopOnAxis> stations,
        Dictionary<string, double> meters)
    {
        var morperIdx = -1;
        var gerresheimIdx = -1;
        for (var i = 0; i < stations.Count; i++)
        {
            var k = BildfahrplanStopAxis.MatchKey(stations[i].DisplayName);
            if (morperIdx < 0 && k.Contains("morper", StringComparison.OrdinalIgnoreCase))
            {
                morperIdx = i;
            }

            if (gerresheimIdx < 0 && k.Contains("gerresheim", StringComparison.OrdinalIgnoreCase))
            {
                gerresheimIdx = i;
            }
        }

        if (morperIdx < 0 || gerresheimIdx < 0)
        {
            return;
        }

        var mG = stations[gerresheimIdx].DistanceMeters;
        var erkrath = IndexOfNameContaining(stations, "erkrath");
        var flingern = IndexOfNameContaining(stations, "flingern");
        var hbf = IndexOfNameContaining(stations, "hbf");
        // Düsseldorf-Seite = weg von Erkrath
        var towardDuesseldorf = 1.0;
        if (erkrath >= 0)
        {
            towardDuesseldorf = Math.Sign(mG - stations[erkrath].DistanceMeters);
            if (towardDuesseldorf == 0)
            {
                towardDuesseldorf = 1;
            }
        }

        double m;
        var outer = flingern >= 0 ? flingern : -1;
        if (outer < 0)
        {
            // Düsseldorf Hbf (nicht Wuppertal)
            for (var t = 0; t < stations.Count; t++)
            {
                var k = BildfahrplanStopAxis.MatchKey(stations[t].DisplayName);
                if (k.Contains("hbf", StringComparison.OrdinalIgnoreCase) &&
                    (k.Contains("düsseldorf", StringComparison.OrdinalIgnoreCase) ||
                     k.Contains("duesseldorf", StringComparison.OrdinalIgnoreCase) ||
                     k.Contains("dusseldorf", StringComparison.OrdinalIgnoreCase) ||
                     !k.Contains("wuppertal", StringComparison.OrdinalIgnoreCase)))
                {
                    if (k.Contains("wuppertal", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    outer = t;
                    if (k.Contains("düsseldorf", StringComparison.OrdinalIgnoreCase) ||
                        k.Contains("duesseldorf", StringComparison.OrdinalIgnoreCase) ||
                        k.Contains("dusseldorf", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }
        }

        if (outer >= 0)
        {
            var mOuter = stations[outer].DistanceMeters;
            m = (mOuter + mG) / 2.0;
            if (Math.Abs(m - mG) < 400)
            {
                m = mG + towardDuesseldorf * 800;
            }
        }
        else
        {
            m = mG + towardDuesseldorf * 800;
        }

        // Nie auf Gerresheim oder Erkrath-Seite kleben
        if (Math.Abs(m - mG) < 400)
        {
            m = mG + towardDuesseldorf * 800;
        }

        if (erkrath >= 0)
        {
            var mE = stations[erkrath].DistanceMeters;
            // Muss auf der anderen Seite von Erkrath liegen als Gerresheim→Erkrath
            if (Math.Sign(m - mG) == Math.Sign(mE - mG) && Math.Sign(mE - mG) != 0)
            {
                m = mG + towardDuesseldorf * 800;
            }
        }

        var morper = stations[morperIdx] with { DistanceMeters = m };
        stations.RemoveAt(morperIdx);
        if (morperIdx < gerresheimIdx)
        {
            gerresheimIdx--;
        }

        // In Liste: auf Düsseldorf-Seite neben Gerresheim
        var insertAt = towardDuesseldorf > 0
            ? (m >= mG ? gerresheimIdx + 1 : gerresheimIdx)
            : (m <= mG ? gerresheimIdx + 1 : gerresheimIdx);
        // Meter-basiert
        insertAt = InsertIndexForMeters(stations, m);
        insertAt = Math.Clamp(insertAt, 0, stations.Count);
        stations.Insert(insertAt, morper);
        meters[morper.DisplayName] = m;
        meters[BildfahrplanStopAxis.MatchKey(morper.DisplayName)] = m;
    }

    /// <summary>Depotausfahrt/Depoteinfahrt aus Fahrten auf die Achse bringen, falls fehlend.</summary>
    private static void EnsureDepotStopsOnAxis(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        List<StopOnAxis> stations,
        Dictionary<string, double> meters)
    {
        RouteStopItem? ausfahrt = null;
        RouteStopItem? einfahrt = null;
        string? ausfahrtName = null;
        string? einfahrtName = null;

        foreach (var routeKey in routeKeys)
        {
            foreach (var stop in editor.GetStops(routeKey).Where(BildfahrplanStopAxis.IsAxisLabelStop))
            {
                var name = BildfahrplanStopAxis.AxisDisplayName(stop);
                if (string.IsNullOrEmpty(name) || AxisContainsName(stations, name))
                {
                    continue;
                }

                if (IsDepotAusfahrt(name) && ausfahrt is null)
                {
                    ausfahrt = stop;
                    ausfahrtName = name;
                }
                else if (IsDepotEinfahrt(name) && einfahrt is null)
                {
                    einfahrt = stop;
                    einfahrtName = name;
                }
                else if (IsDepotLabel(name) && ausfahrt is null && einfahrt is null)
                {
                    // generisches „Betriebshof“ / „Depot“: als Ausfahrt am Start behandeln
                    if (BildfahrplanStopAxis.MatchKey(name).Contains("betriebshof", StringComparison.OrdinalIgnoreCase) ||
                        BildfahrplanStopAxis.MatchKey(name).Equals("depot", StringComparison.OrdinalIgnoreCase))
                    {
                        ausfahrt ??= stop;
                        ausfahrtName ??= name;
                    }
                }
            }
        }

        var line = stations.Where(s => !IsDepotLabel(s.DisplayName)).ToList();
        if (line.Count == 0)
        {
            return;
        }

        var mid = line.Average(s => s.DistanceMeters);
        var lowEnd = line.MinBy(s => s.DistanceMeters)!;
        var highEnd = line.MaxBy(s => s.DistanceMeters)!;

        if (ausfahrt is not null && ausfahrtName is not null && !AxisContainsName(stations, ausfahrtName))
        {
            // Ausfahrt typisch am Düsseldorf-Ende (hohe Meter bei üblicher Orientierung) oder am Start der Schnur
            var anchor = highEnd;
            var hbfD = line.FirstOrDefault(s =>
            {
                var k = BildfahrplanStopAxis.MatchKey(s.DisplayName);
                return k.Contains("hbf", StringComparison.OrdinalIgnoreCase) &&
                       !k.Contains("wuppertal", StringComparison.OrdinalIgnoreCase);
            });
            if (hbfD is not null)
            {
                anchor = hbfD;
            }

            var dir = Math.Sign(anchor.DistanceMeters - mid);
            if (dir == 0)
            {
                dir = 1;
            }

            var m = anchor.DistanceMeters + dir * DefaultDepotGapMeters;
            InsertStation(stations, meters, ausfahrt, ausfahrtName, m, InsertIndexForMeters(stations, m));
        }

        if (einfahrt is not null && einfahrtName is not null && !AxisContainsName(stations, einfahrtName))
        {
            var anchor = lowEnd;
            var hbfW = line.FirstOrDefault(s =>
            {
                var k = BildfahrplanStopAxis.MatchKey(s.DisplayName);
                return k.Contains("wuppertal", StringComparison.OrdinalIgnoreCase) &&
                       k.Contains("hbf", StringComparison.OrdinalIgnoreCase);
            });
            if (hbfW is not null)
            {
                anchor = hbfW;
            }
            else
            {
                var steinbeck = line.FirstOrDefault(s =>
                    BildfahrplanStopAxis.MatchKey(s.DisplayName)
                        .Contains("steinbeck", StringComparison.OrdinalIgnoreCase));
                if (steinbeck is not null)
                {
                    anchor = steinbeck;
                }
            }

            var dir = Math.Sign(anchor.DistanceMeters - mid);
            if (dir == 0)
            {
                dir = -1;
            }

            var m = anchor.DistanceMeters + dir * DefaultDepotGapMeters;
            InsertStation(stations, meters, einfahrt, einfahrtName, m, InsertIndexForMeters(stations, m));
        }
    }

    private static double ResolvePauseParentGapMeters(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        string parentDisplayName,
        string pauseDisplayName)
    {
        var measured = TryMeasureSnapBetweenStops(editor, routeKeys, parentDisplayName, pauseDisplayName);
        if (measured is >= 50)
        {
            return measured.Value;
        }

        return DefaultPauseParentSnapMeters;
    }

    /// <summary>Misst die gesnappte Länge zwischen zwei Halten (über Fahrten, die beide enthalten).</summary>
    private static double? TryMeasureSnapBetweenStops(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        string stopNameA,
        string stopNameB)
    {
        double? bestMeters = null;
        var bestRank = int.MaxValue;

        foreach (var routeKey in routeKeys)
        {
            var stops = editor.GetStops(routeKey);
            int? idxA = null;
            int? idxB = null;
            for (var s = 0; s < stops.Count; s++)
            {
                if (!BildfahrplanStopAxis.IsAxisLabelStop(stops[s]))
                {
                    continue;
                }

                var dn = BildfahrplanStopAxis.AxisDisplayName(stops[s]);
                if (StopMatchesAxisLabel(dn, stopNameA))
                {
                    idxA ??= s;
                }

                if (StopMatchesAxisLabel(dn, stopNameB))
                {
                    idxB ??= s;
                }
            }

            if (idxA is null || idxB is null || idxA == idxB)
            {
                continue;
            }

            var span = Math.Abs(idxA.Value - idxB.Value);
            if (span > 6)
            {
                continue;
            }

            var draft = RoutePathDraftRepository.LoadOrCreate(routeKey, stops, editor.PackageRoot);
            var meters = BildfahrplanStopAxis.MeasureIndexSpanMeters(
                draft, stops, idxA.Value, idxB.Value, out var usedSnap);
            if (meters < 50)
            {
                continue;
            }

            // Rank: Snap + wenige Zwischenhalte bevorzugen
            var rank = (usedSnap ? 0 : 100) + span;
            if (rank < bestRank || (rank == bestRank && (bestMeters is null || meters < bestMeters)))
            {
                bestRank = rank;
                bestMeters = meters;
            }

            if (usedSnap && span <= 2)
            {
                return meters;
            }
        }

        return bestMeters;
    }

    private static bool StopMatchesAxisLabel(string displayName, string label)
    {
        if (BildfahrplanStopAxis.NamesReferToSameStop(displayName, label))
        {
            return true;
        }

        // Zwei Pause-Labels am gleichen Bahnhof
        if (IsPauseLabel(label) && IsPauseLabel(displayName) &&
            BildfahrplanStopAxis.NamesReferToSameStop(
                PauseParentMatchKey(displayName),
                PauseParentMatchKey(label)))
        {
            return true;
        }

        // Alias ohne Pause-Wort (nur Parent↔Parent)
        if (!IsPauseLabel(label) && !IsPauseLabel(displayName) &&
            BildfahrplanStopAxis.NamesReferToSameStop(displayName, PauseParentMatchKey(label)))
        {
            return true;
        }

        return false;
    }

    private static int FindParentStationIndex(List<StopOnAxis> stations, string parentKey, int pauseIndex)
    {
        if (string.IsNullOrEmpty(parentKey))
        {
            return -1;
        }

        for (var p = 0; p < stations.Count; p++)
        {
            if (p == pauseIndex || IsPauseLabel(stations[p].DisplayName))
            {
                continue;
            }

            var pk = BildfahrplanStopAxis.MatchKey(stations[p].DisplayName);
            if (BildfahrplanStopAxis.NamesReferToSameStop(pk, parentKey) ||
                pk.Contains(parentKey, StringComparison.OrdinalIgnoreCase) ||
                parentKey.Contains(pk, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return -1;
    }

    private static void PlacePauseStopsAfterParent(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        List<StopOnAxis> stations,
        Dictionary<string, double> meters)
    {
        for (var i = 0; i < stations.Count; i++)
        {
            var pauseName = stations[i].DisplayName;
            if (!IsPauseLabel(pauseName))
            {
                continue;
            }

            var parentKey = PauseParentMatchKey(pauseName);
            if (string.IsNullOrEmpty(parentKey))
            {
                continue;
            }

            var parentIdx = -1;
            for (var p = 0; p < stations.Count; p++)
            {
                if (p == i || IsPauseLabel(stations[p].DisplayName))
                {
                    continue;
                }

                if (BildfahrplanStopAxis.NamesReferToSameStop(stations[p].DisplayName, parentKey) ||
                    BildfahrplanStopAxis.MatchKey(stations[p].DisplayName)
                        .Contains(parentKey, StringComparison.OrdinalIgnoreCase) ||
                    parentKey.Contains(
                        BildfahrplanStopAxis.MatchKey(stations[p].DisplayName),
                        StringComparison.OrdinalIgnoreCase))
                {
                    parentIdx = p;
                    break;
                }
            }

            if (parentIdx < 0)
            {
                continue;
            }

            var pause = stations[i];
            stations.RemoveAt(i);
            if (i < parentIdx)
            {
                parentIdx--;
            }

            var insertAt = parentIdx + 1;
            var parentM = stations[parentIdx].DistanceMeters;
            var gap = ResolvePauseParentGapMeters(editor, routeKeys, stations[parentIdx].DisplayName, pauseName);

            double dir;
            if (insertAt < stations.Count)
            {
                var nextM = stations[insertAt].DistanceMeters;
                dir = Math.Sign(nextM - parentM);
                if (dir == 0)
                {
                    dir = 1;
                }
            }
            else
            {
                var prevM = parentIdx > 0 ? stations[parentIdx - 1].DistanceMeters : parentM - gap;
                dir = Math.Sign(parentM - prevM);
                if (dir == 0)
                {
                    dir = 1;
                }
            }

            pause = pause with { DistanceMeters = parentM + dir * gap };
            stations.Insert(insertAt, pause);
            meters[pause.DisplayName] = pause.DistanceMeters;
            meters[BildfahrplanStopAxis.MatchKey(pause.DisplayName)] = pause.DistanceMeters;
            i = Math.Max(i, insertAt);
        }
    }

    private static bool IsPauseLabel(string? name)
    {
        var k = BildfahrplanStopAxis.MatchKey(name);
        return k.Contains(" pause", StringComparison.OrdinalIgnoreCase) ||
               k.StartsWith("pause ", StringComparison.OrdinalIgnoreCase) ||
               k.EndsWith(" pause", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(k, "pause", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDepotLabel(string? name)
    {
        var k = BildfahrplanStopAxis.MatchKey(name);
        return k.Contains("depotausfahrt", StringComparison.OrdinalIgnoreCase) ||
               k.Contains("depoteinfahrt", StringComparison.OrdinalIgnoreCase) ||
               k.Contains("betriebshof", StringComparison.OrdinalIgnoreCase) ||
               (k.Contains("depot", StringComparison.OrdinalIgnoreCase) &&
                (k.Contains("ausfahrt", StringComparison.OrdinalIgnoreCase) ||
                 k.Contains("einfahrt", StringComparison.OrdinalIgnoreCase) ||
                 k.Equals("depot", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Fremdäste (Kaarst/Neuss), die nicht auf die Hauptkorridor-Achse gehören.</summary>
    private static bool IsOffCorridorBranchName(string? name)
    {
        var k = BildfahrplanStopAxis.MatchKey(name);
        return k.Contains("neuss", StringComparison.OrdinalIgnoreCase) ||
               k.Contains("kaarst", StringComparison.OrdinalIgnoreCase) ||
               k.Contains("meerbusch", StringComparison.OrdinalIgnoreCase) ||
               k.Contains("ikea", StringComparison.OrdinalIgnoreCase);
    }

    private static void PruneOffCorridorStations(
        List<StopOnAxis> stations,
        Dictionary<string, double> meters,
        HashSet<string> referenceKeys)
    {
        for (var i = stations.Count - 1; i >= 0; i--)
        {
            var key = BildfahrplanStopAxis.MatchKey(stations[i].DisplayName);
            if (!IsOffCorridorBranchName(stations[i].DisplayName) || referenceKeys.Contains(key))
            {
                continue;
            }

            meters.Remove(stations[i].DisplayName);
            meters.Remove(key);
            stations.RemoveAt(i);
        }
    }

    /// <summary>Fahrt teilt genug Halte mit der aktuellen Achse (sonst Kurz-/Astfahrt ignorieren).</summary>
    private static bool TripOverlapsCorridor(
        EditableRoutePackage editor,
        string routeKey,
        List<StopOnAxis> stations)
    {
        if (stations.Count == 0)
        {
            return false;
        }

        var shared = 0;
        foreach (var stop in editor.GetStops(routeKey).Where(BildfahrplanStopAxis.IsAxisLabelStop))
        {
            var name = BildfahrplanStopAxis.AxisDisplayName(stop);
            if (!string.IsNullOrEmpty(name) && AxisContainsName(stations, name))
            {
                shared++;
                if (shared >= 3)
                {
                    return true;
                }
            }
        }

        // Kurze Referenzäste: mind. 2 gemeinsame Halte
        return shared >= 2 && stations.Count <= 6;
    }

    private static bool TripHasDepotOrSpecial(EditableRoutePackage editor, string routeKey)
    {
        foreach (var stop in editor.GetStops(routeKey).Where(BildfahrplanStopAxis.IsAxisLabelStop))
        {
            var name = BildfahrplanStopAxis.AxisDisplayName(stop);
            if (IsDepotLabel(name) || IsPauseLabel(name) ||
                BildfahrplanStopAxis.MatchKey(name).Contains("morper", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDepotAusfahrt(string? name)
    {
        var k = BildfahrplanStopAxis.MatchKey(name);
        return k.Contains("depotausfahrt", StringComparison.OrdinalIgnoreCase) ||
               (k.Contains("depot", StringComparison.OrdinalIgnoreCase) &&
                k.Contains("ausfahrt", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDepotEinfahrt(string? name)
    {
        var k = BildfahrplanStopAxis.MatchKey(name);
        return k.Contains("depoteinfahrt", StringComparison.OrdinalIgnoreCase) ||
               (k.Contains("depot", StringComparison.OrdinalIgnoreCase) &&
                k.Contains("einfahrt", StringComparison.OrdinalIgnoreCase));
    }

    private static string PauseParentMatchKey(string pauseName)
    {
        var k = BildfahrplanStopAxis.MatchKey(pauseName);
        k = System.Text.RegularExpressions.Regex.Replace(
            k,
            @"\bpause\b",
            " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        k = System.Text.RegularExpressions.Regex.Replace(
            k,
            @"\bpp\b",
            " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        k = System.Text.RegularExpressions.Regex.Replace(k, @"\s+", " ").Trim();
        return BildfahrplanStopAxis.MatchKey(k);
    }

    private static void DeduplicateAliasStations(
        List<StopOnAxis> stations,
        Dictionary<string, double> meters)
    {
        for (var i = 0; i < stations.Count; i++)
        {
            for (var j = stations.Count - 1; j > i; j--)
            {
                if (!BildfahrplanStopAxis.NamesReferToSameStop(
                        stations[i].DisplayName,
                        stations[j].DisplayName))
                {
                    continue;
                }

                // Längeren/spezifischeren Namen behalten, kürzeren entfernen
                var keepI = stations[i].DisplayName.Length >= stations[j].DisplayName.Length;
                var keep = keepI ? stations[i] : stations[j];
                var drop = keepI ? stations[j] : stations[i];
                var merged = keep with
                {
                    DisplayName = BildfahrplanStopAxis.PreferDisplayName(
                        stations[i].DisplayName,
                        stations[j].DisplayName),
                    DistanceMeters = keep.DistanceMeters
                };

                stations[i] = merged;
                stations.RemoveAt(j);
                meters[merged.DisplayName] = merged.DistanceMeters;
                meters[BildfahrplanStopAxis.MatchKey(merged.DisplayName)] = merged.DistanceMeters;
                meters[BildfahrplanStopAxis.MatchKey(drop.DisplayName)] = merged.DistanceMeters;
                meters[drop.DisplayName] = merged.DistanceMeters;
            }
        }
    }

    private static void RebuildMetersLookup(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys,
        IList<RouteStopItem> refStops,
        List<StopOnAxis> stations,
        Dictionary<string, double> meters)
    {
        meters.Clear();
        foreach (var s in stations)
        {
            meters[s.DisplayName] = s.DistanceMeters;
            meters[BildfahrplanStopAxis.MatchKey(s.DisplayName)] = s.DistanceMeters;
        }

        // Kurzformen aller Achsenhalte (z. B. letztes Wort) als Alias
        foreach (var s in stations)
        {
            var parts = BildfahrplanStopAxis.MatchKey(s.DisplayName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].Length >= 6)
            {
                meters.TryAdd(parts[^1], s.DistanceMeters);
            }
        }

        void MapStop(RouteStopItem stop)
        {
            var name = BildfahrplanStopAxis.AxisDisplayName(stop);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!TryFindStationMeters(stations, meters, name, out var m))
            {
                return;
            }

            foreach (var key in LookupKeys(stop))
            {
                meters[key] = m;
            }

            meters[name] = m;
            meters[BildfahrplanStopAxis.MatchKey(name)] = m;
        }

        foreach (var stop in refStops.Where(BildfahrplanStopAxis.IsAxisLabelStop))
        {
            MapStop(stop);
        }

        foreach (var key in routeKeys)
        {
            foreach (var stop in editor.GetStops(key).Where(BildfahrplanStopAxis.IsAxisLabelStop))
            {
                MapStop(stop);
            }
        }
    }

    private static bool TryFindStationMeters(
        List<StopOnAxis> stations,
        IReadOnlyDictionary<string, double> meters,
        string name,
        out double m)
    {
        if (meters.TryGetValue(name, out m) ||
            meters.TryGetValue(BildfahrplanStopAxis.MatchKey(name), out m))
        {
            return true;
        }

        var idx = IndexOfName(stations, name);
        if (idx >= 0)
        {
            m = stations[idx].DistanceMeters;
            return true;
        }

        m = 0;
        return false;
    }

    private static bool AxisContainsName(List<StopOnAxis> stations, string name) =>
        IndexOfName(stations, name) >= 0;

    private static int IndexOfName(List<StopOnAxis> stations, string name)
    {
        for (var i = 0; i < stations.Count; i++)
        {
            if (BildfahrplanStopAxis.NamesReferToSameStop(stations[i].DisplayName, name))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Löst Streckenmeter eines Halts über Code, Name/Alias oder GPS-Projektion (nah an Snap).</summary>
    public static bool TryResolveMeters(
        RouteStopItem stop,
        IReadOnlyDictionary<string, double> metersByLookup,
        IReadOnlyList<RoutePathLatLng>? snappedShape,
        out double meters)
    {
        foreach (var key in LookupKeys(stop))
        {
            if (metersByLookup.TryGetValue(key, out meters))
            {
                return true;
            }
        }

        var display = BildfahrplanStopAxis.AxisDisplayName(stop);
        var match = BildfahrplanStopAxis.MatchKey(display);
        if (!string.IsNullOrEmpty(match) && metersByLookup.TryGetValue(match, out meters))
        {
            return true;
        }

        // Alias: Kurzname ↔ „Ort Kurzname“
        foreach (var pair in metersByLookup)
        {
            if (BildfahrplanStopAxis.NamesReferToSameStop(pair.Key, display))
            {
                meters = pair.Value;
                return true;
            }
        }

        if (snappedShape is { Count: >= 2 } &&
            TryParseLatLon(stop, out var lat, out var lon) &&
            BildfahrplanStopAxis.TryDistanceAlongPolyline(
                snappedShape,
                new RoutePathLatLng { Lat = lat, Lon = lon },
                out var along,
                out var distToPath) &&
            distToPath <= MaxSnapProjectionMeters)
        {
            meters = along;
            return true;
        }

        meters = 0;
        return false;
    }

    public static IEnumerable<string> LookupKeys(RouteStopItem stop)
    {
        if (!string.IsNullOrWhiteSpace(stop.PlannerStopCode))
        {
            yield return "code:" + stop.PlannerStopCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(stop.VrrStopId))
        {
            yield return "vrr:" + stop.VrrStopId.Trim();
        }

        var name = BildfahrplanStopAxis.AxisDisplayName(stop);
        if (!string.IsNullOrEmpty(name))
        {
            yield return name;
            var match = BildfahrplanStopAxis.MatchKey(name);
            if (!string.Equals(match, name, StringComparison.OrdinalIgnoreCase))
            {
                yield return match;
            }
        }
    }

    private static List<IReadOnlyList<string>> BuildChains(
        EditableRoutePackage editor,
        IReadOnlyList<string> routeKeys)
    {
        var chains = new List<IReadOnlyList<string>>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in routeKeys)
        {
            var canonical = RouteDisplayHelper.ToCanonicalRouteKey(key);
            if (!visited.Add(canonical))
            {
                continue;
            }

            var chain = RouteChainPlanner.BuildConnectedRouteChain(editor, key);
            if (chain.Count == 0)
            {
                chains.Add([key]);
                continue;
            }

            foreach (var c in chain)
            {
                visited.Add(RouteDisplayHelper.ToCanonicalRouteKey(c));
            }

            chains.Add(chain);
        }

        return chains;
    }

    private static (string? Key, RoutePathDraft? Draft, IList<RouteStopItem>? Stops, double Len)
        PickBestCorridorReference(EditableRoutePackage editor, IReadOnlyList<string> keys)
    {
        string? bestKey = null;
        RoutePathDraft? bestDraft = null;
        IList<RouteStopItem>? bestStops = null;
        var bestScore = double.MinValue;
        var bestLen = -1.0;

        foreach (var key in keys)
        {
            var stops = editor.GetStops(key);
            var timed = stops.Count(s => !s.IsWaypoint);
            if (timed < 2)
            {
                continue;
            }

            var draft = RoutePathDraftRepository.LoadOrCreate(key, stops, editor.PackageRoot);
            var len = SnapLength(draft);
            // Viele Halte + langer Snap = typischer Hauptkorridor (nicht Kurz-/Astfahrt)
            var score = timed * 10_000.0 + len;
            if (score > bestScore)
            {
                bestScore = score;
                bestLen = len;
                bestKey = key;
                bestDraft = draft;
                bestStops = stops;
            }
        }

        return (bestKey, bestDraft, bestStops, bestLen);
    }

    private static double SumSnapLength(EditableRoutePackage editor, IReadOnlyList<string> keys) =>
        keys.Sum(k =>
        {
            var stops = editor.GetStops(k);
            if (stops.Count < 2)
            {
                return 0;
            }

            return SnapLength(RoutePathDraftRepository.LoadOrCreate(k, stops, editor.PackageRoot));
        });

    private static double SnapLength(RoutePathDraft draft)
    {
        if (draft.SnappedShape.Count >= 2)
        {
            return RoutePathDraftIntegrity.PolylineLengthMeters(draft.SnappedShape);
        }

        return draft.RoadSegmentPolylines.Values
            .Where(p => p.Count >= 2)
            .Sum(RoutePathDraftIntegrity.PolylineLengthMeters);
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
}
