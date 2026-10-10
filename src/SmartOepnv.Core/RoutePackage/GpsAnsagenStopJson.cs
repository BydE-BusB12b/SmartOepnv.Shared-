using System.Text.Json.Nodes;
using SmartOepnv.Core.Dienstvorlagen;

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
            IsAnnouncementEnabled = JsonNodeReading.GetBoolean(obj["zielwechselEnabled"])
                || JsonNodeReading.GetBoolean(obj["isAnnouncementEnabled"], defaultValue: true),
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
            MobitecDestination = ReadProtocolDestination(obj, "mobitecDestination", "destination"),
            MobitecDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["mobitecDestinationId"])),
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
            MobitecEndDestination = ReadProtocolDestination(obj, "mobitecEndDestination", "endDestination"),
            MobitecEndDestinationId = OutsideDisplayId.Normalize(JsonNodeReading.GetString(obj["mobitecEndDestinationId"])),
            IsEndStop = JsonNodeReading.GetBoolean(obj["isEndStop"]),
            PlayEndStopAnnouncementEn = ReadPlayEndStopAnnouncementEn(obj),
            PlayEndStopAnnouncementNl = JsonNodeReading.GetBoolean(obj["playEndStopAnnouncementNl"]),
            PlayStartStopGreeting = JsonNodeReading.GetBoolean(obj["playStartStopGreeting"]),
            StartStopGreetingCoordinates = JsonNodeReading.GetString(obj["startStopGreetingCoordinates"]),
            RouteChangeEnabled = JsonNodeReading.GetBoolean(obj["routeChangeEnabled"]),
            SelectedLineCourseTrip = JsonNodeReading.GetString(obj["selectedLineCourseTrip"]),
            RouteChangeTargetsByDate = ReadRouteChangeTargetsByDate(obj["routeChangeTargetsByDate"]),
            EndDestinationCoordinates = JsonNodeReading.GetString(obj["endDestinationCoordinates"]),
            StopHintEnabled = JsonNodeReading.GetBoolean(obj["stopHintEnabled"]),
            StopHintText = JsonNodeReading.GetString(obj["stopHintText"]),
            StopHintTriggerMode = RouteStopHintTrigger.Normalize(
                JsonNodeReading.GetString(obj["stopHintTriggerMode"])),
            StopHintGpsCoordinates = JsonNodeReading.GetString(obj["stopHintGpsCoordinates"]),
            StopHintRadius = JsonNodeReading.GetInt32(obj["stopHintRadius"], 40),
            EntwerterEnabled = JsonNodeReading.GetBoolean(obj["entwerterEnabled"]),
            EntwerterCode = JsonNodeReading.GetString(obj["entwerterCode"]),
            ZielwechselEnabled = JsonNodeReading.GetBoolean(obj["zielwechselEnabled"]),
            ZielwechselGpsCoordinates = JsonNodeReading.GetString(obj["zielwechselGpsCoordinates"]),
            ZielwechselRadius = JsonNodeReading.GetInt32(obj["zielwechselRadius"], 40),
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
            // Zielwechsel ≠ Starthaltestelle: immer ansagen (auch wenn ältere Daten false hatten).
            ["isAnnouncementEnabled"] = stop.ZielwechselEnabled || stop.IsAnnouncementEnabled,
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
            ["mobitecDestination"] = stop.MobitecDestination,
            ["mobitecDestinationId"] = stop.MobitecDestinationId,
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
            ["mobitecEndDestination"] = stop.MobitecEndDestination,
            ["mobitecEndDestinationId"] = stop.MobitecEndDestinationId,
            ["isEndStop"] = stop.IsEndStop,
            ["playEndStopAnnouncement"] = stop.PlayEndStopAnnouncement,
            ["playEndStopAnnouncementEn"] = stop.PlayEndStopAnnouncementEn,
            ["playEndStopAnnouncementNl"] = stop.PlayEndStopAnnouncementNl,
            ["playStartStopGreeting"] = stop.PlayStartStopGreeting,
            ["startStopGreetingCoordinates"] = stop.StartStopGreetingCoordinates,
            ["routeChangeEnabled"] = stop.RouteChangeEnabled,
            ["selectedLineCourseTrip"] = stop.SelectedLineCourseTrip,
            ["endDestinationCoordinates"] = stop.EndDestinationCoordinates
        };

        if (stop.StopHintEnabled || !string.IsNullOrWhiteSpace(stop.StopHintText))
        {
            obj["stopHintEnabled"] = stop.StopHintEnabled;
            obj["stopHintText"] = stop.StopHintText;
            obj["stopHintTriggerMode"] = RouteStopHintTrigger.Normalize(stop.StopHintTriggerMode);
            obj["stopHintGpsCoordinates"] = stop.StopHintGpsCoordinates;
            obj["stopHintRadius"] = stop.StopHintRadius > 0 ? stop.StopHintRadius : 40;
        }

        if (stop.EntwerterEnabled || !string.IsNullOrWhiteSpace(stop.EntwerterCode))
        {
            obj["entwerterEnabled"] = stop.EntwerterEnabled;
            obj["entwerterCode"] = (stop.EntwerterCode ?? string.Empty).Trim();
        }

        obj["zielwechselEnabled"] = stop.ZielwechselEnabled;
        obj["zielwechselGpsCoordinates"] = stop.ZielwechselGpsCoordinates ?? string.Empty;
        obj["zielwechselRadius"] = stop.ZielwechselRadius > 0 ? stop.ZielwechselRadius : 40;

        WriteRouteChangeTargetsByDate(obj, stop.RouteChangeTargetsByDate);

        var plannerCode = PlannerStopCode.Normalize(stop.PlannerStopCode);
        if (!string.IsNullOrEmpty(plannerCode))
        {
            obj["plannerStopCode"] = plannerCode;
            obj["stopCode"] = plannerCode;
        }

        return obj;
    }

    /// <summary>
    /// EN-Flag; fehlende neuen Felder → bisherige Endansage zählt als Englisch
    /// (alle bestehenden Endhaltestellen-Ansagen sind EN).
    /// </summary>
    private static bool ReadPlayEndStopAnnouncementEn(JsonObject obj)
    {
        if (obj["playEndStopAnnouncementEn"] is not null)
        {
            return JsonNodeReading.GetBoolean(obj["playEndStopAnnouncementEn"]);
        }

        var legacy = obj["playEndStopAnnouncement"] is not null
            ? JsonNodeReading.GetBoolean(obj["playEndStopAnnouncement"])
            : JsonNodeReading.GetBoolean(obj["isEndStop"]);
        // Nur migrieren, wenn kein NL-Feld gesetzt ist (sonst reine NL-Ansage).
        if (obj["playEndStopAnnouncementNl"] is not null &&
            JsonNodeReading.GetBoolean(obj["playEndStopAnnouncementNl"]) &&
            !legacy)
        {
            return false;
        }

        return legacy;
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

    private static List<RouteChangeTargetEntry> ReadRouteChangeTargetsByDate(JsonNode? node)
    {
        var result = new List<RouteChangeTargetEntry>();
        if (node is not JsonArray arr)
        {
            return result;
        }

        foreach (var item in arr)
        {
            if (item is not JsonObject entryObj)
            {
                continue;
            }

            var trip = JsonNodeReading.GetString(entryObj["selectedLineCourseTrip"]);
            var dates = new List<DateOnly>();
            if (entryObj["operatingDates"] is JsonArray datesArr)
            {
                foreach (var dateNode in datesArr)
                {
                    var raw = dateNode?.GetValue<string>();
                    if (RouteOperatingDatesEditor.TryParseDate(raw, out var date))
                    {
                        dates.Add(date);
                    }
                }
            }

            var days = new List<DutyOperatingDay>();
            if (entryObj["operatingDays"] is JsonArray daysArr)
            {
                foreach (var dayNode in daysArr)
                {
                    var raw = dayNode?.GetValue<string>();
                    if (RouteOperatingDaysEditor.TryParseDayId(raw, out var day))
                    {
                        days.Add(day);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(trip) || (dates.Count == 0 && days.Count == 0))
            {
                continue;
            }

            result.Add(new RouteChangeTargetEntry
            {
                SelectedLineCourseTrip = trip.Trim(),
                OperatingDates = dates.Distinct().OrderBy(d => d).ToList(),
                OperatingDays = days.Distinct().OrderBy(d => (int)d).ToList()
            });
        }

        return result;
    }

    private static void WriteRouteChangeTargetsByDate(JsonObject obj, IReadOnlyList<RouteChangeTargetEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var arr = new JsonArray();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.SelectedLineCourseTrip) || !entry.HasScheduleConstraint)
            {
                continue;
            }

            var entryObj = new JsonObject
            {
                ["selectedLineCourseTrip"] = entry.SelectedLineCourseTrip.Trim()
            };

            if (entry.OperatingDates.Count > 0)
            {
                var datesArr = new JsonArray();
                foreach (var date in entry.OperatingDates.Distinct().OrderBy(d => d))
                {
                    datesArr.Add(RouteDateRange.FormatDate(date));
                }

                entryObj["operatingDates"] = datesArr;
            }

            if (entry.OperatingDays.Count > 0)
            {
                var daysArr = new JsonArray();
                foreach (var day in entry.OperatingDays.Distinct().OrderBy(d => (int)d))
                {
                    daysArr.Add(RouteOperatingDaysEditor.ToDayId(day));
                }

                entryObj["operatingDays"] = daysArr;
            }

            arr.Add(entryObj);
        }

        if (arr.Count > 0)
        {
            obj["routeChangeTargetsByDate"] = arr;
        }
    }
}
