namespace SmartOepnv.Core.Updates;

public static class DesktopSoftwareUpdateDownloader
{
    public static async Task<string> DownloadAsync(SoftwareUpdateNotice notice, CancellationToken ct = default)
    {
        if (!AppServices.IsInitialized || !AppServices.Dropbox.Settings.IsConnected)
        {
            throw new InvalidOperationException("Dropbox nicht verbunden.");
        }

        if (string.IsNullOrWhiteSpace(notice.SetupFileName))
        {
            throw new InvalidOperationException("Keine Setup-Datei in software_versions.json angegeben.");
        }

        var fileName = Path.GetFileName(notice.SetupFileName.Trim());
        var bytes = await AppServices.Dropbox.TryDownloadNamedBinaryFileAsync(fileName, ct).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Setup-Datei „{fileName}“ nicht in Dropbox gefunden.");
        }

        var targetDir = ResolveDownloadDirectory();
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, fileName);

        if (File.Exists(targetPath))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            targetPath = Path.Combine(targetDir, $"{baseName}_{stamp}{extension}");
        }

        await File.WriteAllBytesAsync(targetPath, bytes, ct).ConfigureAwait(false);
        return targetPath;
    }

    private static string ResolveDownloadDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(userProfile, "Downloads");
        if (Directory.Exists(downloads))
        {
            return downloads;
        }

        return AppPaths.GetLocalDataDirectory(AppServices.SettingsSubfolder);
    }
}
