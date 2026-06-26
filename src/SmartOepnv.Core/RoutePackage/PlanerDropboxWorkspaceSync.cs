using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Planer: planer_workspace.json und leitstelle_stand.json mit Dropbox abgleichen (Anmeldung laden, Beenden speichern).
/// routes_export.json bleibt unberührt – die wird nur manuell für die Apps exportiert.
/// </summary>
public static class PlanerDropboxWorkspaceSync
{
    public sealed record ImportResult(
        bool Imported,
        long RemoteTimestamp,
        long LocalTimestamp,
        string Message,
        bool RemoteHasMoreContent = false);

    public sealed record ExportResult(bool Exported, string Message, bool LocalSaved = false);

    private static PlanerWorkspaceService Workspace => new(AppServices.SettingsSubfolder);

    public static async Task<ImportResult> TryImportIfRemoteNewerAsync(
        IProgress<DropboxTransferProgress>? progress = null,
        CancellationToken ct = default) =>
        await TryImportFromDropboxAsync(forceOverwrite: false, progress, ct).ConfigureAwait(false);

    public static async Task<ImportResult> TryImportFromDropboxAsync(
        bool forceOverwrite = false,
        IProgress<DropboxTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!AppServices.IsPlannerApp)
        {
            return new ImportResult(false, 0, 0, "Nur im Planer verfügbar.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new ImportResult(false, 0, 0, "Dropbox nicht verbunden.");
        }

        ReportOverall(progress, "Planer-Arbeitsstand wird geprüft…", 0);

        var localTimestamp = Workspace.GetLocalSavedAtUtcMs();
        var localDocument = Workspace.TryReadLocalDocument();

        string remoteJson;
        try
        {
            var downloadProgress = ScaleProgress(
                progress,
                $"{DropboxConstants.PlanerWorkspaceFileName} wird geladen…",
                5,
                90);
            remoteJson = await AppServices.Dropbox
                .DownloadNamedFileAsync(DropboxConstants.PlanerWorkspaceFileName, ct, downloadProgress)
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

        ReportOverall(progress, "Planer-Arbeitsstand wird übernommen…", 92);

        var document = PlanerWorkspaceService.Parse(remoteJson);
        if (document is null)
        {
            return new ImportResult(false, 0, localTimestamp, "planer_workspace.json in Dropbox ist ungültig.");
        }

        var remoteTimestamp = document.SavedAtUtcMs;
        var remoteHasMoreContent = PlanerWorkspaceContentCompare.RemoteHasMoreContentThanLocal(document, localDocument);
        var localIsNewerThanRemote = localTimestamp > 0 && remoteTimestamp < localTimestamp;
        var preferRemote = !localIsNewerThanRemote &&
                           (ShouldPreferRemoteDespiteLocalTimestamp(document, localDocument) || remoteHasMoreContent);

        if (!forceOverwrite &&
            localTimestamp > 0 &&
            remoteTimestamp <= localTimestamp &&
            !preferRemote)
        {
            var mergedVersions = PlanerWorkspaceService.MergePackageVersionSnapshots(document.PackageVersionSnapshots);
            var versionHint = mergedVersions > 0
                ? $" {mergedVersions} Version(en) aus Dropbox übernommen."
                : string.Empty;
            ReportOverall(progress, "Lokaler Stand ist aktueller.", 100);
            return new ImportResult(
                false,
                remoteTimestamp,
                localTimestamp,
                "Dropbox unverändert – lokaler Planer-Arbeitsstand ist neuer oder gleich alt." + versionHint,
                RemoteHasMoreContent: remoteHasMoreContent);
        }

        Workspace.Apply(document, authoritative: true);
        ReportOverall(progress, "Planer-Arbeitsstand übernommen.", 100);
        return new ImportResult(
            true,
            remoteTimestamp,
            localTimestamp,
            forceOverwrite
                ? $"Planer-Arbeitsstand aus Dropbox geladen ({DropboxConstants.PlanerWorkspaceFileName}, lokaler Stand überschrieben)."
                : $"Planer-Arbeitsstand aus Dropbox übernommen ({DropboxConstants.PlanerWorkspaceFileName}).",
            RemoteHasMoreContent: remoteHasMoreContent);
    }

    public static async Task<ExportResult> TryExportAsync(
        CancellationToken ct = default,
        bool flushBeforeCapture = true,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        if (!AppServices.IsPlannerApp)
        {
            return new ExportResult(false, "Nur im Planer verfügbar.");
        }

        try
        {
            ReportOverall(progress, "Arbeitsstand wird vorbereitet…", 2);

            var existingDocument = Workspace.TryReadLocalDocument();
            var captureRequest = new PlanerWorkspaceCaptureRequest
            {
                SkipFlush = !flushBeforeCapture,
                PreferCachedRoutesJson = !flushBeforeCapture,
                ReuseSnapshotPackageJsonFrom = existingDocument?.PackageVersionSnapshots
            };

            var document = Workspace.CaptureCurrent(captureRequest);
            var json = PlanerWorkspaceService.Serialize(document);
            Workspace.WriteLocalCopy(document);
            PlanerWorkspaceSaveCoordinator.MarkLocalSaved();

            ReportOverall(progress, "Lokal gespeichert – Upload startet…", 8);

            if (!AppServices.Dropbox.Settings.IsConnected)
            {
                return new ExportResult(false, "Dropbox nicht verbunden – lokal gespeichert.", LocalSaved: true);
            }

            string? leitstelleJson = null;
            if (AppServices.Routes.HasPackage && AppServices.Routes.Editor is not null)
            {
                leitstelleJson = AppServices.Routes.BuildLeitstelleStandJson();
            }

            var sizeKb = Math.Max(1, json.Length / 1024);
            var workspaceProgress = ScaleProgress(
                progress,
                $"{DropboxConstants.PlanerWorkspaceFileName} wird hochgeladen…",
                10,
                leitstelleJson is null ? 100 : 85);
            await AppServices.Dropbox.UploadNamedFileAsync(
                DropboxConstants.PlanerWorkspaceFileName,
                json,
                ct,
                workspaceProgress).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(leitstelleJson))
            {
                PlanerWorkspaceSaveCoordinator.MarkDropboxExported();
                ReportOverall(progress, "Upload abgeschlossen.", 100);
                return new ExportResult(
                    true,
                    $"Planer-Arbeitsstand in Dropbox gespeichert ({DropboxConstants.PlanerWorkspaceFileName}, {sizeKb} KB).",
                    LocalSaved: true);
            }

            var leitstelleProgress = ScaleProgress(
                progress,
                $"{DropboxConstants.LeitstelleStandFileName} wird hochgeladen…",
                85,
                100);
            await AppServices.Dropbox.UploadLeitstelleStandAsync(leitstelleJson, ct, leitstelleProgress)
                .ConfigureAwait(false);
            PlanerWorkspaceSaveCoordinator.MarkDropboxExported();
            ReportOverall(progress, "Upload abgeschlossen.", 100);
            return new ExportResult(
                true,
                $"Planer-Arbeitsstand in Dropbox gespeichert ({DropboxConstants.PlanerWorkspaceFileName}, {sizeKb} KB). " +
                $"{DropboxConstants.LeitstelleStandFileName} aktualisiert.",
                LocalSaved: true);
        }
        catch (Exception ex)
        {
            var localSaved = PlanerWorkspaceSaveCoordinator.WasLocalSavedRecently();
            return new ExportResult(
                false,
                localSaved
                    ? $"Dropbox-Export fehlgeschlagen (lokal gespeichert): {ex.Message}"
                    : $"Dropbox-Export fehlgeschlagen: {ex.Message}",
                LocalSaved: localSaved);
        }
    }

    public static bool TryApplyLocalWorkspace() => Workspace.TryApplyLocalDocument();

    private static bool ShouldPreferRemoteDespiteLocalTimestamp(
        PlanerWorkspaceDocument remote,
        PlanerWorkspaceDocument? local)
    {
        if (local is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(local.RoutesPackageJson) &&
            !string.IsNullOrWhiteSpace(remote.RoutesPackageJson))
        {
            return true;
        }

        if (!local.PlannerOverlay.HasContent && remote.PlannerOverlay.HasContent)
        {
            return true;
        }

        if ((local.DutyTemplates?.Count ?? 0) == 0 && (remote.DutyTemplates?.Count ?? 0) > 0)
        {
            return true;
        }

        return PlanerWorkspaceContentCompare.RemoteHasMoreContentThanLocal(remote, local);
    }

    private static void ReportOverall(IProgress<DropboxTransferProgress>? progress, string phase, double percent)
    {
        progress?.Report(new DropboxTransferProgress
        {
            Phase = phase,
            BytesTransferred = (long)Math.Round(percent),
            TotalBytes = 100
        });
    }

    private static IProgress<DropboxTransferProgress>? ScaleProgress(
        IProgress<DropboxTransferProgress>? progress,
        string phase,
        double startPercent,
        double endPercent)
    {
        if (progress is null)
        {
            return null;
        }

        var range = endPercent - startPercent;
        return new Progress<DropboxTransferProgress>(p =>
        {
            var overall = startPercent + p.Percent * range / 100.0;
            progress.Report(new DropboxTransferProgress
            {
                Phase = phase,
                BytesTransferred = (long)Math.Round(overall),
                TotalBytes = 100,
                EstimatedSecondsRemaining = p.EstimatedSecondsRemaining
            });
        });
    }
}
