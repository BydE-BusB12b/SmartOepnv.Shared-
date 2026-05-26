using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

public static class ManagedAnnouncementTemplateEditor
{
    public static IList<ManagedAnnouncementTemplateItem> LoadFromRoot(JsonObject root)
    {
        var list = new List<ManagedAnnouncementTemplateItem>();
        if (root["managedAnnouncementTemplates"] is not JsonArray arr)
        {
            return list;
        }

        foreach (var node in arr.OfType<JsonObject>())
        {
            list.Add(Parse(node));
        }

        return list;
    }

    public static void SaveToRoot(JsonObject root, IList<ManagedAnnouncementTemplateItem> templates)
    {
        var arr = new JsonArray();
        foreach (var t in templates)
        {
            arr.Add(Write(t));
        }

        root["managedAnnouncementTemplates"] = arr;
    }

    private static ManagedAnnouncementTemplateItem Parse(JsonObject obj)
    {
        var id = obj["id"]?.GetValue<string>();
        var code = ManagedAnnouncementTemplateItem.NormalizeCode(
            obj["announcementCode"]?.GetValue<string>() ?? obj["code"]?.GetValue<string>());

        return new ManagedAnnouncementTemplateItem
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            StopTemplateId = obj["stopTemplateId"]?.GetValue<string>()?.Trim() ?? string.Empty,
            AnnouncementCode = code,
            DisplayName = obj["displayName"]?.GetValue<string>() ?? string.Empty,
            Description = obj["description"]?.GetValue<string>() ?? string.Empty,
            Category = obj["category"]?.GetValue<string>() ?? "haltestelle",
            EmbeddedSoundFileName = obj["embeddedSoundFileName"]?.GetValue<string>() ?? string.Empty,
            IncludeInSpecialAnnouncements = obj["includeInSpecialAnnouncements"]?.GetValue<bool>() ?? false
        };
    }

    private static JsonObject Write(ManagedAnnouncementTemplateItem t) => new()
    {
        ["id"] = t.Id,
        ["stopTemplateId"] = t.StopTemplateId,
        ["announcementCode"] = ManagedAnnouncementTemplateItem.NormalizeCode(t.AnnouncementCode),
        ["displayName"] = t.DisplayName,
        ["description"] = t.Description,
        ["category"] = string.IsNullOrWhiteSpace(t.Category) ? "haltestelle" : t.Category,
        ["embeddedSoundFileName"] = t.EmbeddedSoundFileName,
        ["includeInSpecialAnnouncements"] = t.IncludeInSpecialAnnouncements
    };
}
