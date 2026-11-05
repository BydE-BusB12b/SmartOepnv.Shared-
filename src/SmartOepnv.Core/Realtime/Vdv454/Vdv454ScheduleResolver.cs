using System.Globalization;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.Core.Realtime.Vdv454;

/// <summary>
/// Löst Aimed-Zeiten (Soll) aus dem Planer-Routenpaket für eine Live-Fahrt.
/// Quelle: <see cref="RouteStopItem.Time"/> (HH:mm) je Halt – Ankunft/Abfahrt gleich gesetzt.
/// </summary>
public static class Vdv454ScheduleResolver
{
    private static readonly TimeZoneInfo BerlinTz = ResolveBerlinTimeZone();

    public sealed class JourneySchedule
    {
        public required string RouteKey { get; init; }
        public required IReadOnlyList<Vdv454EstimatedCall> Calls { get; init; }
        public Vdv454EstimatedCall? MonitoredCall { get; init; }
    }

    public static JourneySchedule? TryResolve(
        VehicleLiveState vehicle,
        EditableRoutePackage editor,
        Vdv454ParticipantConfig config)
    {
        var serviceDate = ServiceDateBerlin(vehicle.TimestampEpochMs);
        var routeKey = TryResolveRouteKey(vehicle, editor, serviceDate);
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return null;
        }

        var stops = editor.GetStops(routeKey)
            .Where(s => !s.IsWaypoint)
            .Where(s => TryParseClock(s.Time, out _))
            .ToList();
        if (stops.Count == 0)
        {
            return null;
        }

        var delaySec = vehicle.DelaySeconds;
        DateTime? previousPlannedLocal = null;
        var dayOffset = 0;
        var calls = new List<Vdv454EstimatedCall>(stops.Count);

        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            _ = TryParseClock(stop.Time, out var clock);
            var plannedLocal = BuildPlannedLocal(serviceDate, clock, ref dayOffset, ref previousPlannedLocal);
            var aimed = new DateTimeOffset(plannedLocal, BerlinTz.GetUtcOffset(plannedLocal));
            DateTimeOffset? expected = delaySec is int d
                ? aimed.AddSeconds(-d)
                : null;

            var stopRef = BuildStopRef(config, stop);
            calls.Add(new Vdv454EstimatedCall
            {
                StopPointRef = stopRef,
                StopPointName = string.IsNullOrWhiteSpace(stop.Name) ? null : stop.Name.Trim(),
                Order = i + 1,
                AimedArrival = aimed,
                AimedDeparture = aimed,
                ExpectedArrival = expected,
                ExpectedDeparture = expected,
                VehicleAtStop = null
            });
        }

        return new JourneySchedule
        {
            RouteKey = routeKey,
            Calls = calls,
            MonitoredCall = PickMonitoredCall(calls, vehicle)
        };
    }

    private static Vdv454EstimatedCall? PickMonitoredCall(
        IReadOnlyList<Vdv454EstimatedCall> calls,
        VehicleLiveState vehicle)
    {
        if (calls.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(vehicle.PlannerStopCode))
        {
            var code = vehicle.PlannerStopCode.Trim();
            var byCode = calls.FirstOrDefault(c =>
                c.StopPointRef.EndsWith(":" + code, StringComparison.OrdinalIgnoreCase) ||
                c.StopPointRef.EndsWith(":" + SanitizeToken(code), StringComparison.OrdinalIgnoreCase));
            if (byCode is not null)
            {
                return byCode;
            }
        }

        if (!string.IsNullOrWhiteSpace(vehicle.StopName))
        {
            var name = vehicle.StopName.Trim();
            var byName = calls.FirstOrDefault(c =>
                string.Equals(c.StopPointName, name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        // Nächster Halt nach Expected/Aimed relativ zur Aufnahmezeit
        var now = DateTimeOffset.FromUnixTimeMilliseconds(vehicle.TimestampEpochMs);
        return calls
            .Where(c => (c.ExpectedArrival ?? c.AimedArrival) is DateTimeOffset t && t >= now.AddMinutes(-2))
            .OrderBy(c => c.ExpectedArrival ?? c.AimedArrival)
            .FirstOrDefault()
            ?? calls[^1];
    }

    private static string BuildStopRef(Vdv454ParticipantConfig config, RouteStopItem stop)
    {
        var code = stop.PlannerStopCode?.Trim();
        if (!string.IsNullOrEmpty(code))
        {
            return $"{config.LocalCodespace}:Stop:{SanitizeToken(code)}";
        }

        var name = stop.Name?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            return $"{config.LocalCodespace}:StopName:{SanitizeToken(name)}";
        }

        return $"{config.LocalCodespace}:Stop:UNKNOWN";
    }

    private static string? TryResolveRouteKey(
        VehicleLiveState vehicle,
        EditableRoutePackage editor,
        DateOnly serviceDate)
    {
        var fromRouteName = RouteDisplayHelper.Parse(vehicle.RouteName ?? string.Empty);
        var line = RouteDisplayHelper.NormalizeLineCourse(
            !string.IsNullOrWhiteSpace(vehicle.LineCourse) ? vehicle.LineCourse : fromRouteName.LineCourse);
        var trip = RouteDisplayHelper.NormalizeTripNumber(
            !string.IsNullOrWhiteSpace(vehicle.TripNumber) ? vehicle.TripNumber : fromRouteName.TripNumber);

        // lineCourse kann "002/01" sein oder mit Fahrt formatiert – Trip ggf. aus Display ziehen
        if (string.IsNullOrWhiteSpace(trip) && !string.IsNullOrWhiteSpace(vehicle.LineCourse))
        {
            var embedded = RouteDisplayHelper.Parse($"x (Linie: {vehicle.LineCourse})");
            if (string.IsNullOrWhiteSpace(line))
            {
                line = RouteDisplayHelper.NormalizeLineCourse(embedded.LineCourse);
            }
        }

        if (!string.IsNullOrWhiteSpace(vehicle.RouteName))
        {
            var direct = RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(
                vehicle.RouteName, editor.StopsByRoute);
            if (direct is not null &&
                editor.GetRouteDateRange(direct).Contains(serviceDate) &&
                editor.GetStops(direct).Any(s => !s.IsWaypoint && TryParseClock(s.Time, out _)))
            {
                // Wenn Linie/Fahrt gesetzt: nur akzeptieren wenn passend
                var p = RouteDisplayHelper.Parse(direct);
                var keyLine = RouteDisplayHelper.NormalizeLineCourse(p.LineCourse);
                var keyTrip = RouteDisplayHelper.NormalizeTripNumber(p.TripNumber);
                var lineOk = string.IsNullOrWhiteSpace(line) ||
                             string.Equals(keyLine, line, StringComparison.Ordinal);
                var tripOk = string.IsNullOrWhiteSpace(trip) ||
                             string.Equals(keyTrip, trip, StringComparison.Ordinal);
                if (lineOk && tripOk)
                {
                    return direct;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(line) && string.IsNullOrWhiteSpace(trip))
        {
            return null;
        }

        var candidates = new List<(string Key, int Score)>();
        foreach (var name in editor.RouteNames.Concat(editor.StopsByRoute.Keys).Distinct(StringComparer.Ordinal))
        {
            var p = RouteDisplayHelper.Parse(name);
            var keyLine = RouteDisplayHelper.NormalizeLineCourse(p.LineCourse);
            var keyTrip = RouteDisplayHelper.NormalizeTripNumber(p.TripNumber);

            if (!string.IsNullOrWhiteSpace(line) &&
                !string.Equals(keyLine, line, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(trip) &&
                !string.Equals(keyTrip, trip, StringComparison.Ordinal))
            {
                continue;
            }

            if (!editor.GetRouteDateRange(name).Contains(serviceDate))
            {
                continue;
            }

            var timedStops = editor.GetStops(name).Count(s => !s.IsWaypoint && TryParseClock(s.Time, out _));
            if (timedStops == 0)
            {
                continue;
            }

            var score = timedStops * 10;
            if (!string.IsNullOrWhiteSpace(trip) &&
                string.Equals(keyTrip, trip, StringComparison.Ordinal))
            {
                score += 100;
            }

            if (!string.IsNullOrWhiteSpace(line) &&
                string.Equals(keyLine, line, StringComparison.Ordinal))
            {
                score += 50;
            }

            candidates.Add((name, score));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => editor.GetStops(c.Key).Count)
            .Select(c => RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(c.Key, editor.StopsByRoute) ?? c.Key)
            .FirstOrDefault();
    }

    private static DateOnly ServiceDateBerlin(long timestampEpochMs)
    {
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(timestampEpochMs);
        var local = TimeZoneInfo.ConvertTime(utc, BerlinTz);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static bool TryParseClock(string? raw, out TimeOnly clock)
    {
        clock = default;
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return false;
        }

        string[] formats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];
        return TimeOnly.TryParseExact(
                   value,
                   formats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out clock) ||
               TimeOnly.TryParseExact(
                   value,
                   formats,
                   CultureInfo.GetCultureInfo("de-DE"),
                   DateTimeStyles.None,
                   out clock);
    }

    private static DateTime BuildPlannedLocal(
        DateOnly serviceDate,
        TimeOnly clock,
        ref int dayOffset,
        ref DateTime? previousPlannedLocal)
    {
        var planned = serviceDate.AddDays(dayOffset).ToDateTime(clock);
        if (previousPlannedLocal is { } prev && planned < prev)
        {
            dayOffset++;
            planned = serviceDate.AddDays(dayOffset).ToDateTime(clock);
        }

        previousPlannedLocal = planned;
        return planned;
    }

    private static string SanitizeToken(string value)
    {
        var chars = value.Trim().Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/'
                ? c
                : (char.IsWhiteSpace(c) ? '_' : '_')).ToArray();
        return chars.Length == 0 ? "X" : new string(chars);
    }

    private static TimeZoneInfo ResolveBerlinTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
    }
}
