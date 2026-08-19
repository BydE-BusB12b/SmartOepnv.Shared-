using System.Text.Json.Nodes;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Schreibt Route-Pakete im Format von GPSAnsagen „Route an alle Fahrzeuge senden“
/// (<see cref="RouteDistributionManager"/> / routes_export.json).
/// </summary>
public static class GpsAnsagenRouteExportSync
{
    public const string LiteExportProfile = "lite";

    private static readonly string[] LiteVehicleUpdateStripKeys =
    [
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
    ];

    public static void ApplyToPackage(EditableRoutePackage package, JsonObject root, LocalWorkspaceStore? workspace = null)
    {
        ApplyToPackage(package, root, workspace, rebuildEmbeddedMedia: true);
    }

    /// <param name="rebuildEmbeddedMedia">
    /// false = embeddedSounds/Sonderansagen-Audio im Root belassen (schneller lokaler Stop-/Routen-Save).
    /// true = vollständige Neu-Einbettung (Ansagen-Kartei, Fahrzeug-Export, …).
    /// </param>
    public static void ApplyToPackage(
        EditableRoutePackage package,
        JsonObject root,
        LocalWorkspaceStore? workspace,
        bool rebuildEmbeddedMedia)
    {
        var allStops = package.StopsByRoute.Values.SelectMany(s => s).ToList();
        // Nur noch vorhandene Routen folgen – gelöschte Routenwechsel-Ziele sonst als leere Hüllen.
        var collectedRoutes = RouteDistributionRouteCollector.CollectAllRoutesForDistribution(
            package.RouteNames,
            allStops,
            package.RouteNames);

        var packageRoutes = RoutePackageRouteKeyHelper
            .DistinctCanonicalKeys(
                collectedRoutes
                    .Union(package.RouteNames)
                    .Union(package.StopsByRoute.Keys.Where(k => package.StopsByRoute[k].Count > 0))
                    .Union(RoutePackagePhoneMetadata.GetRouteKeysFromBlock(root, "routePathDrafts"))
                    .Union(RoutePackagePhoneMetadata.GetRouteKeysFromBlock(root, "routeOfflineGuidance")))
            .ToList();

        SyncRoutesAndLineCourse(packageRoutes, root, package.RouteInteriorDisplayDestinationsByRoute);
        SyncRouteStops(package, packageRoutes, root);
        root.Remove("routeDirections");
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routeOfflineGuidance", packageRoutes);
        RoutePathDraftRepository.NormalizeDraftKeysToCanonical(root);
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routePathDrafts", packageRoutes);
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
        ManagedAnnouncementTemplateEditor.SaveToRoot(root, package.AnnouncementTemplates);
        if (rebuildEmbeddedMedia)
        {
            EnsureAnnouncementSoundsFromWorkspace(root, package, workspace);
            SyncEmbeddedSounds(package, root, workspace);
            SyncEndStopAnnouncementMetadata(package, root, workspace);
            SpecialAnnouncementsEditor.SyncToRootFromTemplates(root, package.AnnouncementTemplates, workspace);
        }

        RouteOperatingDaysEditor.SaveToRoot(root, packageRoutes, package.RouteOperatingDaysByRoute);
        RouteDateRangeEditor.SaveToRoot(root, packageRoutes, package.RouteDateRangesByRoute);
        RouteOperatingDatesEditor.SaveToRoot(root, packageRoutes, package.RouteOperatingDatesByRoute);
        RouteInteriorDisplayDestinationEditor.SaveToRoot(
            root,
            packageRoutes,
            package.RouteInteriorDisplayDestinationsByRoute);
        RouteItcsRouteListEditor.SaveToRoot(root, packageRoutes, package.RoutesExcludedFromItcsRouteList);
        RouteMainDeviceOnlyEditor.SaveToRoot(root, packageRoutes, package.RoutesMainDeviceOnly);
        AutoScheduleSourceRouteEditor.SaveToRoot(root, packageRoutes, package.AutoScheduleSourceByRoute);
    }

    private static void SyncEndStopAnnouncementMetadata(
        EditableRoutePackage package,
        JsonObject root,
        LocalWorkspaceStore? workspace)
    {
        var needsEndStopAudio = package.StopsByRoute.Values
            .SelectMany(stops => stops)
            .Any(s => s.PlayEndStopAnnouncement);

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
    /// Fehlt die referenzierte Datei, wird der Ton der verknüpften Haltestellen-Vorlage verwendet
    /// (z. B. Ansage 0162 → Stop-Vorlage 00138 mit 0081_…_zusammen.wav).
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

            // Expliziter Dateiname existiert nicht → Ton der verknüpften Haltestellen-Vorlage
            if (!string.IsNullOrWhiteSpace(name) &&
                !existing.ContainsKey(name) &&
                PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, name) is null &&
                (string.IsNullOrWhiteSpace(template.LocalAudioPath) || !File.Exists(template.LocalAudioPath)))
            {
                var fallback = TryResolveSoundFromLinkedStopTemplate(package, template);
                if (!string.IsNullOrWhiteSpace(fallback) &&
                    !string.Equals(fallback, name, StringComparison.OrdinalIgnoreCase))
                {
                    template.EmbeddedSoundFileName = fallback;
                    name = fallback;
                }
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

    private static string? TryResolveSoundFromLinkedStopTemplate(
        EditableRoutePackage package,
        ManagedAnnouncementTemplateItem announcement)
    {
        var stopId = announcement.StopTemplateId?.Trim() ?? string.Empty;
        if (stopId.Length == 0)
        {
            return null;
        }

        var stop = package.StopTemplates.FirstOrDefault(t =>
            string.Equals(t.Id?.Trim(), stopId, StringComparison.OrdinalIgnoreCase));
        var fileName = stop?.EmbeddedSoundFileName?.Trim();
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
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

    private static void SyncRouteStops(
        EditableRoutePackage package,
        IEnumerable<string> routesToExport,
        JsonObject root,
        bool replaceAll = true)
    {
        var routeStopsObject = replaceAll
            ? new JsonObject()
            : root["routeStops"] as JsonObject ?? new JsonObject();

        if (!replaceAll)
        {
            foreach (var canonical in routesToExport
                         .Select(RouteDisplayHelper.ToCanonicalRouteKey)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                routeStopsObject.Remove(RouteDisplayHelper.ToDistributionDisplayString(canonical));
            }
        }

        foreach (var canonical in routesToExport
                     .Select(RouteDisplayHelper.ToCanonicalRouteKey)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var distributionKey = RouteDisplayHelper.ToDistributionDisplayString(canonical);
            var stopsArray = new JsonArray();
            foreach (var stop in ResolveStopsForExport(package, canonical))
            {
                stopsArray.Add(GpsAnsagenStopJson.Write(stop, distributionKey));
            }

            routeStopsObject[distributionKey] = stopsArray;
        }

        root["routeStops"] = routeStopsObject;
    }

    private static IEnumerable<RouteStopItem> ResolveStopsForExport(EditableRoutePackage package, string canonical)
    {
        var stops = package.GetStops(canonical);
        if (stops.Count > 0)
        {
            return stops;
        }

        var storageKey = RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(canonical, package.StopsByRoute);
        if (!string.IsNullOrEmpty(storageKey) &&
            package.StopsByRoute.TryGetValue(storageKey, out var bucket) &&
            bucket.Count > 0)
        {
            return bucket;
        }

        return stops;
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
        LocalWorkspaceStore? workspace,
        bool liteVehicleUpdate = false)
    {
        if (selectedRouteNames.Count == 0)
        {
            throw new InvalidOperationException("Mindestens eine Route auswählen.");
        }

        var allStops = package.StopsByRoute.Values.SelectMany(s => s).ToList();
        var routesToExport = RouteDistributionRouteCollector.CollectAllRoutesForDistribution(
            selectedRouteNames,
            allStops,
            package.RouteNames);

        var root = JsonNode.Parse(package.ToJson()) as JsonObject
            ?? throw new InvalidOperationException("Route-Paket konnte nicht gelesen werden.");

        ApplyRouteSubsetToRoot(
            package,
            root,
            routesToExport,
            workspace,
            pruneOthersOnDevice,
            includeEmbeddedSounds: !liteVehicleUpdate);

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

        if (liteVehicleUpdate)
        {
            SyncLiteVehiclePhoneMetadata(package, root);
            // Senden (prune): Katalog ersetzen. Update (Merge): Geschwisterfahrten behalten.
            ApplyLiteVehicleUpdateMetadata(root, replaceLineCourseRoutes: pruneOthersOnDevice);
        }

        root["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        root["autoImport"] = true;
        return root.ToJsonString();
    }

    /// <summary>
    /// Vollständiges Routenpaket ohne Audio/Sonderansagen – für <see cref="DropboxConstants.RouteUpdateFileName"/>.
    /// Bestehende Tondateien auf dem Gerät bleiben erhalten (Merge-Import).
    /// </summary>
    public static string BuildFullLiteVehicleUpdateJson(
        EditableRoutePackage package,
        LocalWorkspaceStore? workspace)
    {
        var root = JsonNode.Parse(package.ToJson()) as JsonObject
            ?? throw new InvalidOperationException("Route-Paket konnte nicht gelesen werden.");

        ApplyLiteRouteDataToRoot(package, root, workspace);
        ApplyLiteVehicleUpdateMetadata(root, replaceLineCourseRoutes: true);
        root["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        root["autoImport"] = true;
        return root.ToJsonString();
    }

    private static void ApplyLiteRouteDataToRoot(
        EditableRoutePackage package,
        JsonObject root,
        LocalWorkspaceStore? workspace)
    {
        var allStops = package.StopsByRoute.Values.SelectMany(s => s).ToList();
        var packageRoutes = RoutePackageRouteKeyHelper
            .DistinctCanonicalKeys(
                package.RouteNames
                    .Union(package.StopsByRoute.Keys.Where(k => package.StopsByRoute[k].Count > 0))
                    .Union(RoutePackagePhoneMetadata.GetRouteKeysFromBlock(root, "routePathDrafts"))
                    .Union(RoutePackagePhoneMetadata.GetRouteKeysFromBlock(root, "routeOfflineGuidance")))
            .ToList();

        SyncRoutesAndLineCourse(packageRoutes, root, package.RouteInteriorDisplayDestinationsByRoute);
        SyncRouteStops(package, packageRoutes, root);
        root.Remove("routeDirections");
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routeOfflineGuidance", packageRoutes);
        RoutePathDraftRepository.NormalizeDraftKeysToCanonical(root);
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routePathDrafts", packageRoutes);
        RouteOperatingDaysEditor.SaveToRoot(root, packageRoutes, package.RouteOperatingDaysByRoute);
        RouteDateRangeEditor.SaveToRoot(root, packageRoutes, package.RouteDateRangesByRoute);
        RouteOperatingDatesEditor.SaveToRoot(root, packageRoutes, package.RouteOperatingDatesByRoute);
        RouteInteriorDisplayDestinationEditor.SaveToRoot(
            root,
            packageRoutes,
            package.RouteInteriorDisplayDestinationsByRoute);
        RouteItcsRouteListEditor.SaveToRoot(root, packageRoutes, package.RoutesExcludedFromItcsRouteList);
        RouteMainDeviceOnlyEditor.SaveToRoot(root, packageRoutes, package.RoutesMainDeviceOnly);
        AutoScheduleSourceRouteEditor.SaveToRoot(root, packageRoutes, package.AutoScheduleSourceByRoute);
        SyncLiteVehiclePhoneMetadata(package, root);
    }

    private static void SyncLiteVehiclePhoneMetadata(EditableRoutePackage package, JsonObject root)
    {
        DateBasedHintsEditor.SaveToRoot(root, package.DateBasedHints);
        RoutePackagePhoneMetadata.SaveOutsideDisplays(root, package.OutsideDisplays);
    }

    private static void ApplyLiteVehicleUpdateMetadata(
        JsonObject root,
        bool replaceLineCourseRoutes)
    {
        StripLiteVehicleUpdateFields(root);
        root["exportProfile"] = LiteExportProfile;
        root["skipEmbeddedSounds"] = true;
        // true: vollständiger Lite-Katalog oder Senden (Allowlist) – App ersetzt lineCourseRoutes.
        // false: Teil-Update – mergen, sonst verschwinden Geschwisterfahrten derselben Linie/Kurs.
        root["replaceLineCourseRoutes"] = replaceLineCourseRoutes;
    }

    private static void StripLiteVehicleUpdateFields(JsonObject root)
    {
        foreach (var key in LiteVehicleUpdateStripKeys)
        {
            root.Remove(key);
        }
    }

    private static void ApplyRouteSubsetToRoot(
        EditableRoutePackage package,
        JsonObject root,
        HashSet<string> routesToExport,
        LocalWorkspaceStore? workspace,
        bool pruneOthersOnDevice,
        bool includeEmbeddedSounds = true)
    {
        if (pruneOthersOnDevice)
        {
            SyncRoutesAndLineCourse(
                routesToExport,
                root,
                package.RouteInteriorDisplayDestinationsByRoute);
            SyncRouteStops(package, routesToExport, root, replaceAll: true);
            RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routeOfflineGuidance", routesToExport);
            RoutePathDraftRepository.NormalizeDraftKeysToCanonical(root);
            RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routePathDrafts", routesToExport);
        }
        else
        {
            SyncRouteStops(package, routesToExport, root, replaceAll: false);
            SyncRoutesAndLineCourse(
                routesToExport,
                root,
                package.RouteInteriorDisplayDestinationsByRoute);
        }

        root.Remove("routeDirections");
        if (includeEmbeddedSounds)
        {
            SyncEmbeddedSoundsForRoutes(package, root, routesToExport, workspace);
        }
    }

    private static void SyncEmbeddedSoundsForRoutes(
        EditableRoutePackage package,
        JsonObject root,
        HashSet<string> routesToExport,
        LocalWorkspaceStore? workspace)
    {
        var exportedStops = package.StopsByRoute
            .Where(kv => RoutePackageRouteKeyHelper.IsRouteKeyAllowed(kv.Key, routesToExport))
            .SelectMany(kv => kv.Value)
            .ToList();

        var names = EmbeddedSoundReferences
            .CollectFromPackage(package, root, workspace, exportedStops)
            .ToList();

        GpsAnsagenEmbeddedSoundsJson.SyncToRoot(root, names, workspace);
    }
}
