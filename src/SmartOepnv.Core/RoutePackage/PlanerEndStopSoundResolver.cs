using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Löst die Endhaltestellen-Ansage aus der Ansagen-Kartei auf.
/// </summary>
public static class PlanerEndStopSoundResolver
{
    public static string? TryResolveEmbeddedFileName(
        IEnumerable<ManagedAnnouncementTemplateItem> templates,
        JsonObject? root,
        LocalWorkspaceStore? workspace) =>
        EndStopAnnouncementResolver.TryResolveEmbeddedFileName(templates, root, workspace);
}
