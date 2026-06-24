using System.Text.Json.Nodes;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Schreibt Route-Pakete im Format von GPSAnsagen „Route an alle Fahrzeuge senden“
/// (<see cref="RouteDistributionManager"/> / routes_export.json).
/// </summary>
public static class GpsAnsagenRouteExportSync
{
    public static void ApplyToPackage(EditableRoutePackage package, JsonObject root, LocalWorkspaceStore? workspace = null)
    {
        var allStops = package.StopsByRoute.Values.SelectMany(s => s).ToList();
        var collectedRoutes = RouteDistributionRouteCollector.CollectAllRoutesForDistribution(
            package.RouteNames,
            allStops);

        // Vollständiges Planer-Paket (lokal + Dropbox zwischen Planern): alle Routen/Navidaten behalten.
        var packageRoutes = collectedRoutes
            .Union(package.RouteNames)
            .Union(package.StopsByRoute.Keys)
            .Union(RoutePackagePhoneMetadata.GetRouteKeysFromBlock(root, "routePathDrafts"))
            .Union(RoutePackagePhoneMetadata.GetRouteKeysFromBlock(root, "routeOfflineGuidance"));

        SyncRoutesAndLineCourse(packageRoutes, root, package.RouteInteriorDisplayDestinationsByRoute);
        SyncRouteStops(package, packageRoutes, root);
        root.Remove("routeDirections");
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routeOfflineGuidance");
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routePathDrafts");
        DateBasedHintsEditor.SaveToRoot(root, package.DateBasedHints);
        RoutePackagePhoneMetadata.SaveOutsideDisplays(root, package.OutsideDisplays);
        RoutePackagePhoneMetadata.SaveAllowedRoutes(
            root,
            collectedRoutes.Select(RouteDisplayHelper.ToDistributionDisplayString),
            package.AdditionalAllowedRoutes);
        EmployeeRosterEditor.SaveToRoot(root, package.Employees);
        RegisteredVehiclesEditor.SaveToRoot(
            root,
            package.RegisteredVehicles,
            package.RegisteredVehiclePhoneRedirects);
        MessageTemplatesEditor.SaveToRoot(root, package.MessageTemplates, package.MailTemplates);
        ManagedStopTemplateEditor.SaveToRoot(root, package.StopTemplates);
        AnnouncementSoundFileResolver.ApplyResolvedFileNames(
            package.AnnouncementTemplates,
            root,
            workspace);
        EnsureAnnouncementSoundsFromWorkspace(root, package, workspace);
        ManagedAnnouncementTemplateEditor.SaveToRoot(root, package.AnnouncementTemplates);
        SyncEmbeddedSounds(package, root, workspace);
        SyncEndStopAnnouncementMetadata(package, root, workspace);
        SpecialAnnouncementsEditor.SyncToRootFromTemplates(root, package.AnnouncementTemplates, workspace);
        RouteOperatingDaysEditor.SaveToRoot(root, packageRoutes, package.RouteOperatingDaysByRoute);
        RouteInteriorDisplayDestinationEditor.SaveToRoot(
            root,
            packageRoutes,
            package.RouteInteriorDisplayDestinationsByRoute);
    }

    private static void SyncEndStopAnnouncementMetadata(
        EditableRoutePackage package,
        JsonObject root,
        LocalWorkspaceStore? workspace)
    {
        var needsEndStopAudio = package.StopsByRoute.Values
            .SelectMany(stops => stops)
            .Any(s => s.IsEndStop && s.PlayEndStopAnnouncement);

        if (!needsEndStopAudio)
        {
            root.Remove(EndStopAnnouncementResolver.RootJsonFieldName);
            return;
        }

        var fileName = EndStopAnnouncementResolver.TryResolveEmbeddedFileName(
            package.AnnouncementTemplates,
            root,
            workspace);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            root.Remove(EndStopAnnouncementResolver.RootJsonFieldName);
            return;
        }

        root[EndStopAnnouncementResolver.RootJsonFieldName] = fileName;
    }

    /// <summary>
    /// Legt fehlende Ansagen-Töne aus dem Workspace in <c>embeddedSounds</c> ab (Sonderansagen inkl.).
    /// </summary>
    private static void EnsureAnnouncementSoundsFromWorkspace(
        JsonObject root,
        EditableRoutePackage package,
        LocalWorkspaceStore? workspace)
    {
        if (workspace is null)
        {
            return;
        }

        var existing = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(root);
        foreach (var template in package.AnnouncementTemplates)
        {
            var name = AnnouncementSoundFileResolver.TryResolve(template, root, workspace)?.Trim();
            if (!string.IsNullOrWhiteSpace(name) &&
                string.IsNullOrWhiteSpace(template.EmbeddedSoundFileName))
            {
                template.EmbeddedSoundFileName = name;
            }

            if (string.IsNullOrWhiteSpace(name) || existing.ContainsKey(name))
            {
                continue;
            }

            var path = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, name);
            if (path is null && !string.IsNullOrWhiteSpace(template.LocalAudioPath) && File.Exists(template.LocalAudioPath))
            {
                path = template.LocalAudioPath;
            }

            if (path is null)
            {
                continue;
            }

            try
            {
                EmbeddedSoundsEditor.UpsertFromFile(root, name, path);
                existing = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(root);
            }
            catch
            {
                // Einzelne Datei überspringen
            }
        }
    }

    private static void SyncRoutesAndLineCourse(
        IEnumerable<string> routesToExport,
        JsonObject root,
        IDictionary<string, string> interiorDisplayDestinations)
    {
        var simpleRoutes = new JsonArray();
        var lineCourseRoutes = new JsonObject();

        foreach (var routeName in routesToExport.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var parsed = RouteDisplayHelper.Parse(routeName);
            if (string.IsNullOrWhiteSpace(parsed.LineCourse) && string.IsNullOrWhiteSpace(parsed.TripNumber))
            {
                simpleRoutes.AddString(routeName);
                continue;
            }

            var lineCourse = RouteDisplayHelper.NormalizeLineCourse(parsed.LineCourse);
            var tripNumber = RouteDisplayHelper.NormalizeTripNumber(parsed.TripNumber);
            var pureName = parsed.Name.Trim();
            var passengerLine = (parsed.PassengerDisplayLine ?? string.Empty).Trim();

            if (lineCourseRoutes[lineCourse] is not JsonArray arr)
            {
                arr = new JsonArray();
                lineCourseRoutes[lineCourse] = arr;
            }

            var routeObj = new JsonObject
            {
                ["name"] = pureName,
                ["lineCourse"] = lineCourse,
                ["tripNumber"] = tripNumber
            };
            if (!string.IsNullOrEmpty(passengerLine))
            {
                routeObj["passengerDisplayLine"] = passengerLine;
            }

            var displayKey = RouteDisplayHelper.ToDisplayString(
                new RouteDefinition(pureName, lineCourse, tripNumber, passengerLine));
            if (!string.IsNullOrWhiteSpace(displayKey))
            {
                var interiorDestination = RouteInteriorDisplayDestinationEditor.GetForRoute(
                    interiorDisplayDestinations,
                    displayKey);
                if (!string.IsNullOrEmpty(interiorDestination))
                {
                    routeObj["interiorDestinationText"] = interiorDestination;
                }
            }

            arr.Add(routeObj);
        }

        root["routes"] = simpleRoutes;
        if (lineCourseRoutes.Count > 0)
        {
            root["lineCourseRoutes"] = lineCourseRoutes;
        }
        else
        {
            root.Remove("lineCourseRoutes");
        }
    }

    private static void SyncRouteStops(EditableRoutePackage package, IEnumerable<string> routesToExport, JsonObject root)
    {
        var routeStopsObject = new JsonObject();
        foreach (var route in routesToExport.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var distributionKey = RouteDisplayHelper.ToDistributionDisplayString(route);
            var stopsArray = new JsonArray();
            foreach (var stop in package.GetStops(route))
            {
                stopsArray.Add(GpsAnsagenStopJson.Write(stop, distributionKey));
            }

            routeStopsObject[distributionKey] = stopsArray;
        }

        root["routeStops"] = routeStopsObject;
    }

    private static void SyncEmbeddedSounds(
        EditableRoutePackage package,
        JsonObject root,
        LocalWorkspaceStore? workspace)
    {
        var names = EmbeddedSoundReferences.CollectFromPackage(package, root, workspace).ToList();
        GpsAnsagenEmbeddedSoundsJson.SyncToRoot(root, names, workspace);
    }

    /// <summary>
    /// Teilpaket für Fahrzeuge: nur ausgewählte Routen (inkl. Routenwechsel-Ziele).
    /// <paramref name="pruneOthersOnDevice"/> = true setzt <c>allowedRoutes</c> (Senden), false entfernt die Allowlist (Update).
    /// </summary>
    public static string BuildVehicleTransferJson(
        EditableRoutePackage package,
        IReadOnlyList<string> selectedRouteNames,
        bool pruneOthersOnDevice,
        LocalWorkspaceStore? workspace)
    {
        if (selectedRouteNames.Count == 0)
        {
            throw new InvalidOperationException("Mindestens eine Route auswählen.");
        }

        var allStops = package.StopsByRoute.Values.SelectMany(s => s).ToList();
        var routesToExport = RouteDistributionRouteCollector.CollectAllRoutesForDistribution(
            selectedRouteNames,
            allStops);

        var root = JsonNode.Parse(package.ToJson()) as JsonObject
            ?? throw new InvalidOperationException("Route-Paket konnte nicht gelesen werden.");

        ApplyRouteSubsetToRoot(package, root, routesToExport, workspace);

        if (pruneOthersOnDevice)
        {
            RoutePackagePhoneMetadata.SaveAllowedRoutes(
                root,
                routesToExport.Select(RouteDisplayHelper.ToDistributionDisplayString),
                package.AdditionalAllowedRoutes);
        }
        else
        {
            root.Remove("allowedRoutes");
        }

        root["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        root["autoImport"] = true;
        return root.ToJsonString();
    }

    private static void ApplyRouteSubsetToRoot(
        EditableRoutePackage package,
        JsonObject root,
        HashSet<string> routesToExport,
        LocalWorkspaceStore? workspace)
    {
        SyncRoutesAndLineCourse(routesToExport, root, package.RouteInteriorDisplayDestinationsByRoute);
        SyncRouteStops(package, routesToExport, root);
        root.Remove("routeDirections");
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routeOfflineGuidance", routesToExport);
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routePathDrafts", routesToExport);
        SyncEmbeddedSoundsForRoutes(package, root, routesToExport, workspace);
    }

    private static void SyncEmbeddedSoundsForRoutes(
        EditableRoutePackage package,
        JsonObject root,
        HashSet<string> routesToExport,
        LocalWorkspaceStore? workspace)
    {
        var exportedStops = package.StopsByRoute
            .Where(kv => routesToExport.Contains(kv.Key))
            .SelectMany(kv => kv.Value)
            .ToList();

        var names = EmbeddedSoundReferences
            .CollectFromPackage(package, root, workspace, exportedStops)
            .ToList();

        GpsAnsagenEmbeddedSoundsJson.SyncToRoot(root, names, workspace);
    }
}
