using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Endhaltestellen-Ansage aus der Ansagen-Kartei (Bezeichnung/Beschreibung enthält „Endhaltestelle“).
/// </summary>
public static class EndStopAnnouncementResolver
{
    public const string RootJsonFieldName = "endStopAnnouncementEmbeddedSoundFileName";

    public static bool MatchesTemplate(ManagedAnnouncementTemplateItem template)
    {
        var display = template.DisplayName?.Trim() ?? string.Empty;
        var description = template.Description?.Trim() ?? string.Empty;
        return ContainsEndStopLabel(display) || ContainsEndStopLabel(description);
    }

    public static ManagedAnnouncementTemplateItem? TryFindTemplate(
        IEnumerable<ManagedAnnouncementTemplateItem> templates) =>
        templates
            .Where(MatchesTemplate)
            .OrderByDescending(HasResolvedAudio)
            .ThenByDescending(t => IsExactEndStopLabel(t.DisplayName))
            .ThenByDescending(t => IsExactEndStopLabel(t.Description))
            .FirstOrDefault();

    public static string? TryResolveEmbeddedFileName(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        JsonObject? root,
        LocalWorkspaceStore? workspace)
    {
        var template = TryFindTemplate(templates);
        if (template is null)
        {
            return null;
        }

        return AnnouncementSoundFileResolver.TryResolve(template, root, workspace)?.Trim();
    }

    private static bool HasResolvedAudio(ManagedAnnouncementTemplateItem template) =>
        !string.IsNullOrWhiteSpace(template.EmbeddedSoundFileName) ||
        !string.IsNullOrWhiteSpace(template.LocalAudioPath);

    private static bool IsExactEndStopLabel(string? value) =>
        string.Equals(value?.Trim(), "Endhaltestelle", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsEndStopLabel(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("Endhaltestelle", StringComparison.OrdinalIgnoreCase);
}
