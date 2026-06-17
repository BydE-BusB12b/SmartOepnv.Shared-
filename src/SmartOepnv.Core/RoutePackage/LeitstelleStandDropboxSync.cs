using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Planer: <c>leitstelle_stand.json</c> nach Dropbox (beim Speichern/Beenden automatisch).
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
            return new ExportResult(false, "Kein Paket geladen – leitstelle_stand.json übersprungen.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new ExportResult(false, "Dropbox nicht verbunden – leitstelle_stand.json nicht hochgeladen.");
        }

        try
        {
            var json = AppServices.Routes.BuildLeitstelleStandJson();
            await AppServices.Dropbox.UploadLeitstelleStandAsync(json, ct).ConfigureAwait(false);
            return new ExportResult(
                true,
                $"Leitstelle-Stand in Dropbox gespeichert ({DropboxConstants.LeitstelleStandFileName}).");
        }
        catch (Exception ex)
        {
            return new ExportResult(false, $"Leitstelle-Stand-Export fehlgeschlagen: {ex.Message}");
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
