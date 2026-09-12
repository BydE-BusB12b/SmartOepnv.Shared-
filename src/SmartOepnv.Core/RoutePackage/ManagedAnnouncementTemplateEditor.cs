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
            if (StartStopGreetingResolver.MatchesAnyGreetingTemplate(t))
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
            if (StartStopGreetingResolver.MatchesAnyGreetingTemplate(template))
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
            AudioOutput = AnnouncementAudioOutput.Normalize(obj["audioOutput"]?.GetValue<string>()),
            IncludeInSpecialAnnouncements = obj["includeInSpecialAnnouncements"]?.GetValue<bool>() ?? false,
            IncludeGong = obj["includeGong"]?.GetValue<bool>() ?? false,
            IncludeSondergong = obj["includeSondergong"]?.GetValue<bool>() ?? false,
            IncludeNextStopGerman = obj["includeNextStopGerman"]?.GetValue<bool>() ?? false,
            IncludeNextStopMp3 = obj["includeNextStopMp3"]?.GetValue<bool>() ?? false,
            IncludeFollowingStops = obj["includeFollowingStops"]?.GetValue<bool>() ?? false,
            SondergongFileName = obj["sondergongFileName"]?.GetValue<string>() ?? string.Empty,
            AnnouncementSequence = ParseSequence(obj["announcementSequence"])
        };

        if (StartStopGreetingResolver.MatchesAnyGreetingTemplate(item))
        {
            item.IncludeInSpecialAnnouncements = false;
        }

        return item;
    }

    private static List<AnnouncementSequenceEntry> ParseSequence(JsonNode? node)
    {
        var list = new List<AnnouncementSequenceEntry>();
        if (node is not JsonArray arr)
        {
            return list;
        }

        foreach (var itemNode in arr.OfType<JsonObject>())
        {
            var kindRaw = itemNode["kind"]?.GetValue<string>()?.Trim().ToLowerInvariant();
            if (kindRaw == "pause")
            {
                list.Add(new AnnouncementSequenceEntry
                {
                    Kind = AnnouncementExportEntryKind.Pause,
                    PauseSeconds = itemNode["pauseSeconds"]?.GetValue<double>() ?? 0.5
                });
                continue;
            }

            if (kindRaw is "line" or "linie")
            {
                list.Add(new AnnouncementSequenceEntry { Kind = AnnouncementExportEntryKind.Line });
                continue;
            }

            if (kindRaw == "nach")
            {
                list.Add(new AnnouncementSequenceEntry { Kind = AnnouncementExportEntryKind.Nach });
                continue;
            }

            if (kindRaw is "routeenddestination" or "enddestination" or "endhaltestelle")
            {
                list.Add(new AnnouncementSequenceEntry { Kind = AnnouncementExportEntryKind.RouteEndDestination });
                continue;
            }

            var fileName = itemNode["fileName"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            list.Add(new AnnouncementSequenceEntry
            {
                Kind = AnnouncementExportEntryKind.Audio,
                FileName = fileName
            });
        }

        return list;
    }

    private static JsonArray WriteSequence(IReadOnlyList<AnnouncementSequenceEntry> sequence)
    {
        var arr = new JsonArray();
        foreach (var entry in sequence)
        {
            if (entry.Kind == AnnouncementExportEntryKind.Pause)
            {
                arr.Add(new JsonObject
                {
                    ["kind"] = "pause",
                    ["pauseSeconds"] = entry.PauseSeconds
                });
                continue;
            }

            if (entry.Kind == AnnouncementExportEntryKind.Line)
            {
                arr.Add(new JsonObject { ["kind"] = "line" });
                continue;
            }

            if (entry.Kind == AnnouncementExportEntryKind.Nach)
            {
                arr.Add(new JsonObject { ["kind"] = "nach" });
                continue;
            }

            if (entry.Kind == AnnouncementExportEntryKind.RouteEndDestination)
            {
                arr.Add(new JsonObject { ["kind"] = "routeEndDestination" });
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.FileName))
            {
                continue;
            }

            arr.Add(new JsonObject
            {
                ["kind"] = "audio",
                ["fileName"] = entry.FileName.Trim()
            });
        }

        return arr;
    }

    private static JsonObject Write(ManagedAnnouncementTemplateItem t)
    {
        var obj = new JsonObject
        {
            ["id"] = t.Id,
            ["stopTemplateId"] = t.StopTemplateId,
            ["announcementCode"] = ManagedAnnouncementTemplateItem.NormalizeCode(t.AnnouncementCode),
            ["displayName"] = t.DisplayName,
            ["description"] = t.Description,
            ["lines"] = t.Lines,
            ["category"] = string.IsNullOrWhiteSpace(t.Category) ? "haltestelle" : t.Category,
            ["embeddedSoundFileName"] = t.EmbeddedSoundFileName,
            ["audioOutput"] = AnnouncementAudioOutput.Normalize(t.AudioOutput),
            ["includeInSpecialAnnouncements"] = t.IncludeInSpecialAnnouncements
        };

        if (t.IncludeGong)
        {
            obj["includeGong"] = true;
        }

        if (t.IncludeSondergong)
        {
            obj["includeSondergong"] = true;
        }

        if (t.IncludeNextStopGerman)
        {
            obj["includeNextStopGerman"] = true;
        }

        if (t.IncludeNextStopMp3)
        {
            obj["includeNextStopMp3"] = true;
        }

        if (t.IncludeFollowingStops)
        {
            obj["includeFollowingStops"] = true;
        }

        if (!string.IsNullOrWhiteSpace(t.SondergongFileName))
        {
            obj["sondergongFileName"] = t.SondergongFileName.Trim();
        }

        if (t.AnnouncementSequence.Count > 0)
        {
            obj["announcementSequence"] = WriteSequence(t.AnnouncementSequence);
        }

        return obj;
    }
}
