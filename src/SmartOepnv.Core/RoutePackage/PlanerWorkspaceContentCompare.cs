namespace SmartOepnv.Core.RoutePackage;

internal static class PlanerWorkspaceContentCompare
{
    public static int EstimateRichness(PlanerWorkspaceDocument document)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(document.RoutesPackageJson))
        {
            score += Math.Max(1, document.RoutesPackageJson.Length / 512);
            score += CountJsonArrayItems(document.RoutesPackageJson, "routes") * 25;
            score += CountJsonArrayItems(document.RoutesPackageJson, "employeeRoster") * 8;
            score += CountJsonArrayItems(document.RoutesPackageJson, "managedStopTemplates") * 4;
            score += CountJsonArrayItems(document.RoutesPackageJson, "managedAnnouncementTemplates") * 4;
            score += CountJsonArrayItems(document.RoutesPackageJson, "registeredVehicles") * 6;
        }

        if (document.PlannerOverlay.HasContent)
        {
            score += document.PlannerOverlay.Employees.Count * 8;
            score += document.PlannerOverlay.Vehicles.Count * 6;
        }

        score += document.VehicleDispositionAssignments.Count * 3;
        score += document.DriverDispositionAssignments?.Count ?? 0;
        score += document.SevSignDrafts.Count * 5;
        score += (document.MitteilungDrafts?.Count ?? 0) * 4;
        score += (document.DutyTemplates?.Count ?? 0) * 20;
        score += document.PackageVersionSnapshots.Count * 10;
        score += document.AnnouncementRawSounds.Count * 4;
        score += document.AnnouncementRawSounds.Values.Sum(p => p.Size > 0 ? (int)Math.Min(p.Size / 4096, 50) : 0);

        return score;
    }

    public static bool RemoteHasMoreContentThanLocal(
        PlanerWorkspaceDocument remote,
        PlanerWorkspaceDocument? local)
    {
        if (local is null)
        {
            return true;
        }

        var remoteScore = EstimateRichness(remote);
        var localScore = EstimateRichness(local);
        if (remoteScore > localScore + 5)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(local.RoutesPackageJson) &&
            !string.IsNullOrWhiteSpace(remote.RoutesPackageJson))
        {
            return true;
        }

        return false;
    }

    private static int CountJsonArrayItems(string json, string propertyName)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var array) &&
                array.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return array.GetArrayLength();
            }
        }
        catch
        {
            // ignore parse errors
        }

        return 0;
    }
}
