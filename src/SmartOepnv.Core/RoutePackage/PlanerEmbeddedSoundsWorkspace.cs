using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Lokale Tondateien neben dem Route-Paket (workspace/embedded_sounds) –
/// Import aus JSON-Base64, Quelle beim erneuten Export nach Dropbox.
/// </summary>
public static class PlanerEmbeddedSoundsWorkspace
{
    public static string GetSoundsDirectory(LocalWorkspaceStore workspace)
    {
        var packageDir = Path.GetDirectoryName(workspace.PackageFilePath)
            ?? throw new InvalidOperationException("Workspace-Pfad ungültig.");
        var dir = Path.Combine(packageDir, "embedded_sounds");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string? TryGetLocalFilePath(LocalWorkspaceStore workspace, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var path = Path.Combine(GetSoundsDirectory(workspace), fileName.Trim());
        return File.Exists(path) ? path : null;
    }

    public static bool HasSoundInPackage(JsonObject root, string fileName)
    {
        return GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(root)
            .ContainsKey(fileName);
    }

    /// <summary>Schreibt alle <c>embeddedSounds</c> aus dem JSON in den Workspace-Ordner.</summary>
    public static int ExtractFromJsonToWorkspace(LocalWorkspaceStore workspace, string json)
    {
        var count = 0;
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject root)
            {
                return 0;
            }

            var dir = GetSoundsDirectory(workspace);
            var entries = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(root);
            foreach (var (fileName, payload) in entries)
            {
                if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(payload.Base64))
                {
                    continue;
                }

                try
                {
                    var bytes = Convert.FromBase64String(payload.Base64);
                    if (bytes.Length == 0)
                    {
                        continue;
                    }

                    var target = Path.Combine(dir, fileName);
                    if (File.Exists(target))
                    {
                        continue;
                    }

                    File.WriteAllBytes(target, bytes);
                    count++;
                }
                catch
                {
                    // Einzelne Datei überspringen
                }
            }
        }
        catch
        {
            return count;
        }

        return count;
    }

    /// <summary>Entfernt Tondateien im Workspace, die nicht mehr referenziert sind.</summary>
    public static void PruneUnreferencedFiles(LocalWorkspaceStore workspace, IEnumerable<string> keepFileNames)
    {
        var keep = new HashSet<string>(
            keepFileNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var dir = GetSoundsDirectory(workspace);
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                if (!EmbeddedSoundCatalog.IsAudioFile(path))
                {
                    continue;
                }

                var fileName = Path.GetFileName(path);
                if (!keep.Contains(fileName))
                {
                    File.Delete(path);
                }
            }
        }
        catch
        {
            // Workspace optional
        }
    }
}
