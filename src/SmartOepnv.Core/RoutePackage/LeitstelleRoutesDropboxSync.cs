using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Planer → Leitstelle: Routen/Haltestellen/Fahrwege ohne Audio als
/// <c>leitstelle_routes.json</c> (Fahrzeuge lesen diese Datei nicht).
/// </summary>
public static class LeitstelleRoutesDropboxSync
{
    public sealed record ImportResult(bool Imported, string Message);

    public static async Task<ImportResult> TryMergeFromDropboxAsync(CancellationToken ct = default)
    {
        if (!AppServices.IsInitialized)
        {
            return new ImportResult(false, "AppServices nicht initialisiert.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new ImportResult(false, "Dropbox nicht verbunden.");
        }

        try
        {
            var updateJson = await AppServices.Dropbox
                .TryDownloadNamedFileAsync(DropboxConstants.LeitstelleRoutesFileName, ct)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(updateJson))
            {
                return new ImportResult(false, $"Keine {DropboxConstants.LeitstelleRoutesFileName} in Dropbox.");
            }

            if (!LiteRouteUpdateMerge.IsLiteVehicleUpdate(updateJson))
            {
                return new ImportResult(false, $"{DropboxConstants.LeitstelleRoutesFileName} ist kein Lite-Routenpaket.");
            }

            if (!AppServices.Routes.TryMergeLiteRouteUpdateJson(
                    updateJson,
                    out var message,
                    trackLeitstelleRoutes: true))
            {
                return new ImportResult(false, message);
            }

            return new ImportResult(true, message);
        }
        catch (Exception ex)
        {
            return new ImportResult(false, $"Leitstelle-Routen fehlgeschlagen: {ex.Message}");
        }
    }
}
