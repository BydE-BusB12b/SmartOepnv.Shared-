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
    /// <summary>Stabile ID zum DS021T-Ziel (Außenanzeige).</summary>
    public string DestinationId { get; set; } = string.Empty;

    public string Ds021NeuDestination { get; set; } = string.Empty;
    public string Ds021NeuDestinationId { get; set; } = string.Empty;

    public string FmaS1Destination { get; set; } = string.Empty;
    public string FmaS1DestinationId { get; set; } = string.Empty;

    public string Ds003aDestination { get; set; } = string.Empty;
    public string Ds003aDestinationId { get; set; } = string.Empty;

    public string ZielnummerDestination { get; set; } = string.Empty;
    public string ZielnummerDestinationId { get; set; } = string.Empty;

    public string LineNumber { get; set; } = string.Empty;

    public string EndDestination { get; set; } = string.Empty;
    public string EndDestinationId { get; set; } = string.Empty;

    public string Ds021NeuEndDestination { get; set; } = string.Empty;
    public string Ds021NeuEndDestinationId { get; set; } = string.Empty;

    public string FmaS1EndDestination { get; set; } = string.Empty;
    public string FmaS1EndDestinationId { get; set; } = string.Empty;

    public string Ds003aEndDestination { get; set; } = string.Empty;
    public string Ds003aEndDestinationId { get; set; } = string.Empty;

    public string ZielnummerEndDestination { get; set; } = string.Empty;
    public string ZielnummerEndDestinationId { get; set; } = string.Empty;

    public bool IsEndStop { get; set; }
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
        DestinationId = DestinationId,
        Ds021NeuDestination = Ds021NeuDestination,
        Ds021NeuDestinationId = Ds021NeuDestinationId,
        FmaS1Destination = FmaS1Destination,
        FmaS1DestinationId = FmaS1DestinationId,
        Ds003aDestination = Ds003aDestination,
        Ds003aDestinationId = Ds003aDestinationId,
        ZielnummerDestination = ZielnummerDestination,
        ZielnummerDestinationId = ZielnummerDestinationId,
        LineNumber = LineNumber,
        EndDestination = EndDestination,
        EndDestinationId = EndDestinationId,
        Ds021NeuEndDestination = Ds021NeuEndDestination,
        Ds021NeuEndDestinationId = Ds021NeuEndDestinationId,
        FmaS1EndDestination = FmaS1EndDestination,
        FmaS1EndDestinationId = FmaS1EndDestinationId,
        Ds003aEndDestination = Ds003aEndDestination,
        Ds003aEndDestinationId = Ds003aEndDestinationId,
        ZielnummerEndDestination = ZielnummerEndDestination,
        ZielnummerEndDestinationId = ZielnummerEndDestinationId,
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
        DestinationId = other.DestinationId;
        Ds021NeuDestination = other.Ds021NeuDestination;
        Ds021NeuDestinationId = other.Ds021NeuDestinationId;
        FmaS1Destination = other.FmaS1Destination;
        FmaS1DestinationId = other.FmaS1DestinationId;
        Ds003aDestination = other.Ds003aDestination;
        Ds003aDestinationId = other.Ds003aDestinationId;
        ZielnummerDestination = other.ZielnummerDestination;
        ZielnummerDestinationId = other.ZielnummerDestinationId;
        LineNumber = other.LineNumber;
        EndDestination = other.EndDestination;
        EndDestinationId = other.EndDestinationId;
        Ds021NeuEndDestination = other.Ds021NeuEndDestination;
        Ds021NeuEndDestinationId = other.Ds021NeuEndDestinationId;
        FmaS1EndDestination = other.FmaS1EndDestination;
        FmaS1EndDestinationId = other.FmaS1EndDestinationId;
        Ds003aEndDestination = other.Ds003aEndDestination;
        Ds003aEndDestinationId = other.Ds003aEndDestinationId;
        ZielnummerEndDestination = other.ZielnummerEndDestination;
        ZielnummerEndDestinationId = other.ZielnummerEndDestinationId;
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
