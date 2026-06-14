using System.IO;

namespace SmartOepnv.Core;

/// <summary>
/// Sicherungskopien des Smart-OEPNV-Benutzerdatenordners (Planer/Leitstelle).
/// </summary>
public static class SmartOepnvDataBackupService
{
    private const int MinAutomaticBackupIntervalMs = 600_000;
    private static long _lastBackupTickMs;

    public static string GetProjectBackupRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AndroidStudioProjects",
            "_Backups",
            "Smart-OEPNV");

    public static string BackupAppData(string appSubfolder, string reason = "manual")
    {
        var source = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        if (!Directory.Exists(source))
        {
            return string.Empty;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.Combine(
            GetProjectBackupRoot(),
            "AppData",
            appSubfolder,
            $"{reason}_{stamp}");
        SafeDataFileStore.CopyDirectorySnapshot(source, destination);
        return destination;
    }

    public static void BackupAllProfiles(string reason = "manual", bool force = false)
    {
        if (!force &&
            !string.Equals(reason, "manual", StringComparison.OrdinalIgnoreCase) &&
            _lastBackupTickMs > 0 &&
            Environment.TickCount64 - _lastBackupTickMs < MinAutomaticBackupIntervalMs)
        {
            return;
        }

        BackupAppData("Planer", reason);
        BackupAppData("Leitstelle", reason);
        _lastBackupTickMs = Environment.TickCount64;
    }
}
