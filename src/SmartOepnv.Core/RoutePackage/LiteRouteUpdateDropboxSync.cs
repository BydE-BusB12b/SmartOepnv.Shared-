using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Leitstelle-Fallback: <c>routes_update.json</c> nur wenn kein <c>leitstelle_routes.json</c>
/// vorliegt. Sonst würden ältere Fahrzeug-Updates die Planer-Fahrwege überschreiben.
/// </summary>
public static class LiteRouteUpdateDropboxSync
{
    public sealed record ImportResult(bool Imported, string Message);

    public static async Task<ImportResult> TryMergeFromDropboxAsync(
        CancellationToken ct = default,
        bool skipWhenLeitstelleRoutesPresent = false)
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
            if (skipWhenLeitstelleRoutesPresent)
            {
                var leitstelleRoutesJson = await AppServices.Dropbox
                    .TryDownloadNamedFileAsync(DropboxConstants.LeitstelleRoutesFileName, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(leitstelleRoutesJson) &&
                    LiteRouteUpdateMerge.IsLiteVehicleUpdate(leitstelleRoutesJson))
                {
                    return new ImportResult(
                        false,
                        $"{DropboxConstants.RouteUpdateFileName} übersprungen – " +
                        $"{DropboxConstants.LeitstelleRoutesFileName} ist maßgeblich.");
                }
            }

            var updateJson = await AppServices.Dropbox
                .TryDownloadNamedFileAsync(DropboxConstants.RouteUpdateFileName, ct)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(updateJson))
            {
                return new ImportResult(false, $"Keine {DropboxConstants.RouteUpdateFileName} in Dropbox.");
            }

            if (!LiteRouteUpdateMerge.IsLiteVehicleUpdate(updateJson))
            {
                return new ImportResult(false, $"{DropboxConstants.RouteUpdateFileName} ist kein Lite-Update.");
            }

            if (!AppServices.Routes.TryMergeLiteRouteUpdateJson(updateJson, out var message))
            {
                return new ImportResult(false, message);
            }

            return new ImportResult(true, message);
        }
        catch (Exception ex)
        {
            return new ImportResult(false, $"Lite-Update fehlgeschlagen: {ex.Message}");
        }
    }
}
