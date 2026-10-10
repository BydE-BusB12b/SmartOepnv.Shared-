namespace SmartOepnv.Core.RoutePackage;

using System.Security.Cryptography;

/// <summary>
/// Roh-Tondateien für die Ansagen-Kartei (vor Zusammenfügen / embeddedSounds).
/// Liegt neben <c>routes_export.json</c> unter <c>ansagen_roh</c>.
/// </summary>
public static class PlanerAnnouncementRawSoundsWorkspace
{
    public const string FolderName = "ansagen_roh";

    public static string GetRawSoundsDirectory(LocalWorkspaceStore workspace)
    {
        var packageDir = Path.GetDirectoryName(workspace.PackageFilePath)
            ?? throw new InvalidOperationException("Workspace-Pfad ungültig.");
        var dir = Path.Combine(packageDir, FolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static IReadOnlyList<string> ListAudioFiles(LocalWorkspaceStore workspace)
    {
        var dir = GetRawSoundsDirectory(workspace);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(dir)
            .Where(EmbeddedSoundCatalog.IsAudioFile)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? TryGetLocalFilePath(LocalWorkspaceStore workspace, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var path = Path.Combine(GetRawSoundsDirectory(workspace), fileName.Trim());
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Löst Tonpfad aus Sequenz-Eintrag auf – auch nach Workspace-Sync (nur Dateiname im Rohordner).
    /// </summary>
    public static string? ResolveAudioPath(LocalWorkspaceStore workspace, string? sourcePath, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
        {
            return sourcePath;
        }

        var fileName = !string.IsNullOrWhiteSpace(sourcePath)
            ? Path.GetFileName(sourcePath)
            : displayName?.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return TryGetLocalFilePath(workspace, fileName);
    }

    public static Dictionary<string, PlanerWorkspaceBinaryPayload> CaptureForSync(
        LocalWorkspaceStore workspace,
        IReadOnlyDictionary<string, PlanerWorkspaceBinaryPayload>? reuseFrom = null)
    {
        return CaptureManifestForSync(workspace, reuseFrom);
    }

    /// <summary>
    /// Manifest für Dropbox-Sync: nur Dateiname, Größe und Hash – keine Base64-Daten in planer_workspace.json.
    /// </summary>
    public static Dictionary<string, PlanerWorkspaceBinaryPayload> CaptureManifestForSync(
        LocalWorkspaceStore workspace,
        IReadOnlyDictionary<string, PlanerWorkspaceBinaryPayload>? reuseFrom = null)
    {
        var result = new Dictionary<string, PlanerWorkspaceBinaryPayload>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ListAudioFiles(workspace))
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length <= 0)
                {
                    continue;
                }

                if (TryReuseManifestEntry(reuseFrom, fileName, fileInfo.Length, out var reused))
                {
                    result[fileName] = reused;
                    continue;
                }

                result[fileName] = new PlanerWorkspaceBinaryPayload
                {
                    Data = string.Empty,
                    Size = fileInfo.Length,
                    Sha256 = ComputeSha256Hex(path)
                };
            }
            catch
            {
                // Einzelne Datei überspringen
            }
        }

        return result;
    }

    public static bool TryReuseManifestEntry(
        IReadOnlyDictionary<string, PlanerWorkspaceBinaryPayload>? reuseFrom,
        string fileName,
        long fileSize,
        out PlanerWorkspaceBinaryPayload entry)
    {
        entry = new PlanerWorkspaceBinaryPayload();
        if (reuseFrom?.TryGetValue(fileName, out var cached) != true ||
            cached.Size != fileSize ||
            string.IsNullOrWhiteSpace(cached.Sha256))
        {
            return false;
        }

        entry = new PlanerWorkspaceBinaryPayload
        {
            Data = string.Empty,
            Size = cached.Size,
            Sha256 = cached.Sha256
        };
        return true;
    }

    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool LocalFileMatchesManifest(string filePath, PlanerWorkspaceBinaryPayload manifest)
    {
        if (!File.Exists(filePath) || manifest.Size <= 0)
        {
            return false;
        }

        var info = new FileInfo(filePath);
        if (info.Length != manifest.Size)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            return true;
        }

        return string.Equals(ComputeSha256Hex(filePath), manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static int ApplyFromSync(
        LocalWorkspaceStore workspace,
        IReadOnlyDictionary<string, PlanerWorkspaceBinaryPayload>? incoming,
        bool replaceExtraneous)
    {
        if (incoming is null)
        {
            return 0;
        }

        var dir = GetRawSoundsDirectory(workspace);
        var written = 0;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fileName, payload) in incoming)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            if (!EmbeddedSoundCatalog.IsAudioFile(fileName))
            {
                continue;
            }

            if (payload.IsExternalReference)
            {
                keep.Add(fileName.Trim());
                continue;
            }

            if (string.IsNullOrWhiteSpace(payload.Data))
            {
                continue;
            }

            try
            {
                var bytes = Convert.FromBase64String(payload.Data);
                if (bytes.Length == 0)
                {
                    continue;
                }

                var target = Path.Combine(dir, fileName.Trim());
                File.WriteAllBytes(target, bytes);
                keep.Add(Path.GetFileName(target));
                written++;
            }
            catch
            {
                // Einzelne Datei überspringen
            }
        }

        if (replaceExtraneous)
        {
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                if (!EmbeddedSoundCatalog.IsAudioFile(path))
                {
                    continue;
                }

                var fileName = Path.GetFileName(path);
                if (!keep.Contains(fileName))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        return written;
    }
}
