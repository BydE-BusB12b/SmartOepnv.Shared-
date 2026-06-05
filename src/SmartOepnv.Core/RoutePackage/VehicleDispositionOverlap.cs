namespace SmartOepnv.Core.RoutePackage;

public static class VehicleDispositionOverlap
{
    public const string ConflictMessage = "Fahrzeug ist verplant. Bitte korrigieren.";

    public static bool HasConflict(
        IEnumerable<VehicleDispositionAssignment> assignments,
        string vehicleKey,
        long startEpochMs,
        long endEpochMs,
        string? excludeAssignmentId = null)
    {
        if (string.IsNullOrEmpty(vehicleKey))
        {
            return false;
        }

        foreach (var assignment in assignments)
        {
            if (excludeAssignmentId is not null &&
                string.Equals(assignment.Id, excludeAssignmentId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(assignment.VehiclePhone, vehicleKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (assignment.StartEpochMs < endEpochMs && assignment.EndEpochMs > startEpochMs)
            {
                return true;
            }
        }

        return false;
    }
}
