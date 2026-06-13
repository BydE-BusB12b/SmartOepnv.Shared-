namespace SmartOepnv.Core.RoutePackage;

public static class DriverDispositionMerge
{
    public static List<DriverDispositionAssignment> Merge(
        IEnumerable<DriverDispositionAssignment> local,
        IEnumerable<DriverDispositionAssignment> incoming)
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
