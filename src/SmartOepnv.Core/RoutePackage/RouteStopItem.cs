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

    /// <summary>Starthaltestellen-Ziel DS003a Krefeld (nur Handy/Planer, optional in JSON).</summary>
    public string Ds003aDestination { get; set; } = string.Empty;

    public string LineNumber { get; set; } = string.Empty;

    public string EndDestination { get; set; } = string.Empty;

    /// <summary>Endhaltestellen-Ziel DS003a Krefeld.</summary>
    public string Ds003aEndDestination { get; set; } = string.Empty;

    public bool IsEndStop { get; set; }

    /// <summary>Endhaltestellen-Ansage aus der Kartei an die Haltestellenansage anhängen (nur wenn <see cref="IsEndStop"/>).</summary>
    public bool PlayEndStopAnnouncement { get; set; }

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

    public RouteStopItem Clone() => new()
    {
        PlannerStopCode = PlannerStopCode,
        Name = Name,
        RouteName = RouteName,
        GpsCoordinates = GpsCoordinates,
        StopCoordinates = StopCoordinates,
        Radius = Radius,
        VrrStopId = VrrStopId,
        StopDisplay = StopDisplay,
        Time = Time,
        IsWaypoint = IsWaypoint,
        WaypointName = WaypointName,
        IsAnnouncementEnabled = IsAnnouncementEnabled,
        EmbeddedSoundFileName = EmbeddedSoundFileName,
        Destination = Destination,
        Ds003aDestination = Ds003aDestination,
        LineNumber = LineNumber,
        EndDestination = EndDestination,
        Ds003aEndDestination = Ds003aEndDestination,
        IsEndStop = IsEndStop,
        PlayEndStopAnnouncement = PlayEndStopAnnouncement,
        RouteChangeEnabled = RouteChangeEnabled,
        SelectedLineCourseTrip = SelectedLineCourseTrip,
        EndDestinationCoordinates = EndDestinationCoordinates,
        IsDisplayEnabled = IsDisplayEnabled,
        DisplayText = DisplayText,
        DisplayText2 = DisplayText2,
        DisplayText3 = DisplayText3,
        UseDisplayText2 = UseDisplayText2,
        UseDisplayText3 = UseDisplayText3,
        DisplayInterval = DisplayInterval,
        NextStop = NextStop,
        Abstand = Abstand
    };

    public void CopyFrom(RouteStopItem other)
    {
        PlannerStopCode = other.PlannerStopCode;
        Name = other.Name;
        RouteName = other.RouteName;
        GpsCoordinates = other.GpsCoordinates;
        StopCoordinates = other.StopCoordinates;
        Radius = other.Radius;
        VrrStopId = other.VrrStopId;
        StopDisplay = other.StopDisplay;
        Time = other.Time;
        IsWaypoint = other.IsWaypoint;
        WaypointName = other.WaypointName;
        IsAnnouncementEnabled = other.IsAnnouncementEnabled;
        EmbeddedSoundFileName = other.EmbeddedSoundFileName;
        Destination = other.Destination;
        Ds003aDestination = other.Ds003aDestination;
        LineNumber = other.LineNumber;
        EndDestination = other.EndDestination;
        Ds003aEndDestination = other.Ds003aEndDestination;
        IsEndStop = other.IsEndStop;
        PlayEndStopAnnouncement = other.PlayEndStopAnnouncement;
        RouteChangeEnabled = other.RouteChangeEnabled;
        SelectedLineCourseTrip = other.SelectedLineCourseTrip;
        EndDestinationCoordinates = other.EndDestinationCoordinates;
        IsDisplayEnabled = other.IsDisplayEnabled;
        DisplayText = other.DisplayText;
        DisplayText2 = other.DisplayText2;
        DisplayText3 = other.DisplayText3;
        UseDisplayText2 = other.UseDisplayText2;
        UseDisplayText3 = other.UseDisplayText3;
        DisplayInterval = other.DisplayInterval;
        NextStop = other.NextStop;
        Abstand = other.Abstand;
    }
}

