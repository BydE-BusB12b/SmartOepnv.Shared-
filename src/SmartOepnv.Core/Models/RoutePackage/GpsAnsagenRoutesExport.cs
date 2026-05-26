namespace SmartOepnv.Core.Models.RoutePackage;

/// <summary>
/// Entspricht dem GPSAnsagen-Export (RouteDistributionManager, version 1.0, exportType routes).
/// Property-Namen bleiben für JSON-Kompatibilität bei camelCase-Serialisierung.
/// </summary>
public sealed class GpsAnsagenRoutesExport
{
    public string Version { get; set; } = "1.0";
    public string ExportType { get; set; } = "routes";
    public long Timestamp { get; set; }
    public string? DeviceId { get; set; }
    public bool AutoImport { get; set; }
    public List<string> Routes { get; set; } = [];
    public Dictionary<string, List<LineCourseRouteInfo>> LineCourseRoutes { get; set; } = new();
    public Dictionary<string, List<PlannedStop>> RouteStops { get; set; } = new();
    public Dictionary<string, EmbeddedSoundPayload> EmbeddedSounds { get; set; } = new();
    public Dictionary<string, string> RouteOfflineGuidance { get; set; } = new();
    public Dictionary<string, string> RoutePathDrafts { get; set; } = new();
    public List<DriverProfile> EmployeeRoster { get; set; } = [];
}

public sealed class LineCourseRouteInfo
{
    public string Name { get; set; } = string.Empty;
    public string? LineCourse { get; set; }
    public string? TripNumber { get; set; }
}

public sealed class PlannedStop
{
    public string Name { get; set; } = string.Empty;
    public string? GpsCoordinates { get; set; }
    public string? StopCoordinates { get; set; }
    public int Radius { get; set; } = 50;
    public bool IsAnnouncementEnabled { get; set; } = true;
    public string? EmbeddedSoundFileName { get; set; }
    public string? VrrStopId { get; set; }
}

public sealed class EmbeddedSoundPayload
{
    public string Data { get; set; } = string.Empty;
    public long Size { get; set; }
}

public sealed class DriverProfile
{
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PersonnelNumber { get; set; } = string.Empty;
    public string? LicenseExpiry { get; set; }
    public string? FqnExpiry { get; set; }
    public string? DriverCardExpiry { get; set; }
    public bool LoginAsMainDevice { get; set; }
}
