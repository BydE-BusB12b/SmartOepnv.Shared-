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
            DestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["destinationId"])),
            Ds021NeuDestination = ReadProtocolDestination(obj, "ds021NeuDestination", "destination"),
            Ds021NeuDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["ds021NeuDestinationId"])),
            FmaS1Destination = ReadProtocolDestination(obj, "fmaS1Destination", "destination"),
            FmaS1DestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["fmaS1DestinationId"])),
            Ds003aDestination = JsonNodeReading.GetString(obj["ds003aDestination"]),
            Ds003aDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["ds003aDestinationId"])),
            ZielnummerDestination = ReadProtocolDestination(obj, "zielnummerDestination", "destination"),
            ZielnummerDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["zielnummerDestinationId"])),
            LineNumber = JsonNodeReading.GetString(obj["lineNumber"]),
            EndDestination = JsonNodeReading.GetString(obj["endDestination"]),
            EndDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["endDestinationId"])),
            Ds021NeuEndDestination = ReadProtocolDestination(obj, "ds021NeuEndDestination", "endDestination"),
            Ds021NeuEndDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["ds021NeuEndDestinationId"])),
            FmaS1EndDestination = ReadProtocolDestination(obj, "fmaS1EndDestination", "endDestination"),
            FmaS1EndDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["fmaS1EndDestinationId"])),
            Ds003aEndDestination = JsonNodeReading.GetString(obj["ds003aEndDestination"]),
            Ds003aEndDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["ds003aEndDestinationId"])),
            ZielnummerEndDestination = ReadProtocolDestination(obj, "zielnummerEndDestination", "endDestination"),
            ZielnummerEndDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["zielnummerEndDestinationId"])),
            IsEndStop = JsonNodeReading.GetBoolean(obj["isEndStop"]),
            PlayEndStopAnnouncement = obj["playEndStopAnnouncement"] is not null
                ? JsonNodeReading.GetBoolean(obj["playEndStopAnnouncement"])
                : JsonNodeReading.GetBoolean(obj["isEndStop"]),
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
        var obj = new JsonObject
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
            ["destinationId"] = stop.DestinationId,
            ["ds021NeuDestination"] = stop.Ds021NeuDestination,
            ["ds021NeuDestinationId"] = stop.Ds021NeuDestinationId,
            ["fmaS1Destination"] = stop.FmaS1Destination,
            ["fmaS1DestinationId"] = stop.FmaS1DestinationId,
            ["ds003aDestination"] = stop.Ds003aDestination,
            ["ds003aDestinationId"] = stop.Ds003aDestinationId,
            ["zielnummerDestination"] = stop.ZielnummerDestination,
            ["zielnummerDestinationId"] = stop.ZielnummerDestinationId,
            ["lineNumber"] = stop.LineNumber,
            ["endDestination"] = stop.EndDestination,
            ["endDestinationId"] = stop.EndDestinationId,
            ["ds021NeuEndDestination"] = stop.Ds021NeuEndDestination,
            ["ds021NeuEndDestinationId"] = stop.Ds021NeuEndDestinationId,
            ["fmaS1EndDestination"] = stop.FmaS1EndDestination,
            ["fmaS1EndDestinationId"] = stop.FmaS1EndDestinationId,
            ["ds003aEndDestination"] = stop.Ds003aEndDestination,
            ["ds003aEndDestinationId"] = stop.Ds003aEndDestinationId,
            ["zielnummerEndDestination"] = stop.ZielnummerEndDestination,
            ["zielnummerEndDestinationId"] = stop.ZielnummerEndDestinationId,
            ["isEndStop"] = stop.IsEndStop,
            ["playEndStopAnnouncement"] = stop.PlayEndStopAnnouncement,
            ["routeChangeEnabled"] = stop.RouteChangeEnabled,
            ["selectedLineCourseTrip"] = stop.SelectedLineCourseTrip,
            ["endDestinationCoordinates"] = stop.EndDestinationCoordinates
        };

        var plannerCode = PlannerStopCode.Normalize(stop.PlannerStopCode);
        if (!string.IsNullOrEmpty(plannerCode))
        {
            obj["plannerStopCode"] = plannerCode;
            obj["stopCode"] = plannerCode;
        }

        return obj;
    }

    /// <summary>Protokoll-Ziel aus JSON; fehlender Schlüssel fällt auf das Legacy-Feld zurück.</summary>
    private static string ReadProtocolDestination(JsonObject obj, string key, string legacyKey)
    {
        if (obj[key] is null)
        {
            return JsonNodeReading.GetString(obj[legacyKey]);
        }

        return JsonNodeReading.GetString(obj[key]);
    }
}
