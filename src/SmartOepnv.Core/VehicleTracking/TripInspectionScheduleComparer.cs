using System.Globalization;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.Core.VehicleTracking;

/// <summary>Eine Zeile Soll/Ist für die Leitstellen-Fahrtenprüfung.</summary>
public sealed class TripInspectionScheduleRow
{
    public int Sequence { get; init; }
    public required string StopName { get; init; }
    public DateTimeOffset? PlannedLocal { get; init; }
    public DateTimeOffset? ActualLocal { get; init; }
    /// <summary>Soll − Ist in Sekunden (+ früh, − spät), null wenn Ist fehlt.</summary>
    public int? DelaySeconds { get; init; }

    public string PlannedLabel => FormatClock(PlannedLocal);
    public string ActualLabel => FormatClock(ActualLocal);
    public string DelayLabel => FormatDelay(DelaySeconds);

    private static string FormatClock(DateTimeOffset? value) =>
        value is { } t
            ? t.ToString("HH:mm:ss", CultureInfo.GetCultureInfo("de-DE"))
            : "–";

    private static string FormatDelay(int? delaySeconds)
    {
        if (delaySeconds is null)
        {
            return "–";
        }

        var sec = delaySeconds.Value;
        var sign = sec > 0 ? "+" : sec < 0 ? "−" : "";
        var abs = Math.Abs(sec);
        var m = abs / 60;
        var s = abs % 60;
        return $"{sign}{m}:{s:D2}";
    }
}

public sealed class TripInspectionScheduleCompareResult
{
    public static TripInspectionScheduleCompareResult Empty(string message) => new()
    {
        Matched = false,
        Message = message,
        Rows = []
    };

    public bool Matched { get; init; }
    public string? RouteKey { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<TripInspectionScheduleRow> Rows { get; init; } = [];
    public bool HasRows => Rows.Count > 0;
}

/// <summary>
/// Verknüpft GPS-Spur-Segmente mit Fahrplan-Halten (<see cref="RouteStopItem.Time"/>)
/// und schätzt Ist-Zeiten per Geofence (letzte Passage im Radius).
/// </summary>
public static class TripInspectionScheduleComparer
{
    private const int MinRadiusMeters = 40;
    private static readonly TimeZoneInfo BerlinTz = ResolveBerlinTimeZone();

    public static TripInspectionScheduleCompareResult Build(
        GpsTripSegment segment,
        EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return TripInspectionScheduleCompareResult.Empty("Kein Fahrplan geladen.");
        }

        if (!DateOnly.TryParseExact(
                segment.Date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var serviceDate))
        {
            return TripInspectionScheduleCompareResult.Empty("Segment ohne gültiges Datum.");
        }

        var routeKey = TryResolveRouteKey(segment, editor, serviceDate);
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return TripInspectionScheduleCompareResult.Empty(
                "Kein Fahrplan zu Linie/Kurs/Fahrt gefunden.");
        }

        var stops = editor.GetStops(routeKey)
            .Where(s => !s.IsWaypoint)
            .Where(s => TryParseClock(s.Time, out _))
            .ToList();
        if (stops.Count == 0)
        {
            return TripInspectionScheduleCompareResult.Empty(
                $"Route gefunden ({ShortRouteLabel(routeKey)}), aber ohne Sollzeiten.");
        }

        var points = segment.Points
            .OrderBy(p => p.TimestampEpochMs)
            .ToList();
        var rows = new List<TripInspectionScheduleRow>(stops.Count);
        long minEpochMs = 0;
        DateTime? previousPlannedLocal = null;
        var dayOffset = 0;

        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            _ = TryParseClock(stop.Time, out var clock);
            var plannedLocal = BuildPlannedLocal(serviceDate, clock, ref dayOffset, ref previousPlannedLocal);
            DateTimeOffset? actualLocal = null;
            int? delaySeconds = null;

            if (TryGetStopLatLon(stop, out var lat, out var lon))
            {
                var radius = Math.Max(MinRadiusMeters, stop.Radius > 0 ? stop.Radius : 50);
                var hit = FindLastPassage(points, lat, lon, radius, minEpochMs);
                if (hit is { } p)
                {
                    actualLocal = DateTimeOffset.FromUnixTimeMilliseconds(p.TimestampEpochMs)
                        .ToOffset(BerlinTz.GetUtcOffset(
                            DateTimeOffset.FromUnixTimeMilliseconds(p.TimestampEpochMs)));
                    minEpochMs = p.TimestampEpochMs;
                    var plannedOffset = new DateTimeOffset(plannedLocal, BerlinTz.GetUtcOffset(plannedLocal));
                    delaySeconds = (int)Math.Round((plannedOffset - actualLocal.Value).TotalSeconds);
                }
            }

            rows.Add(new TripInspectionScheduleRow
            {
                Sequence = i + 1,
                StopName = string.IsNullOrWhiteSpace(stop.Name) ? $"Halt {i + 1}" : stop.Name.Trim(),
                PlannedLocal = new DateTimeOffset(plannedLocal, BerlinTz.GetUtcOffset(plannedLocal)),
                ActualLocal = actualLocal,
                DelaySeconds = delaySeconds
            });
        }

        var withActual = rows.Count(r => r.ActualLocal is not null);
        return new TripInspectionScheduleCompareResult
        {
            Matched = true,
            RouteKey = routeKey,
            Message = $"{ShortRouteLabel(routeKey)} · {withActual}/{rows.Count} Halte mit Ist-Zeit",
            Rows = rows
        };
    }

    private static string? TryResolveRouteKey(
        GpsTripSegment segment,
        EditableRoutePackage editor,
        DateOnly serviceDate)
    {
        var parsed = RouteDisplayHelper.Parse(segment.RouteDisplay ?? string.Empty);
        var line = RouteDisplayHelper.NormalizeLineCourse(
            !string.IsNullOrWhiteSpace(segment.LineCourse) ? segment.LineCourse : parsed.LineCourse);
        var trip = RouteDisplayHelper.NormalizeTripNumber(
            !string.IsNullOrWhiteSpace(segment.TripNumber) ? segment.TripNumber : parsed.TripNumber);

        if (string.IsNullOrWhiteSpace(line) && string.IsNullOrWhiteSpace(trip) &&
            !string.IsNullOrWhiteSpace(segment.RouteDisplay))
        {
            var direct = RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(
                segment.RouteDisplay, editor.StopsByRoute);
            if (direct is not null &&
                editor.GetRouteDateRange(direct).Contains(serviceDate) &&
                editor.GetStops(direct).Any(s => TryParseClock(s.Time, out _)))
            {
                return direct;
            }
        }

        if (string.IsNullOrWhiteSpace(line) && string.IsNullOrWhiteSpace(trip))
        {
            return null;
        }

        var candidates = new List<(string Key, int Score)>();
        foreach (var name in editor.RouteNames)
        {
            ConsiderCandidate(name);
        }

        foreach (var key in editor.StopsByRoute.Keys)
        {
            ConsiderCandidate(key);
        }

        void ConsiderCandidate(string key)
        {
            var p = RouteDisplayHelper.Parse(key);
            var keyLine = RouteDisplayHelper.NormalizeLineCourse(p.LineCourse);
            var keyTrip = RouteDisplayHelper.NormalizeTripNumber(p.TripNumber);
            if (!string.IsNullOrWhiteSpace(line) &&
                !string.Equals(keyLine, line, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(trip) &&
                !string.Equals(keyTrip, trip, StringComparison.Ordinal))
            {
                return;
            }

            if (!editor.GetRouteDateRange(key).Contains(serviceDate))
            {
                return;
            }

            var timedStops = editor.GetStops(key).Count(s => !s.IsWaypoint && TryParseClock(s.Time, out _));
            if (timedStops == 0)
            {
                return;
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

            candidates.Add((key, score));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => editor.GetStops(c.Key).Count)
            .Select(c => RoutePackageRouteKeyHelper.ResolveRouteKeyWithStops(c.Key, editor.StopsByRoute) ?? c.Key)
            .FirstOrDefault();
    }

    /// <summary>
    /// Letzte GPS-Passage im Haltestellenradius (Ende des Besuchs).
    /// Frühe Einfahrt zählt nicht – erst der letzte Punkt vor dem Verlassen.
    /// </summary>
    private static GpsTripTracePoint? FindLastPassage(
        IReadOnlyList<GpsTripTracePoint> points,
        double lat,
        double lon,
        int radiusMeters,
        long minEpochMsExclusive)
    {
        GpsTripTracePoint? lastInRadius = null;
        var inside = false;

        foreach (var point in points)
        {
            if (point.TimestampEpochMs <= minEpochMsExclusive)
            {
                continue;
            }

            var d = RoutePathGeo.HaversineMeters(
                new RoutePathLatLng { Lat = lat, Lon = lon },
                new RoutePathLatLng { Lat = point.Latitude, Lon = point.Longitude });
            var inRadius = d <= radiusMeters;

            if (inRadius)
            {
                lastInRadius = point;
                inside = true;
                continue;
            }

            if (inside)
            {
                // Besuch beendet: letzter Punkt im Radius ist die Ist-Zeit.
                return lastInRadius;
            }
        }

        return lastInRadius;
    }

    private static bool TryGetStopLatLon(RouteStopItem stop, out double lat, out double lon)
    {
        lat = lon = double.NaN;
        if (RouteCoordinateParser.TryParse(stop.StopCoordinates, out lat, out lon))
        {
            return true;
        }

        return RouteCoordinateParser.TryParse(stop.GpsCoordinates, out lat, out lon);
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

    private static string ShortRouteLabel(string routeKey)
    {
        var p = RouteDisplayHelper.Parse(routeKey);
        var line = (p.LineCourse ?? string.Empty).Trim();
        var trip = (p.TripNumber ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(line) && !string.IsNullOrEmpty(trip))
        {
            return $"{line} / Fahrt {trip}";
        }

        if (!string.IsNullOrEmpty(line))
        {
            return line;
        }

        return routeKey.Length <= 48 ? routeKey : routeKey[..45] + "…";
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
