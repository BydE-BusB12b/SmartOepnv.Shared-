using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// GPSAnsagen-Export: <c>specialAnnouncements</c> als Objekt (Schlüssel = Anzeigename).
/// Beim Handy-Export bleibt die bestehende Sonderansagenliste maßgeblich; der Planer
/// schreibt dieselbe Struktur aus der Kartei für den Import.
/// </summary>
public static class SpecialAnnouncementsEditor
{
    public static void SyncToRootFromTemplates(JsonObject root, IList<ManagedAnnouncementTemplateItem> templates)
    {
        SyncToRootFromTemplates(root, templates, workspace: null);
    }

    public static void SyncToRootFromTemplates(
        JsonObject root,
        IList<ManagedAnnouncementTemplateItem> templates,
        LocalWorkspaceStore? workspace)
    {
        var active = templates
            .Where(t => t.IncludeInSpecialAnnouncements && !string.IsNullOrWhiteSpace(t.EmbeddedSoundFileName))
            .ToList();

        if (active.Count == 0)
        {
            root.Remove("specialAnnouncements");
            return;
        }

        var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(root);
        var obj = new JsonObject();
        foreach (var t in active)
        {
            var name = string.IsNullOrWhiteSpace(t.DisplayName)
                ? t.EmbeddedSoundFileName.Trim()
                : t.DisplayName.Trim();
            if (obj.ContainsKey(name))
            {
                name = $"{name} ({t.AnnouncementCode})";
            }

            var fileName = t.EmbeddedSoundFileName.Trim();
            var entry = new JsonObject
            {
                ["id"] = t.Id,
                ["name"] = name,
                ["isEmbedded"] = true,
                ["fileName"] = fileName
            };

            if (sounds.TryGetValue(fileName, out var audio))
            {
                entry["audioData"] = audio.Base64;
                entry["audioSize"] = audio.Size;
            }
            else if (workspace is not null)
            {
                var path = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, fileName);
                if (path is not null && File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    entry["audioData"] = Convert.ToBase64String(bytes);
                    entry["audioSize"] = bytes.Length;
                }
            }

            obj[name] = entry;
        }

        root["specialAnnouncements"] = obj;
    }
}
