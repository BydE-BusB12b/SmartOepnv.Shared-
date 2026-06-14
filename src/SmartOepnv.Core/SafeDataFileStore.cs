using System.IO;

namespace SmartOepnv.Core;

/// <summary>
/// Atomisches Schreiben mit Versions-Backup im Unterordner <c>backups</c>.
/// </summary>
public static class SafeDataFileStore
{
    public const int DefaultMaxBackups = 50;

    public static void WriteAllText(string filePath, string content, int maxBackups = DefaultMaxBackups, bool archivePrevious = true)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException($"Kein Zielverzeichnis für {filePath}");
        }

        Directory.CreateDirectory(directory);

        if (archivePrevious && File.Exists(filePath))
        {
            ArchiveExistingFile(filePath, maxBackups);
        }

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, content);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }
    }

    public static void ArchiveExistingFile(string filePath, int maxBackups = DefaultMaxBackups)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var backupDir = Path.Combine(directory, "backups");
        Directory.CreateDirectory(backupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        var backupPath = Path.Combine(
            backupDir,
            $"{Path.GetFileNameWithoutExtension(filePath)}_{stamp}{Path.GetExtension(filePath)}");
        if (File.Exists(backupPath))
        {
            backupPath = Path.Combine(
                backupDir,
                $"{Path.GetFileNameWithoutExtension(filePath)}_{stamp}_{Guid.NewGuid():N}{Path.GetExtension(filePath)}");
        }

        File.Copy(filePath, backupPath, overwrite: true);
        PruneOldBackups(backupDir, Path.GetFileName(filePath), maxBackups);
    }

    public static void CopyDirectorySnapshot(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            if (IsUnderBackupsFolder(relative))
            {
                continue;
            }

            var target = Path.Combine(destinationDirectory, relative);
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsUnderBackupsFolder(string relativePath)
    {
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("backups", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void PruneOldBackups(string backupDir, string originalFileName, int maxBackups)
    {
        var prefix = Path.GetFileNameWithoutExtension(originalFileName) + "_";
        var ext = Path.GetExtension(originalFileName);
        foreach (var old in Directory.GetFiles(backupDir, prefix + "*" + ext)
                     .OrderByDescending(static f => f, StringComparer.Ordinal)
                     .Skip(maxBackups))
        {
            try
            {
                File.Delete(old);
            }
            catch
            {
                // ignore prune errors
            }
        }
    }
}
