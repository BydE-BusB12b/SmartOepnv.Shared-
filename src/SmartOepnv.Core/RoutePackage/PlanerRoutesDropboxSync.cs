using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Routen-Paket (<c>planer_routes.json</c>) getrennt von <c>planer_workspace.json</c> synchronisieren.
/// </summary>
public static class PlanerRoutesDropboxSync
{
    public sealed record SyncResult(bool Uploaded, bool Skipped, string Message);

    public static async Task<SyncResult> ExportIfChangedAsync(
        string? routesPackageJson,
        IProgress<DropboxTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(routesPackageJson))
        {
            return new SyncResult(false, true, "Kein Routen-Paket.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new SyncResult(false, true, "Dropbox nicht verbunden.");
        }

        var workspaceDir = Path.Combine(
            AppPaths.GetRoamingDataDirectory(AppServices.SettingsSubfolder),
            "workspace");
        Directory.CreateDirectory(workspaceDir);
        var tempPath = Path.Combine(workspaceDir, DropboxConstants.PlanerRoutesFileName + ".upload.tmp");
        await File.WriteAllTextAsync(tempPath, routesPackageJson, ct).ConfigureAwait(false);

        try
        {
            var localSize = new FileInfo(tempPath).Length;
            var remoteMeta = await AppServices.Dropbox
                .TryGetNamedFileMetadataAsync(DropboxConstants.PlanerRoutesFileName, ct)
                .ConfigureAwait(false);

            if (remoteMeta?.SizeBytes == localSize)
            {
                return new SyncResult(
                    false,
                    true,
                    $"{DropboxConstants.PlanerRoutesFileName} in Dropbox bereits aktuell ({localSize / 1024} KB).");
            }

            await AppServices.Dropbox.UploadNamedFileFromPathAsync(
                    DropboxConstants.PlanerRoutesFileName,
                    tempPath,
                    ct,
                    progress)
                .ConfigureAwait(false);

            return new SyncResult(
                true,
                false,
                $"{DropboxConstants.PlanerRoutesFileName} hochgeladen ({localSize / 1024} KB).");
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static async Task<string?> TryDownloadRoutesJsonAsync(CancellationToken ct = default)
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return null;
        }

        try
        {
            var local = DropboxLocalMirror.TryGetHydratedFile(DropboxConstants.PlanerRoutesFileName);
            if (local is not null)
            {
                var meta = await AppServices.Dropbox
                    .TryGetNamedFileMetadataAsync(DropboxConstants.PlanerRoutesFileName, ct)
                    .ConfigureAwait(false);
                if (meta is { SizeBytes: > 0 })
                {
                    var mirrored = await DropboxLocalMirror.TryReadTextIfMatchesAsync(
                            DropboxConstants.PlanerRoutesFileName,
                            meta.Value.SizeBytes,
                            meta.Value.ContentHash,
                            ct)
                        .ConfigureAwait(false);
                    if (mirrored is not null)
                    {
                        return mirrored;
                    }
                }
            }

            return await AppServices.Dropbox
                .DownloadNamedFileAsync(DropboxConstants.PlanerRoutesFileName, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.Message.Contains("not_found", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
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
