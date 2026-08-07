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
            .Concat(package.AnnouncementTemplates
                .Where(t => t.IncludeInSpecialAnnouncements)
                .Select(t => t.EmbeddedSoundFileName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (stops.Any(s => s.PlayEndStopAnnouncement) && root is not null)
        {
            var endStopFile = EndStopAnnouncementResolver.TryResolveEmbeddedFileName(
                package.AnnouncementTemplates,
                root,
                workspace);
            if (!string.IsNullOrWhiteSpace(endStopFile))
            {
                names.Add(endStopFile.Trim());
            }
        }

        return names;
    }
}
