namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Zusammenführen von Fahrzeugdisposition bei Workspace-Sync (lokale Fahrten nicht verlieren).
/// </summary>
public static class VehicleDispositionMerge
{
    public static List<VehicleDispositionAssignment> Merge(
        IEnumerable<VehicleDispositionAssignment> local,
        IEnumerable<VehicleDispositionAssignment> incoming)
    {
        var byId = local.Select(a => a.Clone()).ToDictionary(a => a.Id, StringComparer.Ordinal);
        foreach (var assignment in incoming)
        {
            byId[assignment.Id] = assignment.Clone();
        }

        return byId.Values
            .OrderBy(a => a.StartEpochMs)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }
}
