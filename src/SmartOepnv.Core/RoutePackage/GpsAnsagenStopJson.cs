using System.Text.Json.Nodes;



namespace SmartOepnv.Core.RoutePackage;



/// <summary>Haltestellen-JSON wie <c>RouteDistributionManager</c> (GPSAnsagen-App).</summary>

public static class GpsAnsagenStopJson

{

    public static RouteStopItem Parse(JsonObject obj, string routeName)

    {

        return new RouteStopItem

        {

            PlannerStopCode = PlannerStopCode.Normalize(

                JsonNodeReading.GetString(obj["plannerStopCode"], JsonNodeReading.GetString(obj["stopCode"]))),

            Name = JsonNodeReading.GetString(obj["name"]),

            RouteName = JsonNodeReading.GetString(obj["routeName"], routeName),

            GpsCoordinates = JsonNodeReading.GetString(obj["gpsCoordinates"]),

            StopCoordinates = JsonNodeReading.GetString(obj["stopCoordinates"]),

            Radius = JsonNodeReading.GetInt32(obj["radius"], 50),

            VrrStopId = JsonNodeReading.GetString(obj["vrrStopId"]),

            StopDisplay = JsonNodeReading.GetString(obj["stopDisplay"]),

            Time = JsonNodeReading.GetString(obj["time"]),

            IsWaypoint = JsonNodeReading.GetBoolean(obj["isWaypoint"]),

            WaypointName = JsonNodeReading.GetString(obj["waypointName"]),

            IsAnnouncementEnabled = JsonNodeReading.GetBoolean(obj["isAnnouncementEnabled"], defaultValue: true),

            EmbeddedSoundFileName = JsonNodeReading.GetString(obj["embeddedSoundFileName"]),

            Destination = JsonNodeReading.GetString(obj["destination"]),

            LineNumber = JsonNodeReading.GetString(obj["lineNumber"]),

            EndDestination = JsonNodeReading.GetString(obj["endDestination"]),

            IsEndStop = JsonNodeReading.GetBoolean(obj["isEndStop"]),

            RouteChangeEnabled = JsonNodeReading.GetBoolean(obj["routeChangeEnabled"]),

            SelectedLineCourseTrip = JsonNodeReading.GetString(obj["selectedLineCourseTrip"]),

            EndDestinationCoordinates = JsonNodeReading.GetString(obj["endDestinationCoordinates"]),

            IsDisplayEnabled = JsonNodeReading.GetBoolean(obj["isDisplayEnabled"]),

            DisplayText = JsonNodeReading.GetString(obj["displayText"]),

            DisplayText2 = JsonNodeReading.GetString(obj["displayText2"]),

            DisplayText3 = JsonNodeReading.GetString(obj["displayText3"]),

            UseDisplayText2 = JsonNodeReading.GetBoolean(obj["useDisplayText2"]),

            UseDisplayText3 = JsonNodeReading.GetBoolean(obj["useDisplayText3"]),

            DisplayInterval = JsonNodeReading.GetInt32(obj["displayInterval"], 5),

            NextStop = JsonNodeReading.GetString(obj["nextStop"]),

            Abstand = JsonNodeReading.GetInt32(obj["abstand"])

        };

    }



    public static JsonObject Write(RouteStopItem stop, string routeName)

    {

        return new JsonObject

        {

            ["name"] = stop.Name,

            ["routeName"] = routeName,

            ["gpsCoordinates"] = stop.GpsCoordinates,

            ["stopCoordinates"] = stop.StopCoordinates,

            ["radius"] = stop.Radius,

            ["isAnnouncementEnabled"] = stop.IsAnnouncementEnabled,

            ["time"] = stop.Time,

            ["isWaypoint"] = stop.IsWaypoint,

            ["waypointName"] = stop.WaypointName,

            ["embeddedSoundFileName"] = stop.EmbeddedSoundFileName,

            ["stopDisplay"] = stop.StopDisplay,

            ["vrrStopId"] = stop.VrrStopId,

            ["isDisplayEnabled"] = stop.IsDisplayEnabled,

            ["displayText"] = stop.DisplayText,

            ["displayText2"] = stop.DisplayText2,

            ["displayText3"] = stop.DisplayText3,

            ["useDisplayText2"] = stop.UseDisplayText2,

            ["useDisplayText3"] = stop.UseDisplayText3,

            ["displayInterval"] = stop.DisplayInterval,

            ["nextStop"] = stop.NextStop,

            ["abstand"] = stop.Abstand,

            ["destination"] = stop.Destination,

            ["lineNumber"] = stop.LineNumber,

            ["endDestination"] = stop.EndDestination,

            ["isEndStop"] = stop.IsEndStop,

            ["routeChangeEnabled"] = stop.RouteChangeEnabled,

            ["selectedLineCourseTrip"] = stop.SelectedLineCourseTrip,

            ["endDestinationCoordinates"] = stop.EndDestinationCoordinates

        };

    }

}


