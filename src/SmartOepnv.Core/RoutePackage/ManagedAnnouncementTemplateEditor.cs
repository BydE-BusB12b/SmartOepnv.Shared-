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
            // Endhaltestellen-Ansage gehört nicht in die ITCS-/Handy-Sonderansagenliste.
            if (EndStopAnnouncementResolver.MatchesTemplate(t))
            {
                t.IncludeInSpecialAnnouncements = false;
            }

            NormalizeSpecialCategory(t);
            arr.Add(Write(t));
        }

        root["managedAnnouncementTemplates"] = arr;
    }

    /// <summary>
    /// Handy-Export: Sonderansagen stehen in <c>specialAnnouncements</c> (Schlüssel = Anzeigename).
    /// Beim Import Flags auf die Kartei übertragen, falls nur der Block gesetzt war.
    /// </summary>
    public static void ApplySpecialFlagsFromRoot(JsonObject root, IList<ManagedAnnouncementTemplateItem> templates)
    {
        if (root["specialAnnouncements"] is not JsonObject specialObj)
        {
            return;
        }

        foreach (var template in templates)
        {
            // Endhaltestellen-Ansage nie als Sonderansage markieren (auch bei Alt-Exporten).
            if (EndStopAnnouncementResolver.MatchesTemplate(template))
            {
                template.IncludeInSpecialAnnouncements = false;
                continue;
            }

            if (MatchesSpecialAnnouncementEntry(specialObj, template))
            {
                template.IncludeInSpecialAnnouncements = true;
                NormalizeSpecialCategory(template);
            }
        }
    }

    private static bool MatchesSpecialAnnouncementEntry(
        JsonObject specialObj,
        ManagedAnnouncementTemplateItem template)
    {
        var displayName = template.DisplayName.Trim();
        var fileName = template.EmbeddedSoundFileName.Trim();

        foreach (var prop in specialObj)
        {
            if (!string.IsNullOrEmpty(displayName) &&
                string.Equals(prop.Key, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (prop.Value is not JsonObject entry)
            {
                continue;
            }

            var entryName = entry["name"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(displayName) &&
                !string.IsNullOrEmpty(entryName) &&
                string.Equals(entryName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var entryFile = entry["fileName"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(fileName) &&
                !string.IsNullOrEmpty(entryFile) &&
                string.Equals(entryFile, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeSpecialCategory(ManagedAnnouncementTemplateItem template)
    {
        if (template.IncludeInSpecialAnnouncements &&
            string.Equals(template.Category, "haltestelle", StringComparison.OrdinalIgnoreCase))
        {
            template.Category = "sonder";
        }
    }

    private static ManagedAnnouncementTemplateItem Parse(JsonObject obj)
    {
        var id = obj["id"]?.GetValue<string>();
        var code = ManagedAnnouncementTemplateItem.NormalizeCode(
            obj["announcementCode"]?.GetValue<string>() ?? obj["code"]?.GetValue<string>());

        var item = new ManagedAnnouncementTemplateItem
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            StopTemplateId = obj["stopTemplateId"]?.GetValue<string>()?.Trim() ?? string.Empty,
            AnnouncementCode = code,
            DisplayName = obj["displayName"]?.GetValue<string>() ?? string.Empty,
            Description = obj["description"]?.GetValue<string>() ?? string.Empty,
            Lines = obj["lines"]?.GetValue<string>() ?? string.Empty,
            Category = obj["category"]?.GetValue<string>() ?? "haltestelle",
            EmbeddedSoundFileName = obj["embeddedSoundFileName"]?.GetValue<string>() ?? string.Empty,
            IncludeInSpecialAnnouncements = obj["includeInSpecialAnnouncements"]?.GetValue<bool>() ?? false
        };

        if (EndStopAnnouncementResolver.MatchesTemplate(item))
        {
            item.IncludeInSpecialAnnouncements = false;
        }

        return item;
    }

    private static JsonObject Write(ManagedAnnouncementTemplateItem t) => new()
    {
        ["id"] = t.Id,
        ["stopTemplateId"] = t.StopTemplateId,
        ["announcementCode"] = ManagedAnnouncementTemplateItem.NormalizeCode(t.AnnouncementCode),
        ["displayName"] = t.DisplayName,
        ["description"] = t.Description,
        ["lines"] = t.Lines,
        ["category"] = string.IsNullOrWhiteSpace(t.Category) ? "haltestelle" : t.Category,
        ["embeddedSoundFileName"] = t.EmbeddedSoundFileName,
        ["includeInSpecialAnnouncements"] = t.IncludeInSpecialAnnouncements
    };
}
