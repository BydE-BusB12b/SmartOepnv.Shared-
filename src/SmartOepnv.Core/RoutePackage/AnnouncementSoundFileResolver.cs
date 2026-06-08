using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Findet Tondateinamen für Ansagen-Vorlagen (Code/Bezeichnung), wenn embeddedSoundFileName leer ist.
/// </summary>
public static class AnnouncementSoundFileResolver
{
    public static string? TryResolve(
        ManagedAnnouncementTemplateItem template,
        JsonObject? root,
        LocalWorkspaceStore? workspace)
    {
        var explicitName = template.EmbeddedSoundFileName?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        var candidates = CollectCandidates(root, workspace);
        return MatchFileName(candidates, template);
    }

    public static void ApplyResolvedFileNames(
        IList<ManagedAnnouncementTemplateItem> templates,
        JsonObject? root,
        LocalWorkspaceStore? workspace)
    {
        foreach (var template in templates)
        {
            if (!template.IncludeInSpecialAnnouncements ||
                !string.IsNullOrWhiteSpace(template.EmbeddedSoundFileName))
            {
                continue;
            }

            var resolved = TryResolve(template, root, workspace);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                template.EmbeddedSoundFileName = resolved;
            }
        }
    }

    private static HashSet<string> CollectCandidates(JsonObject? root, LocalWorkspaceStore? workspace)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (root is not null)
        {
            foreach (var fileName in GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(root).Keys)
            {
                candidates.Add(fileName);
            }

            foreach (var fileName in EmbeddedSoundsEditor.ListFileNames(root))
            {
                candidates.Add(fileName);
            }
        }

        if (workspace is not null)
        {
            try
            {
                var dir = PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(workspace);
                foreach (var path in Directory.EnumerateFiles(dir))
                {
                    if (EmbeddedSoundCatalog.IsAudioFile(path))
                    {
                        candidates.Add(Path.GetFileName(path));
                    }
                }
            }
            catch
            {
                // Workspace optional
            }
        }

        return candidates;
    }

    private static string? MatchFileName(
        IEnumerable<string> files,
        ManagedAnnouncementTemplateItem template)
    {
        var code = ManagedAnnouncementTemplateItem.NormalizeCode(template.AnnouncementCode);
        if (!string.IsNullOrEmpty(code))
        {
            foreach (var file in files)
            {
                if (file.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        var display = template.DisplayName.Trim();
        if (!string.IsNullOrEmpty(display))
        {
            foreach (var file in files)
            {
                var baseName = Path.GetFileNameWithoutExtension(file);
                if (baseName.Equals(display, StringComparison.OrdinalIgnoreCase) ||
                    file.Contains(display, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        return null;
    }
}
