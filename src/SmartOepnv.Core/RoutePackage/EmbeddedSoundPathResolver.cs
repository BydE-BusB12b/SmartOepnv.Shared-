using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst eingebettete Ansagen-Dateinamen zu lokalen Pfaden auf (Workspace oder JSON-Base64).
/// </summary>
public static class EmbeddedSoundPathResolver
{
    public static string? TryResolveLocalPath(
        string fileName,
        JsonObject packageRoot,
        LocalWorkspaceStore? workspace)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var trimmed = fileName.Trim();

        if (workspace is not null)
        {
            var workspacePath = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, trimmed);
            if (workspacePath is not null)
            {
                return workspacePath;
            }
        }

        var entries = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(packageRoot);
        if (!entries.TryGetValue(trimmed, out var payload) || string.IsNullOrWhiteSpace(payload.Base64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(payload.Base64);
            if (bytes.Length == 0)
            {
                return null;
            }

            var cacheDir = Path.Combine(Path.GetTempPath(), "SmartOepnv", "embedded_sound_cache");
            Directory.CreateDirectory(cacheDir);
            var cachePath = Path.Combine(cacheDir, trimmed);
            File.WriteAllBytes(cachePath, bytes);
            return cachePath;
        }
        catch
        {
            return null;
        }
    }
}
