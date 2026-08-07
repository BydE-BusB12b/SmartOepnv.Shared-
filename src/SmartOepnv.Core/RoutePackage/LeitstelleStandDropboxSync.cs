using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Planer: <c>leitstelle_stand.json</c> + <c>leitstelle_routes.json</c> nach Dropbox
/// („Für Leitstelle speichern“). Nicht <c>routes_update.json</c> – das bleibt Fahrzeug-only.
/// </summary>
public static class LeitstelleStandDropboxSync
{
    public sealed record ExportResult(bool Exported, string Message);

    public static async Task<ExportResult> TryExportAsync(CancellationToken ct = default)
    {
        if (!AppServices.IsPlannerApp)
        {
            return new ExportResult(false, "Nur im Planer verfügbar.");
        }

        if (!AppServices.Routes.HasPackage || AppServices.Routes.Editor is null)
        {
            return new ExportResult(false, "Kein Paket geladen – Leitstelle-Stand übersprungen.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new ExportResult(false, "Dropbox nicht verbunden – Leitstelle-Stand nicht hochgeladen.");
        }

        try
        {
            AppServices.FlushAllPendingEditsBestEffort();

            var standJson = AppServices.Routes.BuildLeitstelleStandJson();
            await AppServices.Dropbox.UploadLeitstelleStandAsync(standJson, ct).ConfigureAwait(false);

            // Eigene Datei nur für die Leitstelle – Fahrzeuge importieren routes_update.json, nicht diese.
            var liteJson = AppServices.Routes.PrepareFullLiteVehicleUpdateJson();
            // Volle Karten-Overviews aus Drafts – nicht die ggf. unvollständigen Cache-Einträge im PackageRoot.
            if (AppServices.Routes.Editor is { } editor)
            {
                var liteRoot = System.Text.Json.Nodes.JsonNode.Parse(liteJson)?.AsObject();
                if (liteRoot is not null)
                {
                    liteRoot[LeitstelleRoutePathOverview.OverviewsKey] =
                        LeitstelleRoutePathOverview.BuildOverviewsObject(editor);
                    liteJson = liteRoot.ToJsonString();
                }
            }

            await AppServices.Dropbox
                .UploadNamedFileAsync(DropboxConstants.LeitstelleRoutesFileName, liteJson, ct)
                .ConfigureAwait(false);

            return new ExportResult(
                true,
                $"Leitstelle gespeichert: {DropboxConstants.LeitstelleStandFileName} + " +
                $"{DropboxConstants.LeitstelleRoutesFileName} (Routen, Haltestellen, Fahrwege – ohne Fahrzeug-Update).");
        }
        catch (Exception ex)
        {
            return new ExportResult(false, $"Leitstelle-Export fehlgeschlagen: {ex.Message}");
        }
    }

    public sealed record ImportResult(bool Imported, string Message);

    /// <summary>Leitstelle: <c>leitstelle_stand.json</c> aus Dropbox in den Editor mergen.</summary>
    public static async Task<ImportResult> TryMergeFromDropboxAsync(CancellationToken ct = default)
    {
        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            return new ImportResult(false, "Kein Route-Paket geladen – Leitstelle-Stand nicht übernommen.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new ImportResult(false, "Dropbox nicht verbunden.");
        }

        try
        {
            var stand = await AppServices.Dropbox.TryDownloadLeitstelleStandAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(stand))
            {
                return new ImportResult(false, $"Keine {DropboxConstants.LeitstelleStandFileName} in Dropbox.");
            }

            if (!RoutePackageRosterPreserve.JsonContainsRosterData(stand))
            {
                return new ImportResult(
                    false,
                    $"{DropboxConstants.LeitstelleStandFileName} ohne Personal/Fahrzeuge – bestehender Stand unverändert.");
            }

            AppServices.Routes.TryMergeLeitstelleStandJson(stand);
            return new ImportResult(
                true,
                $"Leitstelle-Stand übernommen ({DropboxConstants.LeitstelleStandFileName}).");
        }
        catch (Exception ex)
        {
            return new ImportResult(false, $"Leitstelle-Stand-Laden fehlgeschlagen: {ex.Message}");
        }
    }
}
