using System.Globalization;
using System.Text;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.Core.Realtime.Vdv454;

/// <summary>
/// Mappt Leitstellen-Live (<see cref="VehicleLiveState"/>) auf VDV-454-Zwischenobjekte.
/// Mit optionalem <see cref="EditableRoutePackage"/>: Aimed-Zeiten aus Planer-Haltzeiten.
/// </summary>
public static class Vdv454LiveMapper
{
    public static Vdv454ExportSnapshot FromLiveVehicles(
        IEnumerable<VehicleLiveState> vehicles,
        Vdv454ParticipantConfig? config = null,
        DateTimeOffset? producedAt = null,
        EditableRoutePackage? schedulePackage = null)
    {
        var cfg = config ?? Vdv454ParticipantConfig.CreateDefault();
        var at = producedAt ?? DateTimeOffset.UtcNow;
        var list = vehicles
            .Where(v => v.Status is VehicleOnlineStatus.Online or VehicleOnlineStatus.Stale)
            .Select(v => MapOne(v, cfg, schedulePackage))
            .ToList();
        return new Vdv454ExportSnapshot
        {
            Config = cfg,
            ProducedAt = at,
            Activities = list
        };
    }

    public static Vdv454VehicleActivity MapOne(
        VehicleLiveState v,
        Vdv454ParticipantConfig config,
        EditableRoutePackage? schedulePackage = null)
    {
        var lineCourse = NormalizeLineCourse(v.LineCourse);
        var vehicleRef = BuildVehicleRef(config, v);
        var lineRef = $"{config.LocalCodespace}:Line:{lineCourse}";
        var journeyRef = BuildJourneyRef(config, v, lineCourse);
        var recorded = DateTimeOffset.FromUnixTimeMilliseconds(v.TimestampEpochMs);

        JourneyPatternAndCalls(v, config, schedulePackage, recorded,
            out var journeyPatternRef,
            out var monitored,
            out var estimatedCalls,
            out var matchedRouteKey);

        if (string.IsNullOrWhiteSpace(journeyPatternRef) && !string.IsNullOrWhiteSpace(v.RouteName))
        {
            journeyPatternRef = $"{config.LocalCodespace}:Pattern:{Sanitize(v.RouteName!)}";
        }
        else if (!string.IsNullOrWhiteSpace(matchedRouteKey))
        {
            journeyPatternRef ??= $"{config.LocalCodespace}:Pattern:{Sanitize(matchedRouteKey)}";
        }

        return new Vdv454VehicleActivity
        {
            VehicleRef = vehicleRef,
            LineRef = lineRef,
            OperatorRef = string.IsNullOrWhiteSpace(config.OperatorRef) ? null : config.OperatorRef,
            DatedVehicleJourneyRef = journeyRef,
            JourneyPatternRef = journeyPatternRef,
            DirectionRef = null,
            DestinationName = v.Destination,
            Latitude = v.Latitude,
            Longitude = v.Longitude,
            DelaySeconds = v.DelaySeconds,
            SpeedKmh = v.SpeedKmh,
            ProgressStatus = v.Status == VehicleOnlineStatus.Online ? "running" : "stale",
            RecordedAtTime = recorded,
            MonitoredCall = monitored,
            EstimatedCalls = estimatedCalls
        };
    }

    private static void JourneyPatternAndCalls(
        VehicleLiveState v,
        Vdv454ParticipantConfig config,
        EditableRoutePackage? schedulePackage,
        DateTimeOffset recorded,
        out string? journeyPatternRef,
        out Vdv454EstimatedCall? monitored,
        out IReadOnlyList<Vdv454EstimatedCall> estimatedCalls,
        out string? matchedRouteKey)
    {
        journeyPatternRef = null;
        matchedRouteKey = null;
        monitored = null;
        estimatedCalls = [];

        if (schedulePackage is not null)
        {
            var schedule = Vdv454ScheduleResolver.TryResolve(v, schedulePackage, config);
            if (schedule is not null && schedule.Calls.Count > 0)
            {
                matchedRouteKey = schedule.RouteKey;
                estimatedCalls = schedule.Calls;
                monitored = schedule.MonitoredCall ?? schedule.Calls[0];
                return;
            }
        }

        // Fallback ohne Fahrplan: nur aktueller Halt, Expected grob aus Delay
        var stopRef = BuildStopPointRef(config, v);
        if (string.IsNullOrWhiteSpace(stopRef) && string.IsNullOrWhiteSpace(v.StopName))
        {
            return;
        }

        var expected = v.DelaySeconds is int d
            ? recorded.AddSeconds(-d)
            : (DateTimeOffset?)null;
        var call = new Vdv454EstimatedCall
        {
            StopPointRef = stopRef ?? $"{config.LocalCodespace}:Stop:UNKNOWN",
            StopPointName = v.StopName,
            Order = null,
            ExpectedArrival = expected,
            ExpectedDeparture = expected,
            VehicleAtStop = null
        };
        monitored = call;
        estimatedCalls = [call];
    }

    public static string BuildVehicleRef(Vdv454ParticipantConfig config, VehicleLiveState v)
    {
        var key = !string.IsNullOrWhiteSpace(v.PhoneNumber)
            ? Sanitize(v.PhoneNumber!)
            : Sanitize(v.Id);
        return $"{config.LocalCodespace}:Vehicle:{key}";
    }

    public static string? BuildStopPointRef(Vdv454ParticipantConfig config, VehicleLiveState v)
    {
        if (!string.IsNullOrWhiteSpace(v.VrrStopId) &&
            !string.IsNullOrWhiteSpace(config.ExternalStopCodespace))
        {
            return $"{config.ExternalStopCodespace}:{Sanitize(v.VrrStopId!)}";
        }

        if (!string.IsNullOrWhiteSpace(v.PlannerStopCode))
        {
            return $"{config.LocalCodespace}:Stop:{Sanitize(v.PlannerStopCode!)}";
        }

        if (!string.IsNullOrWhiteSpace(v.StopName))
        {
            return $"{config.LocalCodespace}:StopName:{Sanitize(v.StopName!)}";
        }

        return null;
    }

    private static string BuildJourneyRef(Vdv454ParticipantConfig config, VehicleLiveState v, string lineCourse)
    {
        if (!string.IsNullOrWhiteSpace(v.TripNumber))
        {
            return $"{config.LocalCodespace}:Journey:{lineCourse}:{Sanitize(v.TripNumber!)}";
        }

        var day = DateTimeOffset.FromUnixTimeMilliseconds(v.TimestampEpochMs).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var veh = !string.IsNullOrWhiteSpace(v.PhoneNumber) ? Sanitize(v.PhoneNumber!) : Sanitize(v.Id);
        return $"{config.LocalCodespace}:Journey:{lineCourse}:{day}:{veh}";
    }

    private static string NormalizeLineCourse(string? lineCourse)
    {
        var normalized = RouteDisplayHelper.NormalizeLineCourse(lineCourse);
        if (string.IsNullOrEmpty(normalized) || normalized == "000/00")
            return "UNKNOWN";
        return Sanitize(normalized);
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/')
                sb.Append(c);
            else if (char.IsWhiteSpace(c))
                sb.Append('_');
        }
        return sb.Length == 0 ? "X" : sb.ToString();
    }
}
