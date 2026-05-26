namespace SmartOepnv.Core.RoutePackage;



public sealed class RouteStopItem

{

    /// <summary>5-stellige Planer-ID (JSON: plannerStopCode), nicht in App-ITCS sichtbar.</summary>

    public string PlannerStopCode { get; set; } = string.Empty;



    public string Name { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    public string GpsCoordinates { get; set; } = string.Empty;

    public string StopCoordinates { get; set; } = string.Empty;

    public int Radius { get; set; } = 50;

    public string VrrStopId { get; set; } = string.Empty;

    public string StopDisplay { get; set; } = string.Empty;

    public string Time { get; set; } = string.Empty;

    public bool IsWaypoint { get; set; }

    public string WaypointName { get; set; } = string.Empty;

    public bool IsAnnouncementEnabled { get; set; } = true;

    public string EmbeddedSoundFileName { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    public string LineNumber { get; set; } = string.Empty;

    public string EndDestination { get; set; } = string.Empty;

    public bool IsEndStop { get; set; }

    public bool RouteChangeEnabled { get; set; }

    public string SelectedLineCourseTrip { get; set; } = string.Empty;

    public string EndDestinationCoordinates { get; set; } = string.Empty;

    public bool IsDisplayEnabled { get; set; }

    public string DisplayText { get; set; } = string.Empty;

    public string DisplayText2 { get; set; } = string.Empty;

    public string DisplayText3 { get; set; } = string.Empty;

    public bool UseDisplayText2 { get; set; }

    public bool UseDisplayText3 { get; set; }

    public int DisplayInterval { get; set; } = 5;

    public string NextStop { get; set; } = string.Empty;

    public int Abstand { get; set; }

}

