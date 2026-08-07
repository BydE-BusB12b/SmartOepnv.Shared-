using System.Text.Json;
using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Kompakt-Paket für die Leitstelle: Fahrer, Fahrzeuge, Nachrichten/Mail-Vorlagen,
/// Außenanzeigen-Zielliste (<c>outsideDisplays</c>) und vereinfachte Fahrwege für die Karte.
/// Wird als <c>leitstelle_stand.json</c> verteilt; Routen/Haltestellen/Snaps gehen parallel
/// als <c>leitstelle_routes.json</c> mit (nicht <c>routes_update.json</c> – das bleibt für Fahrzeuge).
/// </summary>
public static class LeitstelleStandPackage
{
    public static string BuildJson(EditableRoutePackage package)
    {
        // PackageRoot inkl. routePathDrafts aktualisieren, sonst sind Karten-Überblicke leer.
        _ = package.ToJson(indented: false, rebuildEmbeddedMedia: false, includeHeavyMedia: false);

        var root = new JsonObject
        {
            ["version"] = "1.3",
            ["exportType"] = "leitstelleStand",
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["autoImport"] = false,
        };

        EmployeeRosterEditor.SaveToRoot(root, package.Employees);
        RegisteredVehiclesEditor.SaveToRoot(
            root,
            package.RegisteredVehicles,
            package.RegisteredVehiclePhoneRedirects);
        MessageTemplatesEditor.SaveToRoot(root, package.MessageTemplates, package.MailTemplates);
        RoutePackagePhoneMetadata.SaveOutsideDisplays(root, package.OutsideDisplays);
        root[LeitstelleRoutePathOverview.OverviewsKey] =
            LeitstelleRoutePathOverview.BuildOverviewsObject(package);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static void ApplyToEditor(EditableRoutePackage editor, JsonObject root)
    {
        var employees = EmployeeRosterEditor.LoadFromRoot(root).ToList();
        if (employees.Count > 0)
        {
            editor.ReplaceEmployees(employees);
        }

        var vehicles = RegisteredVehiclesEditor.LoadFromRoot(root).ToList();
        var phoneRedirects = RegisteredVehiclesEditor.LoadPhoneRedirectsFromRoot(root).ToList();
        if (vehicles.Count > 0 || phoneRedirects.Count > 0)
        {
            editor.ReplaceRegisteredVehicles(vehicles);
            editor.ReplaceRegisteredVehiclePhoneRedirects(phoneRedirects);
        }

        var messageTemplates = MessageTemplatesEditor.LoadMessageTemplates(root).ToList();
        var mailTemplates = MessageTemplatesEditor.LoadMailTemplates(root).ToList();
        if (messageTemplates.Count > 0 || mailTemplates.Count > 0)
        {
            editor.ReplaceMessageTemplates(messageTemplates, mailTemplates);
        }

        if (root["outsideDisplays"] is JsonArray outside && outside.Count > 0)
        {
            editor.ReplaceOutsideDisplays([.. RoutePackagePhoneMetadata.LoadOutsideDisplays(root)]);
        }
        else if (root["destinationList"] is JsonArray legacy && legacy.Count > 0)
        {
            editor.ReplaceOutsideDisplays([.. RoutePackagePhoneMetadata.LoadOutsideDisplays(root)]);
        }

        LeitstelleRoutePathOverview.ApplyOverviewsToEditor(editor, root);
    }
}
