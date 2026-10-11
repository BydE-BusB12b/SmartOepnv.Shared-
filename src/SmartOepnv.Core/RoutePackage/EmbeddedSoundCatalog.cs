using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Verfügbare eingebettete Ansagen für Auswahllisten im Planer (nur noch referenzierte Töne).
/// </summary>
public static class EmbeddedSoundCatalog
{
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg"];

    /// <summary>
    /// Listet Tondateien, die von Vorlagen/Routen referenziert werden und lokal verfügbar sind
    /// (JSON, Workspace oder ausstehende lokale Datei auf einer Vorlage).
    /// </summary>
    public static IReadOnlyList<string> ListReferenced(
        EditableRoutePackage package,
        JsonObject packageRoot,
        LocalWorkspaceStore? workspace = null,
        IEnumerable<string>? additionalNames = null)
    {
        var referenced = EmbeddedSoundReferences.CollectFromPackage(package, packageRoot, workspace);
        if (additionalNames is not null)
        {
            foreach (var name in additionalNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    referenced.Add(name.Trim());
                }
            }
        }

        var embedded = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(packageRoot);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in referenced)
        {
            if (embedded.ContainsKey(fileName))
            {
                names.Add(fileName);
                continue;
            }

            if (workspace is not null &&
                PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, fileName) is not null)
            {
                names.Add(fileName);
                continue;
            }

            if (HasPendingLocalAudio(package, fileName))
            {
                names.Add(fileName);
            }
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsAudioFile(string path)
    {
        var ext = Path.GetExtension(path);
        return AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasPendingLocalAudio(EditableRoutePackage package, string fileName)
    {
        foreach (var template in package.AnnouncementTemplates)
        {
            if (string.Equals(template.EmbeddedSoundFileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(template.LocalAudioPath) &&
                File.Exists(template.LocalAudioPath))
            {
                return true;
            }
        }

        foreach (var template in package.StopTemplates)
        {
            if (string.Equals(template.EmbeddedSoundFileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(template.LocalAudioPath) &&
                File.Exists(template.LocalAudioPath))
            {
                return true;
            }
        }

        return false;
    }
}
