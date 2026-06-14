namespace SmartOepnv.Core.RoutePackage;

/// <summary>Disponierter Dienst für die Fahrer-App (routes_export.json → driverDutyDispatches).</summary>
public sealed class DriverDutyDispatchPayload
{
    public string AssignmentId { get; set; } = string.Empty;

    public string DriverKey { get; set; } = string.Empty;

    public string PersonnelNumber { get; set; } = string.Empty;

    public string DutyTemplateId { get; set; } = string.Empty;

    public int DutyTemplatePartIndex { get; set; } = 1;

    public string Label { get; set; } = string.Empty;

    public long StartEpochMs { get; set; }

    public long EndEpochMs { get; set; }

    public long Part1EndEpochMs { get; set; }

    public long Part2StartEpochMs { get; set; }

    public string DutyNumber { get; set; } = string.Empty;

    public string DutyNumberPart2 { get; set; } = string.Empty;

    public string DutyNumberPart3 { get; set; } = string.Empty;

    public string DefaultLineCourse { get; set; } = string.Empty;

    public string VehicleNumber { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public List<DriverDutyDispatchTripPayload> Trips { get; set; } = [];
}

public sealed class DriverDutyDispatchTripPayload
{
    public string TripNumber { get; set; } = string.Empty;

    public string LineCourse { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    public string FromTime { get; set; } = string.Empty;

    public string FromStop { get; set; } = string.Empty;

    public string ToTime { get; set; } = string.Empty;

    public string ToStop { get; set; } = string.Empty;

    public int PartIndex { get; set; } = 1;
}
