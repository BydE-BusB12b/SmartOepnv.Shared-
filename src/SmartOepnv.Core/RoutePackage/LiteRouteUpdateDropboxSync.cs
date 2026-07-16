using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Leitstelle: <c>routes_update.json</c> aus Dropbox in den lokalen Editor mergen
/// (Fernroute-Liste, Karte – ohne Voll-Export mit Audio).
/// </summary>
public static class LiteRouteUpdateDropboxSync
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
