using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Ermittelt eingebettete Tondateien, die aktuell von Routen, Haltestellen- oder Ansage-Vorlagen referenziert werden.
/// </summary>
public static class EmbeddedSoundReferences
{
    public static HashSet<string> CollectFromPackage(
        EditableRoutePackage package,
        JsonObject? root = null,
        LocalWorkspaceStore? workspace = null,
        IEnumerable<RouteStopItem>? routeStopsScope = null)
    {
        var stops = routeStopsScope ?? package.StopsByRoute.Values.SelectMany(s => s);
        var names = stops
            .Select(s => s.EmbeddedSoundFileName)
            .Concat(package.StopTemplates.Select(t => t.EmbeddedSoundFileName))
            .Concat(package.AnnouncementTemplates.Select(t => t.EmbeddedSoundFileName))
            .Concat(AnnouncementSequenceExport.CollectReferencedFileNames(
                package.AnnouncementTemplates,
                PlanerSondergongSoundResolver.ConfiguredFileName(
                    workspace is not null && AppServices.IsInitialized
                        ? AppServices.PlanerAppSettings?.Load()
                        : null)))
            .Concat(package.AnnouncementTemplates
                .Where(t => t.IncludeInSpecialAnnouncements)
                .Select(t => t.EmbeddedSoundFileName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (root is not null)
        {
            var stopList = stops.ToList();
            if (stopList.Any(s => s.PlayEndStopAnnouncementEn))
            {
                var endStopFile = EndStopAnnouncementResolver.TryResolveEmbeddedFileName(
                    package.AnnouncementTemplates,
                    root,
                    workspace,
                    EndStopAnnouncementResolver.Language.English);
                if (!string.IsNullOrWhiteSpace(endStopFile))
                {
                    names.Add(endStopFile.Trim());
                }
            }

            if (stopList.Any(s => s.PlayEndStopAnnouncementNl))
            {
                var endStopFileNl = EndStopAnnouncementResolver.TryResolveEmbeddedFileName(
                    package.AnnouncementTemplates,
                    root,
                    workspace,
                    EndStopAnnouncementResolver.Language.Dutch);
                if (!string.IsNullOrWhiteSpace(endStopFileNl))
                {
                    names.Add(endStopFileNl.Trim());
                }
            }
        }

        if (stops.Any(s => s.PlayStartStopGreeting))
        {
            if (StartStopGreetingResolver.Part1UsesSequencePlayback(package.AnnouncementTemplates))
            {
                foreach (var name in StartStopGreetingResolver.CollectPart1SequenceEmbeddedSoundNames(
                             package.AnnouncementTemplates,
                             package,
                             workspace))
                {
                    names.Add(name);
                }
            }
            else
            {
                var part1 = StartStopGreetingResolver.TryResolvePart1FileName(
                    package.AnnouncementTemplates,
                    root,
                    workspace);
                if (!string.IsNullOrWhiteSpace(part1))
                {
                    names.Add(part1.Trim());
                }
            }

            var part2 = StartStopGreetingResolver.TryResolvePart2FileName(
                package.AnnouncementTemplates,
                root,
                workspace);
            if (!string.IsNullOrWhiteSpace(part2))
            {
                names.Add(part2.Trim());
            }

            var sondergong = StartStopGreetingResolver.TryResolveSondergongFileName(
                workspace is not null && AppServices.IsInitialized
                    ? AppServices.PlanerAppSettings?.Load()
                    : null);
            if (!string.IsNullOrWhiteSpace(sondergong))
            {
                names.Add(sondergong.Trim());
            }

            names.Add(PlanerBusDrivesViaSoundResolver.FileName);
            names.Add(PlanerUndSoundResolver.FileName);
        }

        return names;
    }
}
