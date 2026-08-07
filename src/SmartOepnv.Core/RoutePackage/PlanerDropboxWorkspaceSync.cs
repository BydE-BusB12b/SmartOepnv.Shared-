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
        var localSyncGeneration = Workspace.GetLocalSyncGeneration();
        var workspacePath = Workspace.LocalFilePath;

        DropboxNamedFileMetadata? remoteMeta = null;
        try
        {
            remoteMeta = await AppServices.Dropbox
                .TryGetNamedFileMetadataAsync(DropboxConstants.PlanerWorkspaceFileName, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ImportResult(false, 0, localTimestamp, $"Dropbox-Metadaten fehlgeschlagen: {ex.Message}");
        }

        if (remoteMeta is null)
        {
            return new ImportResult(
                false,
                0,
                localTimestamp,
                "Kein Planer-Arbeitsstand in Dropbox – lokaler Stand beibehalten.");
        }

        var stamp = PlanerWorkspaceDropboxSyncStamp.TryLoad(workspacePath);
        if (!forceOverwrite &&
            File.Exists(workspacePath) &&
            PlanerWorkspaceDropboxSyncStamp.MatchesRemote(stamp, remoteMeta.Value, localSyncGeneration) &&
            Workspace.TryApplyLocalDocument())
        {
            IReadOnlyDictionary<string, PlanerWorkspaceBinaryPayload>? soundsManifest = null;
            if (AppServices.IsInitialized)
            {
                soundsManifest = PlanerAnnouncementRawSoundsWorkspace.CaptureForSync(AppServices.Workspace);
            }

            var soundsProgress = ScaleProgress(
                progress,
                "Fehlende Ansagen-Rohdateien werden geprüft…",
                50,
                100);
            var soundsResult = await PlanerAnnouncementRawSoundsDropboxSync
                .ImportMissingFilesAsync(soundsManifest, soundsProgress, ct)
                .ConfigureAwait(false);
            ReportOverall(progress, "Lokaler Planer-Arbeitsstand übernommen.", 100);
            var soundsHint = soundsResult.Downloaded > 0
                ? $" {soundsResult.Downloaded} Ansagen-Rohdatei(en) nachgeladen."
                : string.Empty;
            var genHint = localSyncGeneration > 0
                ? $" Stand #{localSyncGeneration}."
                : string.Empty;
            return new ImportResult(
                true,
                stamp?.LocalSavedAtUtcMs ?? localTimestamp,
                localTimestamp,
                "Lokal aktuell – kein Dropbox-Download (planer_workspace.json unverändert)." +
                genHint +
                soundsHint);
        }

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
        var localGeneration = localDocument?.SyncGeneration ?? 0;
        var remoteGeneration = document.SyncGeneration;
        var localIsNewerByGeneration = localGeneration > 0 &&
                                       remoteGeneration > 0 &&
                                       localGeneration > remoteGeneration;
        var remoteIsNewerByGeneration = remoteGeneration > 0 &&
                                        localGeneration > 0 &&
                                        remoteGeneration > localGeneration;
        var localIsNewerThanRemote = (localTimestamp > 0 && remoteTimestamp < localTimestamp) ||
                                     localIsNewerByGeneration;
        var preferRemote = remoteIsNewerByGeneration ||
                           (!localIsNewerThanRemote &&
                            (ShouldPreferRemoteDespiteLocalTimestamp(document, localDocument) || remoteHasMoreContent));

        if (!forceOverwrite &&
            localTimestamp > 0 &&
            remoteTimestamp <= localTimestamp &&
            !preferRemote)
        {
            // Stempel speichern, damit der nächste Start den großen Download überspringt.
            PlanerWorkspaceDropboxSyncStamp.Save(
                workspacePath,
                remoteMeta.Value,
                localTimestamp,
                localDocument?.SyncGeneration ?? document.SyncGeneration);
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

        await PlanerWorkspaceService.EnrichFromDropboxSidecarsAsync(
                document,
                ScaleProgress(progress, "Routen & Versionen werden geladen…", 88, 92),
                ct)
            .ConfigureAwait(false);

        Workspace.Apply(document, authoritative: true);

        var soundsProgressAfterDownload = ScaleProgress(
            progress,
            "Ansagen-Rohdateien werden geladen…",
            93,
            100);
        var soundsResultAfterDownload = await PlanerAnnouncementRawSoundsDropboxSync
            .ImportMissingFilesAsync(document.AnnouncementRawSounds, soundsProgressAfterDownload, ct)
            .ConfigureAwait(false);

        PlanerWorkspaceDropboxSyncStamp.Save(
            workspacePath,
            remoteMeta.Value,
            document.SavedAtUtcMs,
            document.SyncGeneration);

        ReportOverall(progress, "Planer-Arbeitsstand übernommen.", 100);
        var soundsHintAfterDownload = soundsResultAfterDownload.Downloaded > 0
            ? $" {soundsResultAfterDownload.Downloaded} Ansagen-Rohdatei(en) aus {DropboxConstants.PlanerAnnouncementRawSoundsFolderName}/ geladen."
            : string.Empty;
        return new ImportResult(
            true,
            remoteTimestamp,
            localTimestamp,
            (forceOverwrite
                ? $"Planer-Arbeitsstand aus Dropbox geladen ({DropboxConstants.PlanerWorkspaceFileName}, lokaler Stand überschrieben)."
                : $"Planer-Arbeitsstand aus Dropbox übernommen ({DropboxConstants.PlanerWorkspaceFileName}).") + soundsHintAfterDownload,
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

            // Kein volles Einlesen der lokalen Workspace-Datei (kann GB groß sein).
            var captureRequest = new PlanerWorkspaceCaptureRequest
            {
                SkipFlush = !flushBeforeCapture,
                PreferCachedRoutesJson = !flushBeforeCapture
            };

            var document = Workspace.CaptureCurrent(captureRequest);
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

            var slimTempPath = Workspace.WriteDropboxSlimCopyToTemp(document);
            try
            {
                var workspaceFileInfo = new FileInfo(slimTempPath);
                var sizeKb = Math.Max(1, (int)(workspaceFileInfo.Exists ? workspaceFileInfo.Length / 1024 : 1));
                var workspaceProgress = ScaleProgress(
                    progress,
                    $"{DropboxConstants.PlanerWorkspaceFileName} wird hochgeladen…",
                    10,
                    leitstelleJson is null ? 35 : 30);
                await AppServices.Dropbox.UploadNamedFileFromPathAsync(
                    DropboxConstants.PlanerWorkspaceFileName,
                    slimTempPath,
                    ct,
                    workspaceProgress).ConfigureAwait(false);

                var routesProgress = ScaleProgress(
                    progress,
                    $"{DropboxConstants.PlanerRoutesFileName}…",
                    leitstelleJson is null ? 35 : 30,
                    leitstelleJson is null ? 50 : 45);
                var routesResult = await PlanerRoutesDropboxSync
                    .ExportIfChangedAsync(document.RoutesPackageJson, routesProgress, ct)
                    .ConfigureAwait(false);

                var versionsProgress = ScaleProgress(
                    progress,
                    $"{DropboxConstants.PlanerVersionSnapshotsFolderName}…",
                    leitstelleJson is null ? 50 : 45,
                    leitstelleJson is null ? 65 : 60);
                var versionsResult = await PlanerVersionSnapshotsDropboxSync
                    .ExportChangedFilesAsync(document.PackageVersionSnapshots, versionsProgress, ct)
                    .ConfigureAwait(false);

                var soundsProgress = ScaleProgress(
                    progress,
                    $"Ansagen ({DropboxConstants.PlanerAnnouncementRawSoundsFolderName})…",
                    leitstelleJson is null ? 65 : 60,
                    leitstelleJson is null ? 100 : 80);
                var soundsResult = await PlanerAnnouncementRawSoundsDropboxSync
                    .ExportChangedFilesAsync(document.AnnouncementRawSounds, soundsProgress, ct)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(leitstelleJson))
                {
                    await RememberRemoteWorkspaceStampAsync(document.SavedAtUtcMs, document.SyncGeneration, ct)
                        .ConfigureAwait(false);
                    PlanerWorkspaceSaveCoordinator.MarkDropboxExported();
                    ReportOverall(progress, "Upload abgeschlossen.", 100);
                    return new ExportResult(
                        true,
                        $"Planer-Arbeitsstand in Dropbox gespeichert ({DropboxConstants.PlanerWorkspaceFileName}, {sizeKb} KB). " +
                        $"{routesResult.Message} {versionsResult.Message} {soundsResult.Message}",
                        LocalSaved: true);
                }

                if (!RoutePackageRosterPreserve.JsonContainsRosterData(leitstelleJson))
                {
                    await RememberRemoteWorkspaceStampAsync(document.SavedAtUtcMs, document.SyncGeneration, ct)
                        .ConfigureAwait(false);
                    PlanerWorkspaceSaveCoordinator.MarkDropboxExported();
                    ReportOverall(progress, "Upload abgeschlossen.", 100);
                    return new ExportResult(
                        true,
                        $"Planer-Arbeitsstand in Dropbox gespeichert ({DropboxConstants.PlanerWorkspaceFileName}, {sizeKb} KB). " +
                        $"{routesResult.Message} {versionsResult.Message} {soundsResult.Message} " +
                        $"{DropboxConstants.LeitstelleStandFileName} unverändert (kein Personal/Fahrzeuge im Editor).",
                        LocalSaved: true);
                }

                var leitstelleProgress = ScaleProgress(
                    progress,
                    $"{DropboxConstants.LeitstelleStandFileName} wird hochgeladen…",
                    80,
                    100);
                await AppServices.Dropbox.UploadLeitstelleStandAsync(leitstelleJson, ct, leitstelleProgress)
                    .ConfigureAwait(false);
                await RememberRemoteWorkspaceStampAsync(document.SavedAtUtcMs, document.SyncGeneration, ct)
                    .ConfigureAwait(false);
                PlanerWorkspaceSaveCoordinator.MarkDropboxExported();
                ReportOverall(progress, "Upload abgeschlossen.", 100);
                return new ExportResult(
                    true,
                    $"Planer-Arbeitsstand in Dropbox gespeichert ({DropboxConstants.PlanerWorkspaceFileName}, {sizeKb} KB). " +
                    $"{routesResult.Message} {versionsResult.Message} {soundsResult.Message} " +
                    $"{DropboxConstants.LeitstelleStandFileName} aktualisiert (ohne {DropboxConstants.RouteUpdateFileName}).",
                    LocalSaved: true);
            }
            finally
            {
                TryDeleteTempFile(slimTempPath);
            }
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

    private static async Task RememberRemoteWorkspaceStampAsync(
        long localSavedAtUtcMs,
        long syncGeneration,
        CancellationToken ct)
    {
        try
        {
            var meta = await AppServices.Dropbox
                .TryGetNamedFileMetadataAsync(DropboxConstants.PlanerWorkspaceFileName, ct)
                .ConfigureAwait(false);
            if (meta is null)
            {
                return;
            }

            PlanerWorkspaceDropboxSyncStamp.Save(
                Workspace.LocalFilePath,
                meta.Value,
                localSavedAtUtcMs,
                syncGeneration);
        }
        catch
        {
            // Stempel ist nur Optimierung.
        }
    }

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

        if (!local.RoutesStoredExternally && remote.RoutesStoredExternally &&
            string.IsNullOrWhiteSpace(local.RoutesPackageJson))
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

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }
}
