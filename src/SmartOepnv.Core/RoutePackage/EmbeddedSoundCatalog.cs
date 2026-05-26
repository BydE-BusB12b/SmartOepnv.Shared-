using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Verfügbare eingebettete Ansagen (embeddedSounds + Workspace) für Auswahllisten im Planer.
/// </summary>
public static class EmbeddedSoundCatalog
{
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg"];

    public static IReadOnlyList<string> ListAvailable(
        JsonObject packageRoot,
        LocalWorkspaceStore? workspace = null,
        IEnumerable<string>? additionalNames = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in EmbeddedSoundsEditor.ListFileNames(packageRoot))
        {
            names.Add(fileName);
        }

        if (workspace is not null)
        {
            try
            {
                var dir = PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(workspace);
                foreach (var path in Directory.EnumerateFiles(dir))
                {
                    if (IsAudioFile(path))
                    {
                        names.Add(Path.GetFileName(path));
                    }
                }
            }
            catch
            {
                // Workspace optional
            }
        }

        if (additionalNames is not null)
        {
            foreach (var name in additionalNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsAudioFile(string path)
    {
        var ext = Path.GetExtension(path);
        return AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
