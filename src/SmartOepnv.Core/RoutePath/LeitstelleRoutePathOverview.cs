using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Vereinfachte Fahrwege für die Leitstellen-Karte: nur gesnappte Linie und Haltestellen.
/// Wird in <c>leitstelle_stand.json</c> unter <c>routePathOverviews</c> gespeichert.
/// </summary>
public static class LeitstelleRoutePathOverview
{
    public const string OverviewsKey = "routePathOverviews";

    public static JsonObject BuildOverviewsObject(EditableRoutePackage package)
    {
        var overviews = new JsonObject();
        if (package.PackageRoot["routePathDrafts"] is not JsonObject drafts)
        {
            return overviews;
        }

        foreach (var (routeName, draftNode) in drafts)
        {
            if (string.IsNullOrWhiteSpace(routeName))
            {
                continue;
            }

            var draftJson = JsonNodeExtensions.DraftNodeToJsonText(draftNode);
            if (string.IsNullOrWhiteSpace(draftJson))
            {
                continue;
            }

            try
            {
                var draftObj = JsonNode.Parse(draftJson)?.AsObject();
                if (draftObj is null)
                {
                    continue;
                }

                var overview = SimplifyDraftForOverview(draftObj, routeName);
                if (overview is not null)
                {
                    overviews[routeName] = overview.ToJsonString();
                }
            }
            catch
            {
                // Einzelne defekte Drafts überspringen
            }
        }

        return overviews;
    }

    public static void ApplyOverviewsToEditor(EditableRoutePackage editor, JsonObject root)
    {
        if (root[OverviewsKey] is not JsonObject overviews || overviews.Count == 0)
        {
            return;
        }

        editor.PackageRoot[OverviewsKey] = overviews.DeepClone().AsObject();
    }

    public static string? TryGetOverviewJson(JsonObject? packageRoot, string? routeName)
    {
        return TryGetOverviewJson(packageRoot, routeName, lineCourseTelemetry: null);
    }

    public static string? TryGetOverviewJson(
        JsonObject? packageRoot,
        string? routeName,
        string? lineCourseTelemetry)
    {
        if (packageRoot is null || string.IsNullOrWhiteSpace(routeName))
        {
            return null;
        }

        var resolvedKey = ResolveRouteKey(packageRoot, routeName, lineCourseTelemetry);
        if (resolvedKey is null)
        {
            return null;
        }

        if (packageRoot[OverviewsKey] is JsonObject overviews &&
            JsonNodeExtensions.DraftNodeToJsonText(overviews[resolvedKey]) is { } overviewJson &&
            OverviewHasDrawableLine(overviewJson))
        {
            return overviewJson;
        }

        if (packageRoot["routePathDrafts"] is JsonObject drafts &&
            JsonNodeExtensions.DraftNodeToJsonText(drafts[resolvedKey]) is { } draftJson)
        {
            try
            {
                var draftObj = JsonNode.Parse(draftJson)?.AsObject();
                if (draftObj is null)
                {
                    return null;
                }

                return SimplifyDraftForOverview(draftObj, resolvedKey)?.ToJsonString();
            }
            catch
            {
                return null;
            }
        }

        // Overview ohne Linie (nur Marker) als letzter Fallback
        if (packageRoot[OverviewsKey] is JsonObject overviewsFallback &&
            JsonNodeExtensions.DraftNodeToJsonText(overviewsFallback[resolvedKey]) is { } markerOnly)
        {
            return markerOnly;
        }

        return null;
    }

    public static IReadOnlyList<string> GetAllOverviewJsons(JsonObject? packageRoot)
    {
        if (packageRoot is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var key in CollectRouteKeys(packageRoot).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var overviewJson = TryGetOverviewJson(packageRoot, key);
            if (!string.IsNullOrWhiteSpace(overviewJson))
            {
                result.Add(overviewJson);
            }
        }

        return result;
    }

    public static string? ResolveRouteKey(JsonObject packageRoot, string routeName) =>
        ResolveRouteKey(packageRoot, routeName, lineCourseTelemetry: null);

    /// <param name="lineCourseTelemetry">
    /// Optional Feld vom Fahrzeug, z. B. „128/01, 2134“ – nötig wenn <paramref name="routeName"/>
    /// nur der reine Name ohne Linie/Fahrt ist.
    /// </param>
    public static string? ResolveRouteKey(
        JsonObject packageRoot,
        string routeName,
        string? lineCourseTelemetry)
    {
        var trimmed = routeName.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var keys = CollectRouteKeys(packageRoot).ToList();
        if (keys.Count == 0)
        {
            return null;
        }

        var exact = keys.FirstOrDefault(k => string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var telemetry = EnrichTelemetryFromLineCourse(RouteDisplayHelper.Parse(trimmed), lineCourseTelemetry);

        // Mit Linie/Fahrt zuerst scharf matchen – sonst gewinnt oft die kürzeste Geschwisterfahrt.
        if (!string.IsNullOrWhiteSpace(telemetry.LineCourse) ||
            !string.IsNullOrWhiteSpace(telemetry.TripNumber))
        {
            var sharpMatches = keys
                .Where(k => RouteDefinitionMatchesTelemetry(k, telemetry, requireLineOrTrip: true))
                .ToList();
            if (sharpMatches.Count == 1)
            {
                return sharpMatches[0];
            }

            if (sharpMatches.Count > 1)
            {
                return PickPreferredRouteKey(sharpMatches);
            }
        }

        var canonicalMatches = keys
            .Where(k => RouteDisplayHelper.RouteKeysMatch(k, trimmed))
            .ToList();
        if (canonicalMatches.Count == 1)
        {
            return canonicalMatches[0];
        }

        if (canonicalMatches.Count > 1)
        {
            return PickPreferredRouteKey(canonicalMatches);
        }

        var definitionMatches = keys
            .Where(k => RouteDefinitionMatchesTelemetry(k, telemetry, requireLineOrTrip: false))
            .ToList();
        if (definitionMatches.Count == 1)
        {
            return definitionMatches[0];
        }

        if (definitionMatches.Count > 1)
        {
            return PickPreferredRouteKey(definitionMatches);
        }

        // Kein Raten per Substring („über CAS“ darf nicht „Hommelsbach“ ersetzen).
        return null;
    }

    private static RouteDefinition EnrichTelemetryFromLineCourse(
        RouteDefinition telemetry,
        string? lineCourseTelemetry)
    {
        if (!string.IsNullOrWhiteSpace(telemetry.LineCourse) ||
            !string.IsNullOrWhiteSpace(telemetry.TripNumber) ||
            string.IsNullOrWhiteSpace(lineCourseTelemetry))
        {
            return telemetry;
        }

        var raw = lineCourseTelemetry.Trim();
        if (raw is "–" or "-")
        {
            return telemetry;
        }

        var comma = raw.IndexOf(',');
        var lineCourse = comma >= 0 ? raw[..comma].Trim() : raw;
        var trip = comma >= 0 ? raw[(comma + 1)..].Trim() : string.Empty;
        return new RouteDefinition(telemetry.Name, lineCourse, trip, telemetry.PassengerDisplayLine);
    }

    private static string PickPreferredRouteKey(IReadOnlyList<string> matches) =>
        matches
            .OrderBy(k => k.Length)
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
            .First();

    private static bool RouteDefinitionMatchesTelemetry(
        string packageRouteKey,
        RouteDefinition telemetry,
        bool requireLineOrTrip)
    {
        var def = RouteDisplayHelper.Parse(packageRouteKey);
        if (!string.Equals(def.Name, telemetry.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasLine = !string.IsNullOrWhiteSpace(telemetry.LineCourse);
        var hasTrip = !string.IsNullOrWhiteSpace(telemetry.TripNumber);
        if (requireLineOrTrip && !hasLine && !hasTrip)
        {
            return false;
        }

        if (hasLine &&
            !string.Equals(
                RouteDisplayHelper.NormalizeLineCourse(def.LineCourse),
                RouteDisplayHelper.NormalizeLineCourse(telemetry.LineCourse),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (hasTrip &&
            !string.Equals(
                RouteDisplayHelper.NormalizeTripNumber(def.TripNumber),
                RouteDisplayHelper.NormalizeTripNumber(telemetry.TripNumber),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool OverviewHasDrawableLine(string overviewJson)
    {
        try
        {
            var obj = JsonNode.Parse(overviewJson)?.AsObject();
            if (obj is null)
            {
                return false;
            }

            if (obj["snappedShape"] is JsonArray shape && shape.Count >= 2)
            {
                return true;
            }

            if (obj["segmentSnaps"] is not JsonArray segs)
            {
                return false;
            }

            return segs.OfType<JsonObject>()
                .Any(s => s["points"] is JsonArray pts && pts.Count >= 2);
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<string> CollectRouteKeys(JsonObject packageRoot)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (packageRoot[OverviewsKey] is JsonObject overviews)
        {
            foreach (var key in overviews.Select(p => p.Key))
            {
                keys.Add(key);
            }
        }

        if (packageRoot["routePathDrafts"] is JsonObject drafts)
        {
            foreach (var key in drafts.Select(p => p.Key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static JsonObject? SimplifyDraftForOverview(JsonObject draft, string routeName)
    {
        var stopNodes = new JsonArray();
        if (draft["nodes"] is JsonArray nodes)
        {
            foreach (var node in nodes.OfType<JsonObject>())
            {
                var type = node["type"]?.GetValue<string>() ?? string.Empty;
                if (!string.Equals(type, "STOP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lat = node["lat"]?.GetValue<double>() ?? double.NaN;
                var lon = node["lon"]?.GetValue<double>() ?? double.NaN;
                if (!double.IsFinite(lat) || !double.IsFinite(lon))
                {
                    continue;
                }

                stopNodes.Add(new JsonObject
                {
                    ["id"] = node["id"]?.DeepClone() ?? JsonValue.Create(Guid.NewGuid().ToString("N")),
                    ["type"] = "STOP",
                    ["title"] = node["title"]?.DeepClone() ?? JsonValue.Create("Haltestelle"),
                    ["lat"] = lat,
                    ["lon"] = lon
                });
            }
        }

        var segmentSnaps = SimplifySegmentSnaps(draft["segmentSnaps"]);
        var snappedShape = CopyLatLngArray(draft["snappedShape"]);
        var hasLine = (snappedShape?.Count ?? 0) >= 2 ||
                      segmentSnaps?.Any(s => s is JsonObject o && o["points"] is JsonArray pts && pts.Count >= 2) == true;
        if (!hasLine && stopNodes.Count == 0)
        {
            return null;
        }

        var overview = new JsonObject
        {
            ["routeName"] = routeName,
            ["routeLineColor"] = draft["routeLineColor"]?.DeepClone() ?? JsonValue.Create("#2196f3"),
            ["nodes"] = stopNodes
        };

        if (snappedShape is { Count: >= 2 })
        {
            overview["snappedShape"] = snappedShape;
        }

        if (segmentSnaps is { Count: > 0 })
        {
            overview["segmentSnaps"] = segmentSnaps;
        }

        if (draft["roadSnappedEdgeKeys"] is JsonArray edgeKeys)
        {
            overview["roadSnappedEdgeKeys"] = edgeKeys.DeepClone();
        }

        return overview;
    }

    private static JsonArray? SimplifySegmentSnaps(JsonNode? segmentSnapsNode)
    {
        if (segmentSnapsNode is not JsonArray segmentSnaps || segmentSnaps.Count == 0)
        {
            return null;
        }

        var result = new JsonArray();
        foreach (var snap in segmentSnaps.OfType<JsonObject>())
        {
            var points = CopyLatLngArray(snap["points"]);
            if (points is null || points.Count < 2)
            {
                continue;
            }

            result.Add(new JsonObject
            {
                ["fromNodeId"] = snap["fromNodeId"]?.DeepClone(),
                ["toNodeId"] = snap["toNodeId"]?.DeepClone(),
                ["points"] = points
            });
        }

        return result.Count > 0 ? result : null;
    }

    private static JsonArray? CopyLatLngArray(JsonNode? pointsNode)
    {
        if (pointsNode is not JsonArray points || points.Count == 0)
        {
            return null;
        }

        var result = new JsonArray();
        foreach (var point in points.OfType<JsonObject>())
        {
            var lat = point["lat"]?.GetValue<double>() ?? double.NaN;
            var lon = point["lon"]?.GetValue<double>() ?? double.NaN;
            if (!double.IsFinite(lat) || !double.IsFinite(lon))
            {
                continue;
            }

            result.Add(new JsonObject
            {
                ["lat"] = lat,
                ["lon"] = lon
            });
        }

        return result.Count > 0 ? result : null;
    }
}
