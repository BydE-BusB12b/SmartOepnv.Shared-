using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.Core.RoutePackage;



/// <summary>

/// Merge von <c>routes_update.json</c> (Lite, ohne Audio) in ein bestehendes Route-Paket –

/// analog GPSAnsagen <c>RouteAutoImporter</c> bei <c>exportProfile=lite</c>.

/// </summary>

public static class LiteRouteUpdateMerge

{

    private static readonly HashSet<string> PreserveFromBaseKeys = new(StringComparer.Ordinal)

    {

        "embeddedSounds",

        "specialAnnouncements",

        "allowedRoutes",

        "managedAnnouncementTemplates",

        "managedStopTemplates",

        "employeeRoster",

        "employeeRosterMeta",

        "registeredVehicles",

        "registeredVehiclesMeta",

        "registeredVehiclesPlannerMeta",

        "messageTemplates",

        "mailTemplates",

        "driverDutyDispatches",

        "driverDutyDispatchesMeta",

        EndStopAnnouncementResolver.RootJsonFieldName

    };



    private static readonly string[] RouteKeyedObjectFields =

    [

        RouteOperatingDaysEditor.RootFieldName,

        RouteDateRangeEditor.RootFieldName,

        RouteOperatingDatesEditor.RootFieldName,

        RouteInteriorDisplayDestinationEditor.RootFieldName,

        AutoScheduleSourceRouteEditor.RootFieldName,

        "routePathDrafts",

        "routeOfflineGuidance",

        // Nicht ersetzen: sonst vernichtet Lite (wenige Alt-Overviews) den vollen Stand.
        LeitstelleRoutePathOverview.OverviewsKey

    ];



    public static bool IsLiteVehicleUpdate(string json)

    {

        if (string.IsNullOrWhiteSpace(json))

        {

            return false;

        }



        try

        {

            var root = JsonNode.Parse(json)?.AsObject();

            if (root is null)

            {

                return false;

            }



            var profile = root["exportProfile"]?.GetValue<string>();

            if (string.Equals(profile, GpsAnsagenRouteExportSync.LiteExportProfile, StringComparison.OrdinalIgnoreCase))

            {

                return true;

            }



            return root["skipEmbeddedSounds"]?.GetValue<bool>() == true;

        }

        catch

        {

            return false;

        }

    }



    /// <summary>

    /// Prüft, ob das Lite-Update Routen enthält, die im Editor noch fehlen

    /// (z. B. nach fehlgeschlagenem UI-Refresh trotz gespeichertem Timestamp).

    /// </summary>

    public static bool ContainsRoutesMissingFromEditor(string liteJson, EditableRoutePackage? editor)

    {

        if (editor is null)

        {

            return true;

        }



        foreach (var routeKey in CollectRouteKeysFromLiteUpdate(liteJson))

        {

            if (!editor.RouteNames.Any(name => RouteDisplayHelper.RouteKeysMatch(name, routeKey)))

            {

                return true;

            }

        }



        return false;

    }



    /// <summary>
    /// true, wenn gesnapte Fahrwege im Lite-Paket von Drafts/Overviews im Editor abweichen
    /// (Punktzahl) – z. B. nach routes_export-Reload trotz gespeichertem Merge-Timestamp.
    /// </summary>
    public static bool HasStaleRoutePathGeometry(string liteJson, EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return true;
        }

        JsonObject? liteRoot;
        try
        {
            liteRoot = JsonNode.Parse(liteJson)?.AsObject();
        }
        catch
        {
            return false;
        }

        if (liteRoot is null)
        {
            return false;
        }

        var liteDrafts = liteRoot["routePathDrafts"] as JsonObject;
        var liteOverviews = liteRoot[LeitstelleRoutePathOverview.OverviewsKey] as JsonObject;
        if ((liteDrafts is null || liteDrafts.Count == 0) &&
            (liteOverviews is null || liteOverviews.Count == 0))
        {
            return false;
        }

        var editorDrafts = editor.PackageRoot["routePathDrafts"] as JsonObject;
        var editorOverviews = editor.PackageRoot[LeitstelleRoutePathOverview.OverviewsKey] as JsonObject;

        foreach (var key in CollectRoutePathKeys(liteDrafts, liteOverviews))
        {
            var incomingCount = CountSnappedShapePoints(ResolveRoutePathNode(liteDrafts, liteOverviews, key));
            if (incomingCount < 2)
            {
                continue;
            }

            var editorCount = CountSnappedShapePoints(ResolveRoutePathNode(editorDrafts, editorOverviews, key));
            if (editorCount != incomingCount)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> CollectRoutePathKeys(JsonObject? drafts, JsonObject? overviews)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (drafts is not null)
        {
            foreach (var (key, _) in drafts)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key);
                }
            }
        }

        if (overviews is not null)
        {
            foreach (var (key, _) in overviews)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static JsonNode? ResolveRoutePathNode(JsonObject? drafts, JsonObject? overviews, string key)
    {
        if (drafts is not null && drafts[key] is { } draftNode)
        {
            return draftNode;
        }

        if (overviews is not null && overviews[key] is { } overviewNode)
        {
            return overviewNode;
        }

        return null;
    }

    private static int CountSnappedShapePoints(JsonNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        try
        {
            JsonObject? obj = node as JsonObject;
            if (obj is null)
            {
                var text = node.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                obj = JsonNode.Parse(text)?.AsObject();
            }

            if (obj?["snappedShape"] is JsonArray shape)
            {
                return shape.Count;
            }
        }
        catch
        {
            // defekte Einträge ignorieren
        }

        return 0;
    }

    public static string MergeIntoPackageJson(string baseJson, string liteJson)

    {

        var baseRoot = JsonNode.Parse(baseJson)?.AsObject()

            ?? throw new InvalidOperationException("Basis-Route-Paket konnte nicht gelesen werden.");

        var liteRoot = JsonNode.Parse(liteJson)?.AsObject()

            ?? throw new InvalidOperationException("Lite-Update konnte nicht gelesen werden.");



        foreach (var (key, value) in liteRoot)

        {

            if (value is null ||

                PreserveFromBaseKeys.Contains(key) ||

                key is "exportProfile" or "skipEmbeddedSounds" or "autoImport")

            {

                continue;

            }



            switch (key)

            {

                case "routeStops" when value is JsonObject liteStops:

                    MergeRouteStopsObject(baseRoot, liteStops);

                    break;

                case "routes" when value is JsonArray liteRoutes:

                    MergeRoutesArray(baseRoot, liteRoutes);

                    break;

                case "lineCourseRoutes" when value is JsonObject liteLineCourseRoutes:

                    MergeLineCourseRoutesObject(baseRoot, liteLineCourseRoutes);

                    break;

                case RouteItcsRouteListEditor.RootFieldName when value is JsonArray liteExcluded:

                    MergeRouteNameArray(baseRoot, RouteItcsRouteListEditor.RootFieldName, liteExcluded);

                    break;

                case RouteMainDeviceOnlyEditor.RootFieldName when value is JsonArray liteMainDeviceOnly:

                    MergeRouteNameArray(baseRoot, RouteMainDeviceOnlyEditor.RootFieldName, liteMainDeviceOnly);

                    break;

                default:

                    if (RouteKeyedObjectFields.Contains(key, StringComparer.Ordinal) && value is JsonObject liteRouteMap)

                    {

                        MergeRouteKeyedObject(baseRoot, key, liteRouteMap);

                    }

                    else

                    {

                        baseRoot[key] = value.DeepClone();

                    }



                    break;

            }

        }



        baseRoot.Remove("exportProfile");

        baseRoot.Remove("skipEmbeddedSounds");



        return baseRoot.ToJsonString();

    }



    private static IEnumerable<string> CollectRouteKeysFromLiteUpdate(string liteJson)

    {

        var root = JsonNode.Parse(liteJson)?.AsObject();

        if (root is null)

        {

            yield break;

        }



        if (root["routes"] is JsonArray routes)

        {

            foreach (var node in routes)

            {

                var name = node?.GetValue<string>()?.Trim();

                if (!string.IsNullOrWhiteSpace(name))

                {

                    yield return RouteDisplayHelper.ToCanonicalRouteKey(name);

                }

            }

        }



        if (root["lineCourseRoutes"] is JsonObject lineCourseRoutes)

        {

            foreach (var group in lineCourseRoutes)

            {

                if (group.Value is not JsonArray routesArray)

                {

                    continue;

                }



                foreach (var routeNode in routesArray.OfType<JsonObject>())

                {

                    var display = RouteDisplayHelper.ToDisplayString(ParseLineCourseRouteObject(routeNode, group.Key));

                    if (!string.IsNullOrWhiteSpace(display))

                    {

                        yield return RouteDisplayHelper.ToCanonicalRouteKey(display);

                    }

                }

            }

        }



        if (root["routeStops"] is JsonObject routeStops)

        {

            foreach (var routeKey in routeStops.Select(static pair => pair.Key))

            {

                if (!string.IsNullOrWhiteSpace(routeKey))

                {

                    yield return RouteDisplayHelper.ToCanonicalRouteKey(routeKey);

                }

            }

        }

    }



    private static void MergeRouteStopsObject(JsonObject baseRoot, JsonObject liteStops)

    {

        var baseStops = baseRoot["routeStops"] as JsonObject ?? new JsonObject();

        foreach (var (routeKey, stops) in liteStops)

        {

            baseStops[routeKey] = stops?.DeepClone();

        }



        baseRoot["routeStops"] = baseStops;

    }



    private static void MergeRoutesArray(JsonObject baseRoot, JsonArray liteRoutes)

    {

        var merged = new HashSet<string>(StringComparer.Ordinal);

        AddRoutesFromArray(baseRoot["routes"] as JsonArray, merged);

        AddRoutesFromArray(liteRoutes, merged);

        baseRoot["routes"] = new JsonArray(

            merged.OrderBy(static name => name, StringComparer.Ordinal)

                .Select(static name => JsonValue.Create(name))

                .ToArray<JsonNode?>());

    }



    private static void MergeLineCourseRoutesObject(JsonObject baseRoot, JsonObject liteLineCourseRoutes)

    {

        var baseLineCourseRoutes = baseRoot["lineCourseRoutes"] as JsonObject ?? new JsonObject();



        foreach (var (lineCourseKey, liteRoutesNode) in liteLineCourseRoutes)

        {

            if (liteRoutesNode is not JsonArray liteRoutesArray)

            {

                continue;

            }



            var mergedRoutes = new List<JsonObject>();

            if (baseLineCourseRoutes[lineCourseKey] is JsonArray baseRoutesArray)

            {

                foreach (var node in baseRoutesArray.OfType<JsonObject>())

                {

                    mergedRoutes.Add(node);

                }

            }



            foreach (var liteRouteNode in liteRoutesArray.OfType<JsonObject>())

            {

                var liteDefinition = ParseLineCourseRouteObject(liteRouteNode, lineCourseKey);

                var liteDisplay = RouteDisplayHelper.ToDisplayString(liteDefinition);

                mergedRoutes.RemoveAll(existing =>

                {

                    var existingDefinition = ParseLineCourseRouteObject(existing, lineCourseKey);

                    if (RouteDisplayHelper.RouteKeysMatch(

                            RouteDisplayHelper.ToDisplayString(existingDefinition),

                            liteDisplay))

                    {

                        return true;

                    }

                    // Umbenennung: gleiche Linie/Kurs + Fahrt, Basisname mit/ohne Datumspräfix

                    return RouteDisplayHelper.IsLikelyRenamedRoute(existingDefinition, liteDefinition);

                });

                mergedRoutes.Add(liteRouteNode.DeepClone().AsObject());

            }



            baseLineCourseRoutes[lineCourseKey] = new JsonArray(

                mergedRoutes.Select(static route => (JsonNode?)route).ToArray());

        }



        baseRoot["lineCourseRoutes"] = baseLineCourseRoutes;

    }



    private static void MergeRouteKeyedObject(JsonObject baseRoot, string fieldName, JsonObject liteMap)

    {

        var baseMap = baseRoot[fieldName] as JsonObject ?? new JsonObject();

        foreach (var (routeKey, routeValue) in liteMap)

        {

            if (routeValue is not null)

            {

                baseMap[routeKey] = routeValue.DeepClone();

            }

        }



        baseRoot[fieldName] = baseMap;

    }



    private static void MergeRouteNameArray(JsonObject baseRoot, string fieldName, JsonArray liteArray)

    {

        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddRoutesFromArray(baseRoot[fieldName] as JsonArray, merged);

        AddRoutesFromArray(liteArray, merged);

        baseRoot[fieldName] = new JsonArray(

            merged.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)

                .Select(static name => JsonValue.Create(name))

                .ToArray<JsonNode?>());

    }



    private static RouteDefinition ParseLineCourseRouteObject(JsonObject routeObj, string? fallbackLineCourse)

    {

        return new RouteDefinition(

            routeObj["name"]?.GetValue<string>() ?? string.Empty,

            routeObj["lineCourse"]?.GetValue<string>() ?? fallbackLineCourse ?? string.Empty,

            routeObj["tripNumber"]?.GetValue<string>() ?? string.Empty,

            routeObj["passengerDisplayLine"]?.GetValue<string>() ?? string.Empty);

    }



    private static void AddRoutesFromArray(JsonArray? array, HashSet<string> merged)

    {

        if (array is null)

        {

            return;

        }



        foreach (var node in array)

        {

            var name = node?.GetValue<string>()?.Trim();

            if (!string.IsNullOrWhiteSpace(name))

            {

                merged.Add(name);

            }

        }

    }

}


