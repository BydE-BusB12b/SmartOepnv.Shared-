using System.Text.Json;
using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Kompakt-Paket für die Leitstelle: Fahrer, Fahrzeuge, Nachrichten/Mail-Vorlagen
/// und vereinfachte Fahrwege für die Karte (<c>routePathOverviews</c>).
/// Wird als eigene Dropbox-Datei verteilt (Dateiname <c>leitstelle_stand.json</c>).
/// Vollständige Routen/Haltestellen bleiben unverändert.
/// </summary>
public static class LeitstelleStandPackage
{
    public static string BuildJson(EditableRoutePackage package)
    {
        var root = new JsonObject
        {
            ["version"] = "1.1",
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
        root[LeitstelleRoutePathOverview.OverviewsKey] =
            LeitstelleRoutePathOverview.BuildOverviewsObject(package);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static void ApplyToEditor(EditableRoutePackage editor, JsonObject root)
    {
        editor.ReplaceEmployees([.. EmployeeRosterEditor.LoadFromRoot(root)]);
        editor.ReplaceRegisteredVehicles([.. RegisteredVehiclesEditor.LoadFromRoot(root)]);
        editor.ReplaceRegisteredVehiclePhoneRedirects([
            .. RegisteredVehiclesEditor.LoadPhoneRedirectsFromRoot(root)
        ]);

        editor.ReplaceMessageTemplates(
            [.. MessageTemplatesEditor.LoadMessageTemplates(root)],
            [.. MessageTemplatesEditor.LoadMailTemplates(root)]);

        LeitstelleRoutePathOverview.ApplyOverviewsToEditor(editor, root);
    }
}
