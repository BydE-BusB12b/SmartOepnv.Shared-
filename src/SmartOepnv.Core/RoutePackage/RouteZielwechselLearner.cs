namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Lernt Zielwechsel-GPS und Startziele pro PlannerStopCode aus vorhandenen Routen.
/// </summary>
public static class RouteZielwechselLearner
{
    public sealed record Suggestion(
        string GpsCoordinates,
        int Radius,
        string Destination,
        string DestinationId,
        string Ds021NeuDestination,
        string Ds021NeuDestinationId,
        string FmaS1Destination,
        string FmaS1DestinationId,
        string Ds003aDestination,
        string Ds003aDestinationId,
        string ZielnummerDestination,
        string ZielnummerDestinationId,
        string MobitecDestination,
        string MobitecDestinationId,
        string LineNumber)
    {
        public bool HasGps => !string.IsNullOrWhiteSpace(GpsCoordinates);

        public bool HasDestination =>
            RouteStopEditorCatalog.HasStartStopDestination(Destination) ||
            RouteStopEditorCatalog.HasStartStopDestination(Ds021NeuDestination) ||
            RouteStopEditorCatalog.HasStartStopDestination(FmaS1Destination) ||
            !string.IsNullOrWhiteSpace(Ds003aDestination) ||
            !string.IsNullOrWhiteSpace(ZielnummerDestination) ||
            !string.IsNullOrWhiteSpace(MobitecDestination) ||
            !string.IsNullOrWhiteSpace(LineNumber);

        public static Suggestion Empty { get; } = new(
            string.Empty, 40,
            string.Empty, string.Empty,
            string.Empty, string.Empty,
            string.Empty, string.Empty,
            string.Empty, string.Empty,
            string.Empty, string.Empty,
            string.Empty, string.Empty,
            string.Empty);
    }

    /// <summary>
    /// Zielwechsel-GPS (nur Halte mit aktivem Zielwechsel) und Startziele (alle Starthaltestellen)
    /// getrennt aggregieren, dann pro ID kombinieren.
    /// </summary>
    public static IReadOnlyDictionary<string, Suggestion> BuildTypical(
        IEnumerable<IEnumerable<RouteStopItem>> routeStopLists)
    {
        var gpsVotes = new Dictionary<string, Dictionary<string, (string Gps, int Radius, int Count)>>(
            StringComparer.Ordinal);
        var destVotes = new Dictionary<string, Dictionary<string, (Suggestion Dest, int Count)>>(
            StringComparer.Ordinal);

        foreach (var stops in routeStopLists)
        {
            foreach (var stop in stops)
            {
                if (stop.IsWaypoint)
                {
                    continue;
                }

                var code = PlannerStopCode.Normalize(stop.PlannerStopCode);
                if (!PlannerStopCode.IsValid(code))
                {
                    continue;
                }

                if (stop.ZielwechselEnabled)
                {
                    var gps = NormalizeGps(stop.ZielwechselGpsCoordinates);
                    if (gps.Length > 0)
                    {
                        var radius = stop.ZielwechselRadius > 0 ? stop.ZielwechselRadius : 40;
                        var gpsKey = $"{gps}\u001f{radius}";
                        if (!gpsVotes.TryGetValue(code, out var byGps))
                        {
                            byGps = new Dictionary<string, (string, int, int)>(StringComparer.Ordinal);
                            gpsVotes[code] = byGps;
                        }

                        if (byGps.TryGetValue(gpsKey, out var existingGps))
                        {
                            byGps[gpsKey] = (existingGps.Gps, existingGps.Radius, existingGps.Count + 1);
                        }
                        else
                        {
                            byGps[gpsKey] = (gps, radius, 1);
                        }
                    }
                }

                if (!HasAnyStartDestination(stop))
                {
                    continue;
                }

                var dest = DestFromStop(stop);
                var destSig = ToDestinationSignature(dest);
                if (!destVotes.TryGetValue(code, out var byDest))
                {
                    byDest = new Dictionary<string, (Suggestion, int)>(StringComparer.Ordinal);
                    destVotes[code] = byDest;
                }

                if (byDest.TryGetValue(destSig, out var existingDest))
                {
                    byDest[destSig] = (existingDest.Dest, existingDest.Count + 1);
                }
                else
                {
                    byDest[destSig] = (dest, 1);
                }
            }
        }

        var codes = gpsVotes.Keys.Concat(destVotes.Keys).Distinct(StringComparer.Ordinal);
        var result = new Dictionary<string, Suggestion>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            string gps = string.Empty;
            var radius = 40;
            if (gpsVotes.TryGetValue(code, out var byGps) && byGps.Count > 0)
            {
                var bestGps = byGps.Values.OrderByDescending(x => x.Count).First();
                gps = bestGps.Gps;
                radius = bestGps.Radius;
            }

            var dest = Suggestion.Empty;
            if (destVotes.TryGetValue(code, out var byDest) && byDest.Count > 0)
            {
                dest = byDest.Values.OrderByDescending(x => x.Count).First().Dest;
            }

            var combined = WithGps(dest, gps, radius);
            if (combined.HasGps || combined.HasDestination)
            {
                result[code] = combined;
            }
        }

        return result;
    }

    public static bool TrySuggest(
        IReadOnlyDictionary<string, Suggestion> typical,
        string? plannerStopCode,
        out Suggestion suggestion)
    {
        suggestion = Suggestion.Empty;
        var code = PlannerStopCode.Normalize(plannerStopCode);
        if (!PlannerStopCode.IsValid(code) || !typical.TryGetValue(code, out var found))
        {
            return false;
        }

        suggestion = found;
        return true;
    }

    /// <summary>GPS/Radius in leere Zielwechsel-Felder; Ziele nur wenn noch keines gesetzt.</summary>
    public static bool TryApplyZielwechselToEmptyFields(RouteStopItem stop, Suggestion suggestion)
    {
        var changed = false;

        if (suggestion.HasGps && string.IsNullOrWhiteSpace(stop.ZielwechselGpsCoordinates))
        {
            stop.ZielwechselGpsCoordinates = suggestion.GpsCoordinates;
            if (suggestion.Radius > 0)
            {
                stop.ZielwechselRadius = suggestion.Radius;
            }

            changed = true;
        }
        else if (suggestion.Radius > 0 && stop.ZielwechselRadius <= 0)
        {
            stop.ZielwechselRadius = suggestion.Radius;
            changed = true;
        }

        if (suggestion.HasDestination && !HasAnyStartDestination(stop))
        {
            ApplyDestinations(stop, suggestion);
            changed = true;
        }

        return changed;
    }

    /// <summary>Nur Startziel übernehmen (Starthaltestelle ohne Zielwechsel).</summary>
    public static bool TryApplyStartDestinationToEmptyFields(RouteStopItem stop, Suggestion suggestion)
    {
        if (!suggestion.HasDestination || HasAnyStartDestination(stop))
        {
            return false;
        }

        ApplyDestinations(stop, suggestion);
        return true;
    }

    public static bool HasAnyStartDestination(RouteStopItem stop) =>
        RouteStopEditorCatalog.HasStartStopDestination(stop.Destination) ||
        RouteStopEditorCatalog.HasStartStopDestination(stop.Ds021NeuDestination) ||
        RouteStopEditorCatalog.HasStartStopDestination(stop.FmaS1Destination) ||
        !string.IsNullOrWhiteSpace(stop.Ds003aDestination) ||
        !string.IsNullOrWhiteSpace(stop.ZielnummerDestination) ||
        !string.IsNullOrWhiteSpace(stop.MobitecDestination) ||
        !string.IsNullOrWhiteSpace(stop.LineNumber);

    private static Suggestion DestFromStop(RouteStopItem stop) =>
        new(
            string.Empty,
            40,
            stop.Destination?.Trim() ?? string.Empty,
            OutsideDisplayId.Normalize(stop.DestinationId),
            stop.Ds021NeuDestination?.Trim() ?? string.Empty,
            OutsideDisplayId.Normalize(stop.Ds021NeuDestinationId),
            stop.FmaS1Destination?.Trim() ?? string.Empty,
            OutsideDisplayId.Normalize(stop.FmaS1DestinationId),
            stop.Ds003aDestination?.Trim() ?? string.Empty,
            OutsideDisplayId.Normalize(stop.Ds003aDestinationId),
            stop.ZielnummerDestination?.Trim() ?? string.Empty,
            OutsideDisplayId.Normalize(stop.ZielnummerDestinationId),
            stop.MobitecDestination?.Trim() ?? string.Empty,
            OutsideDisplayId.Normalize(stop.MobitecDestinationId),
            stop.LineNumber?.Trim() ?? string.Empty);

    private static Suggestion WithGps(Suggestion dest, string gps, int radius) =>
        dest with { GpsCoordinates = gps, Radius = radius > 0 ? radius : 40 };

    private static void ApplyDestinations(RouteStopItem stop, Suggestion suggestion)
    {
        stop.Destination = suggestion.Destination;
        stop.DestinationId = suggestion.DestinationId;
        stop.Ds021NeuDestination = suggestion.Ds021NeuDestination;
        stop.Ds021NeuDestinationId = suggestion.Ds021NeuDestinationId;
        stop.FmaS1Destination = suggestion.FmaS1Destination;
        stop.FmaS1DestinationId = suggestion.FmaS1DestinationId;
        stop.Ds003aDestination = suggestion.Ds003aDestination;
        stop.Ds003aDestinationId = suggestion.Ds003aDestinationId;
        stop.ZielnummerDestination = suggestion.ZielnummerDestination;
        stop.ZielnummerDestinationId = suggestion.ZielnummerDestinationId;
        stop.MobitecDestination = suggestion.MobitecDestination;
        stop.MobitecDestinationId = suggestion.MobitecDestinationId;
        stop.LineNumber = suggestion.LineNumber;
    }

    private static string ToDestinationSignature(Suggestion suggestion) =>
        string.Join(
            '\u001f',
            suggestion.Destination,
            suggestion.DestinationId,
            suggestion.Ds021NeuDestination,
            suggestion.Ds021NeuDestinationId,
            suggestion.FmaS1Destination,
            suggestion.FmaS1DestinationId,
            suggestion.Ds003aDestination,
            suggestion.Ds003aDestinationId,
            suggestion.ZielnummerDestination,
            suggestion.ZielnummerDestinationId,
            suggestion.MobitecDestination,
            suggestion.MobitecDestinationId,
            suggestion.LineNumber);

    private static string NormalizeGps(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var parts = trimmed.Split(',', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? $"{parts[0]},{parts[1]}"
            : trimmed;
    }
}
