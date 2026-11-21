using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Endhaltestellen-Ansage aus der Ansagen-Kartei.
/// EN: Bezeichnung/Beschreibung enthält „Endhaltestelle“ (nicht NL).
/// NL: „Eindhalteplaats“ / „Endhaltestelle NL“ / „Endhalte NL“.
/// </summary>
public static class EndStopAnnouncementResolver
{
    public const string RootJsonFieldName = "endStopAnnouncementEmbeddedSoundFileName";
    public const string RootJsonFieldNameNl = "endStopAnnouncementNlEmbeddedSoundFileName";

    public enum Language
    {
        English,
        Dutch
    }

    public static bool MatchesTemplate(ManagedAnnouncementTemplateItem template) =>
        MatchesTemplate(template, Language.English);

    public static bool MatchesTemplate(ManagedAnnouncementTemplateItem template, Language language)
    {
        var display = template.DisplayName?.Trim() ?? string.Empty;
        var description = template.Description?.Trim() ?? string.Empty;
        var combined = $"{display} {description}";
        return language switch
        {
            Language.Dutch => ContainsDutchEndStopLabel(combined),
            _ => ContainsEnglishEndStopLabel(combined)
        };
    }

    public static ManagedAnnouncementTemplateItem? TryFindTemplate(
        IEnumerable<ManagedAnnouncementTemplateItem> templates) =>
        TryFindTemplate(templates, Language.English);

    public static ManagedAnnouncementTemplateItem? TryFindTemplate(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        Language language) =>
        templates
            .Where(t => MatchesTemplate(t, language))
            .OrderByDescending(HasResolvedAudio)
            .ThenByDescending(t => IsExactLabel(t.DisplayName, language))
            .ThenByDescending(t => IsExactLabel(t.Description, language))
            .FirstOrDefault();

    public static string? TryResolveEmbeddedFileName(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        JsonObject? root,
        LocalWorkspaceStore? workspace) =>
        TryResolveEmbeddedFileName(templates, root, workspace, Language.English);

    public static string? TryResolveEmbeddedFileName(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        JsonObject? root,
        LocalWorkspaceStore? workspace,
        Language language)
    {
        if (root is not null)
        {
            var field = language == Language.Dutch ? RootJsonFieldNameNl : RootJsonFieldName;
            var explicitName = root[field]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(explicitName))
            {
                return explicitName;
            }
        }

        var template = TryFindTemplate(templates, language);
        if (template is null)
        {
            return null;
        }

        return AnnouncementSoundFileResolver.TryResolve(template, root, workspace)?.Trim();
    }

    private static bool HasResolvedAudio(ManagedAnnouncementTemplateItem template) =>
        !string.IsNullOrWhiteSpace(template.EmbeddedSoundFileName) ||
        !string.IsNullOrWhiteSpace(template.LocalAudioPath);

    private static bool IsExactLabel(string? value, Language language) =>
        language switch
        {
            Language.Dutch =>
                string.Equals(value?.Trim(), "Eindhalteplaats", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value?.Trim(), "Endhaltestelle NL", StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(value?.Trim(), "Endhaltestelle", StringComparison.OrdinalIgnoreCase)
        };

    private static bool ContainsDutchEndStopLabel(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("Eindhalteplaats", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("Endhaltestelle NL", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("Endhalte NL", StringComparison.OrdinalIgnoreCase) ||
         (value.Contains("Endhaltestelle", StringComparison.OrdinalIgnoreCase) &&
          (value.Contains(" NL", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("Niederl", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("Dutch", StringComparison.OrdinalIgnoreCase))));

    private static bool ContainsEnglishEndStopLabel(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("Endhaltestelle", StringComparison.OrdinalIgnoreCase) &&
        !ContainsDutchEndStopLabel(value);
}
