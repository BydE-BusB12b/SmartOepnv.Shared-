using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Planer: planer_workspace.json mit Dropbox abgleichen (Anmeldung laden, Beenden speichern).
/// routes_export.json bleibt unberührt – die wird nur manuell für die Apps exportiert.
/// </summary>
public static class PlanerDropboxWorkspaceSync
{
    public sealed record ImportResult(bool Imported, long RemoteTimestamp, long LocalTimestamp, string Message);

    public sealed record ExportResult(bool Exported, string Message);

    private static PlanerWorkspaceService Workspace => new(AppServices.SettingsSubfolder);

    public static async Task<ImportResult> TryImportIfRemoteNewerAsync(CancellationToken ct = default)
    {
        if (!AppServices.IsPlannerApp)
        {
            return new ImportResult(false, 0, 0, "Nur im Planer verfügbar.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new ImportResult(false, 0, 0, "Dropbox nicht verbunden.");
        }

        var localTimestamp = Workspace.GetLocalSavedAtUtcMs();

        string remoteJson;
        try
        {
            remoteJson = await AppServices.Dropbox
                .DownloadNamedFileAsync(DropboxConstants.PlanerWorkspaceFileName, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.Message.Contains("not_found", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("path/not_found", StringComparison.OrdinalIgnoreCase))
        {
            return new ImportResult(
                false,
                0,
                localTimestamp,
                "Kein Planer-Arbeitsstand in Dropbox – lokaler Stand beibehalten.");
        }
        catch (Exception ex)
        {
            return new ImportResult(false, 0, localTimestamp, $"Dropbox-Laden fehlgeschlagen: {ex.Message}");
        }

        var document = PlanerWorkspaceService.Parse(remoteJson);
        if (document is null)
        {
            return new ImportResult(false, 0, localTimestamp, "planer_workspace.json in Dropbox ist ungültig.");
        }

        var remoteTimestamp = document.SavedAtUtcMs;
        if (localTimestamp > 0 && remoteTimestamp <= localTimestamp)
        {
            var mergedVersions = PlanerWorkspaceService.MergePackageVersionSnapshots(document.PackageVersionSnapshots);
            var versionHint = mergedVersions > 0
                ? $" {mergedVersions} Version(en) aus Dropbox übernommen."
                : string.Empty;
            return new ImportResult(
                false,
                remoteTimestamp,
                localTimestamp,
                "Dropbox unverändert – lokaler Planer-Arbeitsstand beibehalten." + versionHint);
        }

        Workspace.Apply(document);
        return new ImportResult(
            true,
            remoteTimestamp,
            localTimestamp,
            $"Planer-Arbeitsstand aus Dropbox übernommen ({DropboxConstants.PlanerWorkspaceFileName}).");
    }

    public static async Task<ExportResult> TryExportAsync(CancellationToken ct = default)
    {
        if (!AppServices.IsPlannerApp)
        {
            return new ExportResult(false, "Nur im Planer verfügbar.");
        }

        try
        {
            var document = Workspace.CaptureCurrent();
            var json = PlanerWorkspaceService.Serialize(document);
            Workspace.WriteLocalCopy(document);
            PlanerWorkspaceSaveCoordinator.MarkPersisted();

            if (!AppServices.Dropbox.Settings.IsConnected)
            {
                return new ExportResult(false, "Dropbox nicht verbunden – lokal gespeichert.");
            }

            await AppServices.Dropbox
                .UploadNamedFileAsync(DropboxConstants.PlanerWorkspaceFileName, json, ct)
                .ConfigureAwait(false);
            return new ExportResult(
                true,
                $"Planer-Arbeitsstand in Dropbox gespeichert ({DropboxConstants.PlanerWorkspaceFileName}).");
        }
        catch (Exception ex)
        {
            return new ExportResult(false, $"Dropbox-Export fehlgeschlagen: {ex.Message}");
        }
    }
}
