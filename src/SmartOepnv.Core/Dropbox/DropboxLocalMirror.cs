using SmartOepnv.Core;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Liest Dateien aus dem bereits synchronisierten Dropbox-Ordner auf diesem PC,
/// statt sie noch einmal über die Dropbox-API herunterzuladen.
/// Nur vollständig lokale Dateien (kein „nur online“) und nur bei passender Größe
/// bzw. Dropbox-Prüfsumme.
/// </summary>
public static class DropboxLocalMirror
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static string? TryGetHydratedFile(string relativeUnixPath)
    {
        if (string.IsNullOrWhiteSpace(relativeUnixPath) ||
            relativeUnixPath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var folder = TryResolveFolder();
        if (folder is null)
        {
            return null;
        }

        var parts = relativeUnixPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var full = Path.GetFullPath(Path.Combine(new[] { folder }.Concat(parts).ToArray()));
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(full) || !IsHydrated(full))
        {
            return null;
        }

        return full;
    }

    public static async Task<string?> TryReadTextIfMatchesAsync(
        string relativeUnixPath,
        long expectedSize,
        string? contentHash,
        CancellationToken ct = default)
    {
        var path = TryGetHydratedFile(relativeUnixPath);
        if (path is null || !SizeMatches(path, expectedSize))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(contentHash) &&
            !string.Equals(DropboxContentHash.ComputeFile(path), contentHash, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool TryCopyIfSizeMatches(string relativeUnixPath, string destinationPath, long expectedSize)
    {
        var path = TryGetHydratedFile(relativeUnixPath);
        if (path is null || !SizeMatches(path, expectedSize))
        {
            return false;
        }

        var destinationFull = Path.GetFullPath(destinationPath);
        if (string.Equals(path, destinationFull, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var directory = Path.GetDirectoryName(destinationFull);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var destination = new FileStream(destinationFull, FileMode.Create, FileAccess.Write, FileShare.Read);
            source.CopyTo(destination);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SizeMatches(string path, long expectedSize) =>
        expectedSize > 0 && new FileInfo(path).Length == expectedSize;

    private static string? TryResolveFolder()
    {
        string? configured = null;
        try
        {
            configured = AppServices.Dropbox.Settings.FolderPath;
        }
        catch
        {
            // ohne geladene Einstellungen die üblichen Dropbox-Ordner prüfen
        }

        return DropboxSyncFolderLocator.TryResolveSmartOepnvFolder(configured);
    }

    private static bool IsHydrated(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Offline) != 0 ||
                (attributes & RecallOnOpen) != 0 ||
                (attributes & RecallOnDataAccess) != 0)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
