using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Planer-Versions-Snapshots (<c>planer_version_snapshots/</c>) getrennt von planer_workspace.json.
/// </summary>
public static class PlanerVersionSnapshotsDropboxSync
{
    public sealed record SyncResult(int Uploaded, int Downloaded, int Skipped, string Message);

    public static string GetDropboxFileRelativePath(string snapshotId) =>
        $"{DropboxConstants.PlanerVersionSnapshotsFolderName}/{Path.GetFileName(snapshotId)}.json";

    public static async Task<SyncResult> ExportChangedFilesAsync(
        IReadOnlyList<PlannerPackageVersionSnapshotData> snapshots,
        IProgress<DropboxTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new SyncResult(0, 0, 0, "Dropbox nicht verbunden.");
        }

        if (snapshots.Count == 0)
        {
            return new SyncResult(0, 0, 0, "Keine Versionen.");
        }

        var remoteSizes = await AppServices.Dropbox
            .ListRelativeFolderFileSizesAsync(DropboxConstants.PlanerVersionSnapshotsFolderName, ct)
            .ConfigureAwait(false);

        var toUpload = new List<(string Id, string Json, long Size)>();
        var skipped = 0;

        foreach (var snap in snapshots)
        {
            if (string.IsNullOrWhiteSpace(snap.Id))
            {
                continue;
            }

            var fileName = $"{Path.GetFileName(snap.Id)}.json";
            var size = snap.ByteSize;
            if (size <= 0 && !string.IsNullOrWhiteSpace(snap.PackageJson))
            {
                size = System.Text.Encoding.UTF8.GetByteCount(snap.PackageJson);
            }

            if (size > 0 &&
                remoteSizes.TryGetValue(fileName, out var remoteSize) &&
                remoteSize == size)
            {
                skipped++;
                continue;
            }

            var packageJson = snap.PackageJson;
            if (string.IsNullOrWhiteSpace(packageJson) && AppServices.PlannerVersions is not null)
            {
                packageJson = AppServices.PlannerVersions.TryLoadPackageJson(snap.Id);
            }

            if (string.IsNullOrWhiteSpace(packageJson))
            {
                continue;
            }

            if (size <= 0)
            {
                size = System.Text.Encoding.UTF8.GetByteCount(packageJson);
            }

            toUpload.Add((snap.Id, packageJson, size));
        }

        if (toUpload.Count == 0)
        {
            return new SyncResult(
                0,
                0,
                skipped,
                skipped > 0
                    ? $"{skipped} Version(en) in Dropbox bereits aktuell."
                    : "Keine Versionen zum Hochladen.");
        }

        var uploaded = 0;
        for (var i = 0; i < toUpload.Count; i++)
        {
            var (id, json, _) = toUpload[i];
            var fileName = Path.GetFileName(id) + ".json";
            var tempPath = Path.Combine(Path.GetTempPath(), $"planer-snap-{fileName}");
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            try
            {
                var fileProgress = ScaleProgress(
                    progress,
                    $"Version {i + 1}/{toUpload.Count}: {fileName}",
                    i,
                    toUpload.Count);
                await AppServices.Dropbox.UploadRelativeFileFromPathAsync(
                        GetDropboxFileRelativePath(id),
                        tempPath,
                        ct,
                        fileProgress)
                    .ConfigureAwait(false);
                uploaded++;
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        return new SyncResult(
            uploaded,
            0,
            skipped,
            $"{uploaded} Version(en) hochgeladen, {skipped} unverändert.");
    }

    public static async Task<SyncResult> ImportMissingFilesAsync(
        IList<PlannerPackageVersionSnapshotData> snapshots,
        IProgress<DropboxTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (snapshots.Count == 0)
        {
            return new SyncResult(0, 0, 0, "Keine Versionen im Arbeitsstand.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new SyncResult(0, 0, 0, "Dropbox nicht verbunden.");
        }

        var toDownload = new List<(int Index, string Id)>();
        var skipped = 0;

        for (var i = 0; i < snapshots.Count; i++)
        {
            var snap = snapshots[i];
            if (string.IsNullOrWhiteSpace(snap.Id))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(snap.PackageJson))
            {
                skipped++;
                continue;
            }

            if (snap.ByteSize <= 0)
            {
                continue;
            }

            toDownload.Add((i, snap.Id));
        }

        if (toDownload.Count == 0)
        {
            return new SyncResult(
                0,
                0,
                skipped,
                skipped > 0
                    ? $"{skipped} Version(en) bereits im Arbeitsstand enthalten."
                    : "Keine externen Versionen.");
        }

        var downloaded = 0;
        for (var i = 0; i < toDownload.Count; i++)
        {
            var (index, id) = toDownload[i];
            var fileName = Path.GetFileName(id) + ".json";
            var tempPath = Path.Combine(Path.GetTempPath(), $"planer-snap-dl-{fileName}");
            try
            {
                var fileProgress = ScaleProgress(
                    progress,
                    $"Version {i + 1}/{toDownload.Count}: {fileName}",
                    i,
                    toDownload.Count);
                await AppServices.Dropbox.DownloadRelativeFileToPathAsync(
                        GetDropboxFileRelativePath(id),
                        tempPath,
                        ct,
                        fileProgress)
                    .ConfigureAwait(false);
                snapshots[index].PackageJson = await File.ReadAllTextAsync(tempPath, ct).ConfigureAwait(false);
                downloaded++;
            }
            catch (Exception ex) when (ex.Message.Contains("not_found", StringComparison.OrdinalIgnoreCase))
            {
                // Einzelne fehlende Datei überspringen
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        return new SyncResult(
            0,
            downloaded,
            skipped,
            downloaded > 0
                ? $"{downloaded} Version(en) aus Dropbox geladen."
                : "Keine Versionen aus Dropbox geladen.");
    }

    private static IProgress<DropboxTransferProgress>? ScaleProgress(
        IProgress<DropboxTransferProgress>? progress,
        string phase,
        int completed,
        int total)
    {
        if (progress is null || total <= 0)
        {
            return null;
        }

        var startPercent = completed * 100.0 / total;
        var endPercent = (completed + 1) * 100.0 / total;
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

    private static void TryDelete(string path)
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
