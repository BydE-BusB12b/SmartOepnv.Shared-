using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Starthaltestellen-Begrüßung aus der Ansagen-Kartei (Teil 1 / Teil 2).
/// </summary>
public static class StartStopGreetingResolver
{
    public const string Part1RootJsonFieldName = "startStopGreetingPart1EmbeddedSoundFileName";
    public const string Part1SequenceRootJsonFieldName = "startStopGreetingPart1Sequence";
    public const string Part2RootJsonFieldName = "startStopGreetingPart2EmbeddedSoundFileName";
    public const string SondergongRootJsonFieldName = "startStopGreetingSondergongFileName";

    public const string Part1Label = "Begrüßung Teil 1";
    public const string Part2Label = "Begrüßung Teil 2";

    public static bool MatchesPart1Template(ManagedAnnouncementTemplateItem template) =>
        MatchesPartLabel(template, Part1Label) ||
        ContainsLabel(template, "begrüßung teil 1") ||
        ContainsLabel(template, "begruessung teil 1") ||
        MatchesPart1ShortAlias(template);

    private static bool MatchesPart1ShortAlias(ManagedAnnouncementTemplateItem template)
    {
        foreach (var value in new[] { template.DisplayName, template.Description })
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Equals("Begrüßung 1", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Begruessung 1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesPart2Template(ManagedAnnouncementTemplateItem template) =>
        MatchesPartLabel(template, Part2Label) ||
        ContainsLabel(template, "begrüßung teil 2") ||
        ContainsLabel(template, "begruessung teil 2") ||
        MatchesPart2ShortAlias(template);

    private static bool MatchesPart2ShortAlias(ManagedAnnouncementTemplateItem template)
    {
        foreach (var value in new[] { template.DisplayName, template.Description })
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Equals("Begrüßung 2", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Begruessung 2", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesAnyGreetingTemplate(ManagedAnnouncementTemplateItem template) =>
        MatchesPart1Template(template) || MatchesPart2Template(template);

    public static ManagedAnnouncementTemplateItem? TryFindPart1Template(
        IEnumerable<ManagedAnnouncementTemplateItem> templates) =>
        FindTemplate(templates, MatchesPart1Template, Part1Label);

    public static ManagedAnnouncementTemplateItem? TryFindPart2Template(
        IEnumerable<ManagedAnnouncementTemplateItem> templates) =>
        FindTemplate(templates, MatchesPart2Template, Part2Label);

    public static string? TryResolvePart1FileName(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        JsonObject? root,
        LocalWorkspaceStore? workspace) =>
        ResolveFileName(TryFindPart1Template(templates), root, workspace);

    public static IReadOnlyList<AnnouncementSequenceEntry> TryGetPart1Sequence(
        IEnumerable<ManagedAnnouncementTemplateItem> templates) =>
        TryFindPart1Template(templates)?.AnnouncementSequence ?? [];

    public static bool Part1UsesSequencePlayback(IEnumerable<ManagedAnnouncementTemplateItem> templates)
    {
        var template = TryFindPart1Template(templates);
        if (template is null)
        {
            return false;
        }

        return template.AnnouncementSequence.Count > 0 &&
               !AnnouncementSequenceExport.IsLegacyMergedFileName(template.EmbeddedSoundFileName);
    }

    public static IEnumerable<string> CollectPart1SequenceEmbeddedSoundNames(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        EditableRoutePackage package,
        LocalWorkspaceStore? workspace)
    {
        if (!Part1UsesSequencePlayback(templates))
        {
            yield break;
        }

        var sequence = TryGetPart1Sequence(templates);
        var hasLine = false;
        var hasRouteEnd = false;
        foreach (var entry in sequence)
        {
            switch (entry.Kind)
            {
                case AnnouncementExportEntryKind.Audio when !string.IsNullOrWhiteSpace(entry.FileName):
                    yield return entry.FileName.Trim();
                    break;
                case AnnouncementExportEntryKind.Nach:
                    yield return PlanerNachSoundResolver.FileName;
                    break;
                case AnnouncementExportEntryKind.Line:
                    hasLine = true;
                    break;
                case AnnouncementExportEntryKind.RouteEndDestination:
                    hasRouteEnd = true;
                    break;
            }
        }

        if (hasLine && workspace is not null)
        {
            var extraLines = AppServices.IsInitialized
                ? AppServices.PlanerAppSettings?.Load().SpecialBuildingBlockLines
                : null;
            var hiddenLines = AppServices.IsInitialized
                ? AppServices.PlanerAppSettings?.Load().SpecialBuildingBlockLinesHidden
                : null;
            foreach (var displayLine in PlanerSpecialBuildingBlocksCatalog.CollectAllLineStems(
                         package,
                         extraLines,
                         hiddenLines))
            {
                var fileName = PlanerLineSoundResolver.BuildFileName(displayLine);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    yield return fileName;
                }
            }
        }

        // Begrüßungs-Aufzählung „A, B und C“
        yield return PlanerUndSoundResolver.FileName;

        if (hasRouteEnd)
        {
            foreach (var fileName in CollectRouteEndStopNameFileNames(package))
            {
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    yield return fileName.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> CollectPassengerDisplayLines(EditableRoutePackage package)
    {
        var lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var routeName in package.RouteNames)
        {
            var parsed = RouteDisplayHelper.Parse(routeName);
            var displayLine = (parsed.PassengerDisplayLine ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(displayLine))
            {
                displayLine = RouteDisplayHelper.NormalizeLineCourse(parsed.LineCourse);
            }

            if (!string.IsNullOrWhiteSpace(displayLine))
            {
                lines.Add(displayLine);
            }
        }

        return lines;
    }

    private static IEnumerable<string> CollectRouteEndStopNameFileNames(EditableRoutePackage package)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stops in package.StopsByRoute.Values)
        {
            // Fahrgast-Ziel = Halt mit Endhaltestellen-Ansage (nicht Wende-/Endhaltestelle)
            var destinationStop = stops.LastOrDefault(s => s.PlayEndStopAnnouncement && !s.IsWaypoint)
                                  ?? stops.LastOrDefault(s => s.IsEndStop && !s.IsWaypoint)
                                  ?? stops.LastOrDefault(s => !s.IsWaypoint);
            if (destinationStop is null)
            {
                continue;
            }

            var fileName = ResolveStopNameEmbeddedFileName(package, destinationStop);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                names.Add(fileName.Trim());
            }
        }

        return names;
    }

    private static string? ResolveStopNameEmbeddedFileName(
        EditableRoutePackage package,
        RouteStopItem stop)
    {
        if (!string.IsNullOrWhiteSpace(stop.EmbeddedSoundFileName) &&
            !AnnouncementSequenceExport.IsLegacyMergedFileName(stop.EmbeddedSoundFileName))
        {
            return stop.EmbeddedSoundFileName.Trim();
        }

        var stopTemplate = package.StopTemplates
            .FirstOrDefault(t => string.Equals(
                PlannerStopCode.Normalize(t.StopCode),
                PlannerStopCode.Normalize(stop.PlannerStopCode),
                StringComparison.OrdinalIgnoreCase));
        var announcementTemplate = stopTemplate is null
            ? null
            : package.AnnouncementTemplates
                .FirstOrDefault(t => string.Equals(t.StopTemplateId, stopTemplate.Id, StringComparison.OrdinalIgnoreCase));

        if (announcementTemplate is null)
        {
            return stop.EmbeddedSoundFileName?.Trim();
        }

        foreach (var entry in announcementTemplate.AnnouncementSequence)
        {
            if (entry.Kind == AnnouncementExportEntryKind.Audio &&
                !string.IsNullOrWhiteSpace(entry.FileName))
            {
                return entry.FileName.Trim();
            }
        }

        return announcementTemplate.EmbeddedSoundFileName?.Trim();
    }

    public static string? TryResolvePart2FileName(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        JsonObject? root,
        LocalWorkspaceStore? workspace) =>
        ResolveFileName(TryFindPart2Template(templates), root, workspace);

    public static string? TryResolveSondergongFileName(PlanerAppSettings? settings) =>
        PlanerSondergongSoundResolver.ConfiguredFileName(settings);

    private static ManagedAnnouncementTemplateItem? FindTemplate(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        Func<ManagedAnnouncementTemplateItem, bool> matcher,
        string exactLabel) =>
        templates
            .Where(matcher)
            .OrderByDescending(HasResolvedAudio)
            .ThenByDescending(t => IsExactLabel(t.DisplayName, exactLabel))
            .ThenByDescending(t => IsExactLabel(t.Description, exactLabel))
            .FirstOrDefault();

    private static string? ResolveFileName(
        ManagedAnnouncementTemplateItem? template,
        JsonObject? root,
        LocalWorkspaceStore? workspace)
    {
        if (template is null)
        {
            return null;
        }

        return AnnouncementSoundFileResolver.TryResolve(template, root, workspace)?.Trim();
    }

    private static bool HasResolvedAudio(ManagedAnnouncementTemplateItem template) =>
        !string.IsNullOrWhiteSpace(template.EmbeddedSoundFileName) ||
        !string.IsNullOrWhiteSpace(template.LocalAudioPath);

    private static bool MatchesPartLabel(ManagedAnnouncementTemplateItem template, string exactLabel) =>
        IsExactLabel(template.DisplayName, exactLabel) ||
        IsExactLabel(template.Description, exactLabel);

    private static bool IsExactLabel(string? value, string exactLabel) =>
        string.Equals(value?.Trim(), exactLabel, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsLabel(ManagedAnnouncementTemplateItem template, string needle)
    {
        var display = template.DisplayName?.Trim() ?? string.Empty;
        var description = template.Description?.Trim() ?? string.Empty;
        return display.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
               description.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
