using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Synchronisiert Ansagen-Rohdateien (<c>ansagen_roh</c> lokal, <c>planer_ansagen_roh/</c> in Dropbox).
/// Unabhängig von <c>routes_export.json</c> – betrifft nur Planer-zu-Planer-Sync.
/// </summary>
public static class PlanerAnnouncementRawSoundsDropboxSync
{
    public sealed record SyncResult(int Uploaded, int Downloaded, int Skipped, string Message);

    public static string GetDropboxFolderRelativePath() =>
        DropboxConstants.PlanerAnnouncementRawSoundsFolderName;

    public static string GetDropboxFileRelativePath(string fileName) =>
        $"{DropboxConstants.PlanerAnnouncementRawSoundsFolderName}/{fileName}";

    public static async Task<SyncResult> ExportChangedFilesAsync(
        IReadOnlyDictionary<string, PlanerWorkspaceBinaryPayload> manifest,
        IProgress<DropboxTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new SyncResult(0, 0, 0, "Dropbox nicht verbunden.");
        }

        if (!AppServices.IsInitialized)
        {
            return new SyncResult(0, 0, 0, "Planer nicht initialisiert.");
        }

        var workspace = AppServices.Workspace;
        var remoteSizes = await AppServices.Dropbox
            .ListRelativeFolderFileSizesAsync(GetDropboxFolderRelativePath(), ct)
            .ConfigureAwait(false);

        var files = PlanerAnnouncementRawSoundsWorkspace.ListAudioFiles(workspace);
        var toUpload = new List<(string FileName, string LocalPath, long Size)>();
        var skipped = 0;

        foreach (var localPath in files)
        {
            var fileName = Path.GetFileName(localPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            if (!manifest.TryGetValue(fileName, out var entry) || entry.Size <= 0)
            {
                continue;
            }

            if (remoteSizes.TryGetValue(fileName, out var remoteSize) && remoteSize == entry.Size)
            {
                skipped++;
                continue;
            }

            toUpload.Add((fileName, localPath, entry.Size));
        }

        if (toUpload.Count == 0)
        {
            return new SyncResult(0, 0, skipped, $"{skipped} Ansagen-Rohdatei(en) in Dropbox bereits aktuell.");
        }

        var uploaded = 0;
        for (var i = 0; i < toUpload.Count; i++)
        {
            var (fileName, localPath, _) = toUpload[i];
            var fileProgress = ScaleProgress(
                progress,
                $"Ansage {i + 1}/{toUpload.Count}: {fileName}",
                i,
                toUpload.Count);
            await AppServices.Dropbox.UploadRelativeFileFromPathAsync(
                    GetDropboxFileRelativePath(fileName),
                    localPath,
                    ct,
                    fileProgress)
                .ConfigureAwait(false);
            uploaded++;
        }

        return new SyncResult(
            uploaded,
            0,
            skipped,
            $"{uploaded} Ansagen-Rohdatei(en) hochgeladen, {skipped} unverändert.");
    }

    public static async Task<SyncResult> ImportMissingFilesAsync(
        IReadOnlyDictionary<string, PlanerWorkspaceBinaryPayload>? manifest,
        IProgress<DropboxTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (manifest is null || manifest.Count == 0)
        {
            return new SyncResult(0, 0, 0, "Keine Ansagen im Arbeitsstand.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new SyncResult(0, 0, 0, "Dropbox nicht verbunden.");
        }

        if (!AppServices.IsInitialized)
        {
            return new SyncResult(0, 0, 0, "Planer nicht initialisiert.");
        }

        var workspace = AppServices.Workspace;
        var dir = PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(workspace);
        var toDownload = new List<(string FileName, PlanerWorkspaceBinaryPayload Entry)>();
        var skipped = 0;

        foreach (var (fileName, entry) in manifest)
        {
            if (string.IsNullOrWhiteSpace(fileName) || entry.Size <= 0)
            {
                continue;
            }

            if (!EmbeddedSoundCatalog.IsAudioFile(fileName))
            {
                continue;
            }

            var localPath = Path.Combine(dir, fileName.Trim());
            if (PlanerAnnouncementRawSoundsWorkspace.LocalFileMatchesManifest(localPath, entry))
            {
                skipped++;
                continue;
            }

            if (!entry.IsExternalReference && !string.IsNullOrWhiteSpace(entry.Data))
            {
                skipped++;
                continue;
            }

            toDownload.Add((fileName.Trim(), entry));
        }

        if (toDownload.Count == 0)
        {
            return new SyncResult(0, 0, skipped, $"{skipped} Ansagen-Rohdatei(en) lokal bereits vollständig.");
        }

        var downloaded = 0;
        for (var i = 0; i < toDownload.Count; i++)
        {
            var (fileName, _) = toDownload[i];
            var localPath = Path.Combine(dir, fileName);
            var fileProgress = ScaleProgress(
                progress,
                $"Ansage {i + 1}/{toDownload.Count}: {fileName}",
                i,
                toDownload.Count);
            await AppServices.Dropbox.DownloadRelativeFileToPathAsync(
                    GetDropboxFileRelativePath(fileName),
                    localPath,
                    ct,
                    fileProgress)
                .ConfigureAwait(false);
            downloaded++;
        }

        return new SyncResult(
            0,
            downloaded,
            skipped,
            $"{downloaded} Ansagen-Rohdatei(en) aus Dropbox geladen, {skipped} lokal bereits vorhanden.");
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
}
