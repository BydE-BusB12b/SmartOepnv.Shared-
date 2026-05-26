using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Schreibt Route-Pakete im Format von GPSAnsagen „Route an alle Fahrzeuge senden“
/// (<see cref="RouteDistributionManager"/> / routes_export.json).
/// </summary>
public static class GpsAnsagenRouteExportSync
{
    private static readonly Regex LineCourseSuffix = new(
        @"\(Linie:\s*([^,]+),\s*Fahrt:\s*([^)]+)\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        SyncRoutesAndLineCourse(packageRoutes, root);
        SyncRouteStops(package, packageRoutes, root);
        root.Remove("routeDirections");
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routeOfflineGuidance");
        RoutePackagePhoneMetadata.SyncStringKeyedRouteBlocks(root, "routePathDrafts");
        DateBasedHintsEditor.SaveToRoot(root, package.DateBasedHints);
        RoutePackagePhoneMetadata.SaveOutsideDisplays(root, package.OutsideDisplays);
        RoutePackagePhoneMetadata.SaveAllowedRoutes(root, collectedRoutes, package.AdditionalAllowedRoutes);
        EmployeeRosterEditor.SaveToRoot(root, package.Employees);
        RegisteredVehiclesEditor.SaveToRoot(
            root,
            package.RegisteredVehicles,
            package.RegisteredVehiclePhoneRedirects);
        MessageTemplatesEditor.SaveToRoot(root, package.MessageTemplates, package.MailTemplates);
        ManagedStopTemplateEditor.SaveToRoot(root, package.StopTemplates);
        ManagedAnnouncementTemplateEditor.SaveToRoot(root, package.AnnouncementTemplates);
        SpecialAnnouncementsEditor.SyncToRootFromTemplates(root, package.AnnouncementTemplates, workspace);
        SyncEmbeddedSounds(package, root, workspace);
    }

    private static void SyncRoutesAndLineCourse(IEnumerable<string> routesToExport, JsonObject root)
    {
        var simpleRoutes = new JsonArray();
        var lineCourseRoutes = new JsonObject();

        foreach (var routeName in routesToExport.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var match = LineCourseSuffix.Match(routeName);
            if (!match.Success)
            {
                simpleRoutes.AddString(routeName);
                continue;
            }

            var lineCourse = match.Groups[1].Value.Trim();
            var tripNumber = match.Groups[2].Value.Trim();
            var pureName = routeName[..match.Index].TrimEnd();

            if (lineCourseRoutes[lineCourse] is not JsonArray arr)
            {
                arr = new JsonArray();
                lineCourseRoutes[lineCourse] = arr;
            }

            arr.Add(new JsonObject
            {
                ["name"] = pureName,
                ["lineCourse"] = lineCourse,
                ["tripNumber"] = tripNumber
            });
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
            var stopsArray = new JsonArray();
            foreach (var stop in package.GetStops(route))
            {
                stopsArray.Add(GpsAnsagenStopJson.Write(stop, route));
            }

            routeStopsObject[route] = stopsArray;
        }

        root["routeStops"] = routeStopsObject;
    }

    private static void SyncEmbeddedSounds(
        EditableRoutePackage package,
        JsonObject root,
        LocalWorkspaceStore? workspace)
    {
        var names = package.StopsByRoute.Values
            .SelectMany(stops => stops)
            .Select(s => s.EmbeddedSoundFileName)
            .Concat(package.StopTemplates.Select(t => t.EmbeddedSoundFileName))
            .Concat(package.AnnouncementTemplates.Select(t => t.EmbeddedSoundFileName));

        GpsAnsagenEmbeddedSoundsJson.SyncToRoot(root, names, workspace);
    }
}
