using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Sammelt referenzierte Tondateien und Standard-Ansagen für den Sequenz-Export.
/// </summary>
public static class AnnouncementSequenceExport
{
    public const double StandardPrefixLinkPauseSeconds = 1.0;

    public static bool IsLegacyMergedFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(fileName.Trim());
        return name.EndsWith("_zusammen", StringComparison.OrdinalIgnoreCase);
    }

    public static JsonArray WriteSequenceJson(IReadOnlyList<AnnouncementSequenceEntry> sequence)
    {
        var arr = new JsonArray();
        foreach (var entry in sequence)
        {
            switch (entry.Kind)
            {
                case AnnouncementExportEntryKind.Pause:
                    arr.Add(new JsonObject
                    {
                        ["kind"] = "pause",
                        ["pauseSeconds"] = entry.PauseSeconds
                    });
                    break;
                case AnnouncementExportEntryKind.Line:
                    arr.Add(new JsonObject { ["kind"] = "line" });
                    break;
                case AnnouncementExportEntryKind.Nach:
                    arr.Add(new JsonObject { ["kind"] = "nach" });
                    break;
                case AnnouncementExportEntryKind.RouteEndDestination:
                    arr.Add(new JsonObject { ["kind"] = "routeEndDestination" });
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(entry.FileName))
                    {
                        arr.Add(new JsonObject
                        {
                            ["kind"] = "audio",
                            ["fileName"] = entry.FileName.Trim()
                        });
                    }

                    break;
            }
        }

        return arr;
    }

    public static bool HasSequencePlayback(ManagedAnnouncementTemplateItem template) =>
        !IsLegacyMergedFileName(template.EmbeddedSoundFileName) &&
        (template.IncludeGong ||
         template.IncludeSondergong ||
         template.IncludeNextStopGerman ||
         template.IncludeNextStopMp3 ||
         template.IncludeFollowingStops ||
         template.AnnouncementSequence.Count > 0);

    public static IEnumerable<string> CollectReferencedFileNames(
        ManagedAnnouncementTemplateItem template,
        string? sondergongFileName = null)
    {
        if (!HasSequencePlayback(template))
        {
            if (!string.IsNullOrWhiteSpace(template.EmbeddedSoundFileName))
            {
                yield return template.EmbeddedSoundFileName.Trim();
            }

            yield break;
        }

        if (template.IncludeGong)
        {
            yield return PlanerGongSoundResolver.GongFileName;
        }

        if (template.IncludeSondergong)
        {
            var fileName = sondergongFileName?.Trim();
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                yield return fileName;
            }
        }

        if (template.IncludeNextStopGerman)
        {
            yield return PlanerNextStopSoundResolver.FileName;
        }

        if (template.IncludeNextStopMp3)
        {
            foreach (var name in PlanerNextStopMp3SoundResolver.CandidateFileNames)
            {
                yield return name;
            }
        }

        if (template.IncludeFollowingStops)
        {
            foreach (var name in PlanerFollowingStopsSoundResolver.CandidateFileNames)
            {
                yield return name;
            }
        }

        foreach (var entry in template.AnnouncementSequence)
        {
            if (entry.Kind == AnnouncementExportEntryKind.Audio &&
                !string.IsNullOrWhiteSpace(entry.FileName))
            {
                yield return entry.FileName.Trim();
            }

            if (entry.Kind == AnnouncementExportEntryKind.Nach)
            {
                yield return PlanerNachSoundResolver.FileName;
            }
        }
    }

    public static IEnumerable<string> CollectReferencedFileNames(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        string? sondergongFileName = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates)
        {
            foreach (var name in CollectReferencedFileNames(template, sondergongFileName))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
