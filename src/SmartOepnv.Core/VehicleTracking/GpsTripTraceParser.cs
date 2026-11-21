using System.Globalization;
using System.Text.Json;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.VehicleTracking;

public sealed class GpsTripTraceFile
{
    public required string FileName { get; init; }
    public required string Phone { get; init; }
    public required string VehicleName { get; init; }
    public long UpdatedAtEpochMs { get; init; }
    public IReadOnlyList<GpsTripTraceDay> Days { get; init; } = [];
    public IReadOnlyList<GpsTripTraceEvent> Events { get; init; } = [];
}

/// <summary>Betriebsereignis aus der Fahrzeug-Spur (Feld <c>events[]</c>).</summary>
public sealed class GpsTripTraceEvent
{
    public long TimestampEpochMs { get; init; }
    /// <summary>login, logout, line, dest, pas_on, pas_off, bt_enable, crash/app_crash, gps_off</summary>
    public required string Kind { get; init; }
    public string? UserName { get; init; }
    public string? Detail { get; init; }
}

public sealed class GpsTripTraceDay
{
    public required string Date { get; init; }
    public IReadOnlyList<GpsTripTracePoint> Points { get; init; } = [];
}

public sealed class GpsTripTracePoint
{
    public long TimestampEpochMs { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public int SpeedKmh { get; init; }
    public string? LineCourse { get; init; }
    public string? RouteDisplay { get; init; }
    /// <summary>
    /// Bluetooth-Audio (Fahrgastraum) zum Zeitpunkt des Punkts.
    /// <c>null</c> = ältere Spur ohne Feld (Karte: gelb).
    /// </summary>
    public bool? BluetoothConnected { get; init; }
    /// <summary>
    /// Pas.Info aktiv zum Zeitpunkt des Punkts.
    /// <c>null</c> = ältere Spur ohne Feld (Karte: keine roten Streifen).
    /// </summary>
    public bool? PasInfoActive { get; init; }
    /// <summary>Angemeldeter Fahrername (optional, Feld <c>dn</c>).</summary>
    public string? DriverName { get; init; }
}

public sealed class GpsTripSegment
{
    public required string Id { get; init; }
    public required string Date { get; init; }
    public long StartEpochMs { get; init; }
    public long EndEpochMs { get; init; }
    public string? LineCourse { get; init; }
    public string? TripNumber { get; init; }
    public string? RouteDisplay { get; init; }
    public string? DriverName { get; init; }
    public int PointCount { get; init; }
    public IReadOnlyList<GpsTripTracePoint> Points { get; init; } = [];
}

/// <summary>Eine Anmeldung→Abmeldung-Sitzung für die Kontroll-Zeitleiste.</summary>
public sealed class GpsTripTimelineSession
{
    public string? UserName { get; init; }
    public long StartEpochMs { get; init; }
    public long EndEpochMs { get; init; }
    public bool ClosedByLogout { get; init; }
    public IReadOnlyList<GpsTripTraceEvent> Events { get; init; } = [];
}

public static class GpsTripTraceParser
{
    /// <summary>Linie/Kurs ohne Codierung (Fahrer angemeldet, keine Linie gewählt).</summary>
    public const string UncodedLineCourse = "000/00";

    private static readonly TimeSpan SegmentGap = TimeSpan.FromMinutes(20);
    /// <summary>Fahrtwechsel erst nach so vielen Punkten mit neuer Fahrtnummer (gegen Flackern).</summary>
    private const int TripChangeStablePoints = 3;
    /// <summary>
    /// A–B–A mit kurzem Mittelstück (z. B. falsche Fahrtnummer mitten auf der Strecke)
    /// wieder zu einer Fahrt zusammenführen. Echte Rückfahrt länger als dieses Limit bleibt getrennt.
    /// </summary>
    private static readonly TimeSpan TripFlickerMaxDuration = TimeSpan.FromMinutes(25);

    public static GpsTripTraceFile? TryParse(string fileContent, string fileName)
    {
        try
        {
            using var doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;
            var phone = ReadPhone(root, fileName);
            var name = ReadOptionalString(root, "name") ?? phone;
            var updated = root.TryGetProperty("updatedAt", out var upd) && upd.TryGetInt64(out var u)
                ? u
                : 0L;

            var days = new List<GpsTripTraceDay>();
            if (root.TryGetProperty("days", out var daysEl) && daysEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var dayEl in daysEl.EnumerateArray())
                {
                    var date = ReadOptionalString(dayEl, "d");
                    if (string.IsNullOrWhiteSpace(date))
                    {
                        continue;
                    }

                    var points = new List<GpsTripTracePoint>();
                    if (dayEl.TryGetProperty("p", out var pts) && pts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in pts.EnumerateArray())
                        {
                            var point = TryReadPoint(p);
                            if (point is not null)
                            {
                                points.Add(point);
                            }
                        }
                    }

                    days.Add(new GpsTripTraceDay { Date = date, Points = points });
                }
            }

            days.Sort((a, b) => string.Compare(a.Date, b.Date, StringComparison.Ordinal));

            var events = new List<GpsTripTraceEvent>();
            if (root.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var ev in eventsEl.EnumerateArray())
                {
                    var kind = ReadOptionalString(ev, "k");
                    if (string.IsNullOrWhiteSpace(kind))
                    {
                        continue;
                    }

                    var t = ev.TryGetProperty("t", out var tEl) && tEl.TryGetInt64(out var ts) ? ts : 0L;
                    if (t <= 0)
                    {
                        continue;
                    }

                    events.Add(new GpsTripTraceEvent
                    {
                        TimestampEpochMs = t,
                        Kind = kind,
                        UserName = ReadOptionalString(ev, "u"),
                        Detail = ReadOptionalString(ev, "d")
                    });
                }
            }

            events.Sort((a, b) => a.TimestampEpochMs.CompareTo(b.TimestampEpochMs));
            return new GpsTripTraceFile
            {
                FileName = fileName,
                Phone = phone,
                VehicleName = name,
                UpdatedAtEpochMs = updated,
                Days = days,
                Events = events
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Datum mit Zeitleisten-Inhalt: bevorzugtes Datum, sonst jüngster Tag mit Events oder GPS-Spur.
    /// </summary>
    public static DateTime? SuggestTimelineDate(GpsTripTraceFile file, DateTime? preferred)
    {
        var preferredKey = (preferred ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (BuildTimelineSessions(file, preferredKey).Count > 0)
        {
            return (preferred ?? DateTime.Today).Date;
        }

        string? latest = null;
        void Consider(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (latest is null || string.CompareOrdinal(key, latest) > 0)
            {
                latest = key;
            }
        }

        foreach (var ev in file.Events)
        {
            Consider(EpochToServiceDate(ev.TimestampEpochMs));
        }

        foreach (var day in file.Days)
        {
            if (day.Points.Count > 0)
            {
                Consider(day.Date);
            }
        }

        if (latest is null)
        {
            return null;
        }

        return DateTime.TryParseExact(
            latest,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed.Date
            : null;
    }

    /// <summary>
    /// Sitzungen eines Kalendertags (Berlin): Events plus, falls nötig, aus GPS-Punkten abgeleitet
    /// (Fahrernamen / Linie / Ziel wie in der Fahrtenliste rechts).
    /// </summary>
    public static IReadOnlyList<GpsTripTimelineSession> BuildTimelineSessions(
        GpsTripTraceFile file,
        string serviceDateYyyyMmDd)
    {
        var realEvents = file.Events
            .Where(e => EpochToServiceDate(e.TimestampEpochMs) == serviceDateYyyyMmDd)
            .OrderBy(e => e.TimestampEpochMs)
            .ToList();

        var dayEvents = MergeTimelineEvents(
            realEvents,
            SynthesizeTimelineEventsFromPoints(file, serviceDateYyyyMmDd));

        var sessions = new List<GpsTripTimelineSession>();
        List<GpsTripTraceEvent>? bucket = null;
        string? user = null;

        void Flush(bool closedByLogout, bool presumedCrashAfterRestart = false)
        {
            if (bucket is null || bucket.Count == 0)
            {
                return;
            }

            var events = bucket.ToList();
            // Neue Anmeldung ohne Abmeldung → wahrscheinlich App-Absturz (wenn kein Crash-Event schon vorhanden).
            if (presumedCrashAfterRestart &&
                !closedByLogout &&
                !events.Any(e => IsCrashKind(e.Kind)))
            {
                events.Add(new GpsTripTraceEvent
                {
                    TimestampEpochMs = bucket[^1].TimestampEpochMs,
                    Kind = "crash",
                    UserName = user,
                    Detail = "vermutet (keine Abmeldung vor neuer Anmeldung)"
                });
            }

            sessions.Add(new GpsTripTimelineSession
            {
                UserName = user,
                StartEpochMs = bucket[0].TimestampEpochMs,
                EndEpochMs = events[^1].TimestampEpochMs,
                ClosedByLogout = closedByLogout,
                Events = events
            });
            bucket = null;
            user = null;
        }

        foreach (var ev in dayEvents)
        {
            if (string.Equals(ev.Kind, "login", StringComparison.OrdinalIgnoreCase))
            {
                Flush(closedByLogout: false, presumedCrashAfterRestart: bucket is { Count: > 0 });
                bucket = [ev];
                user = ev.UserName;
                continue;
            }

            if (string.Equals(ev.Kind, "logout", StringComparison.OrdinalIgnoreCase))
            {
                bucket ??= [];
                if (!string.IsNullOrWhiteSpace(ev.UserName))
                {
                    user = ev.UserName;
                }

                bucket.Add(ev);
                Flush(closedByLogout: true);
                continue;
            }

            bucket ??= [];
            if (string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(ev.UserName))
            {
                user = ev.UserName;
            }

            bucket.Add(ev);
        }

        Flush(closedByLogout: false);
        return sessions;
    }

    /// <summary>
    /// Leitet Zeitleisten-Ereignisse aus GPS-Punkten ab, wenn die App noch keine/wenig Events geschrieben hat.
    /// </summary>
    private static List<GpsTripTraceEvent> SynthesizeTimelineEventsFromPoints(
        GpsTripTraceFile file,
        string serviceDateYyyyMmDd)
    {
        var points = file.Days
            .SelectMany(d => d.Points)
            .Where(p => EpochToServiceDate(p.TimestampEpochMs) == serviceDateYyyyMmDd)
            .OrderBy(p => p.TimestampEpochMs)
            .ToList();

        if (points.Count == 0)
        {
            return [];
        }

        var synth = new List<GpsTripTraceEvent>();
        string? lastDriver = null;
        string? lastLine = null;
        string? lastRoute = null;
        bool? lastPas = null;
        long lastPointMs = 0;

        foreach (var point in points)
        {
            var driver = EmptyToNull(point.DriverName);
            var line = NormalizeLine(point.LineCourse, point.RouteDisplay);
            var route = EmptyToNull(point.RouteDisplay);
            var pas = point.PasInfoActive;

            // Längere GPS-Lücke bei noch angemeldetem Fahrer → Sitzung trennen (Abmeldung vermutet).
            if (lastDriver is not null &&
                lastPointMs > 0 &&
                point.TimestampEpochMs - lastPointMs > SegmentGap.TotalMilliseconds)
            {
                synth.Add(new GpsTripTraceEvent
                {
                    TimestampEpochMs = lastPointMs,
                    Kind = "logout",
                    UserName = lastDriver,
                    Detail = "aus GPS-Spur (Pause)"
                });
                lastDriver = null;
                lastLine = null;
                lastRoute = null;
                lastPas = null;
            }

            if (driver is not null &&
                !string.Equals(driver, lastDriver, StringComparison.OrdinalIgnoreCase))
            {
                if (lastDriver is not null)
                {
                    synth.Add(new GpsTripTraceEvent
                    {
                        TimestampEpochMs = Math.Max(lastPointMs, point.TimestampEpochMs - 1),
                        Kind = "logout",
                        UserName = lastDriver,
                        Detail = "aus GPS-Spur"
                    });
                }

                synth.Add(new GpsTripTraceEvent
                {
                    TimestampEpochMs = point.TimestampEpochMs,
                    Kind = "login",
                    UserName = driver,
                    Detail = "aus GPS-Spur"
                });
                lastDriver = driver;
                lastLine = null;
                lastRoute = null;
            }

            if (driver is not null &&
                !string.IsNullOrWhiteSpace(line) &&
                !string.Equals(line, lastLine, StringComparison.Ordinal))
            {
                synth.Add(new GpsTripTraceEvent
                {
                    TimestampEpochMs = point.TimestampEpochMs,
                    Kind = "line",
                    UserName = driver,
                    Detail = line
                });
                lastLine = line;
            }

            if (driver is not null &&
                route is not null &&
                !string.Equals(route, lastRoute, StringComparison.Ordinal))
            {
                synth.Add(new GpsTripTraceEvent
                {
                    TimestampEpochMs = point.TimestampEpochMs,
                    Kind = "dest",
                    UserName = driver,
                    Detail = route
                });
                lastRoute = route;
            }

            if (pas is not null && pas != lastPas)
            {
                synth.Add(new GpsTripTraceEvent
                {
                    TimestampEpochMs = point.TimestampEpochMs,
                    Kind = pas.Value ? "pas_on" : "pas_off",
                    UserName = driver ?? lastDriver,
                    Detail = "aus GPS-Spur"
                });
                lastPas = pas;
            }

            lastPointMs = point.TimestampEpochMs;
        }

        return synth;
    }

    private static List<GpsTripTraceEvent> MergeTimelineEvents(
        IReadOnlyList<GpsTripTraceEvent> real,
        IReadOnlyList<GpsTripTraceEvent> synthesized)
    {
        if (synthesized.Count == 0)
        {
            return real.OrderBy(e => e.TimestampEpochMs).ToList();
        }

        const long dedupeWindowMs = 120_000;
        var merged = real.ToList();
        foreach (var synth in synthesized)
        {
            var duplicate = merged.Any(r =>
                string.Equals(r.Kind, synth.Kind, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(r.TimestampEpochMs - synth.TimestampEpochMs) <= dedupeWindowMs &&
                (string.IsNullOrWhiteSpace(synth.UserName) ||
                 string.IsNullOrWhiteSpace(r.UserName) ||
                 string.Equals(r.UserName, synth.UserName, StringComparison.OrdinalIgnoreCase)));
            if (!duplicate)
            {
                merged.Add(synth);
            }
        }

        return merged.OrderBy(e => e.TimestampEpochMs).ToList();
    }

    public static bool IsCrashKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return false;
        }

        return kind.ToLowerInvariant() is "crash" or "app_crash" or "absturz" or "fatal";
    }

    /// <summary>
    /// Eine Fahrt = Start bis Ende (auch über Mitternacht).
    /// Trennung bei GPS-Lücke &gt; 20 Min, Linien/Kurs-Wechsel oder stabilem Fahrtwechsel
    /// (Hin↔Rück). Kurzzeitiges Fahrtnummer-Flackern wird verworfen bzw. zurückgeführt.
    /// </summary>
    public static IReadOnlyList<GpsTripSegment> BuildSegments(GpsTripTraceFile file)
    {
        var segments = new List<GpsTripSegment>();
        var allPoints = file.Days
            .SelectMany(d => d.Points)
            .OrderBy(p => p.TimestampEpochMs)
            .ToList();

        var bucket = new List<GpsTripTracePoint>();
        string? activeLine = null;
        string? activeTrip = null;
        string? pendingTrip = null;
        var pendingStartIndex = 0;
        var pendingCount = 0;

        void ResetTripPending()
        {
            pendingTrip = null;
            pendingStartIndex = 0;
            pendingCount = 0;
        }

        void FlushBucket()
        {
            if (bucket.Count == 0)
            {
                return;
            }

            EmitSegment(file.Phone, bucket, segments);
            bucket.Clear();
            activeLine = null;
            activeTrip = null;
            ResetTripPending();
        }

        void FlushPrefix(int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (count >= bucket.Count)
            {
                FlushBucket();
                return;
            }

            EmitSegment(file.Phone, bucket.GetRange(0, count), segments);
            bucket.RemoveRange(0, count);
            ResetTripPending();
        }

        foreach (var point in allPoints)
        {
            var pointLine = NormalizeLineKey(point);
            var pointTrip = NormalizeTripKey(point);

            if (bucket.Count > 0)
            {
                var gap = TimeSpan.FromMilliseconds(
                    Math.Max(0, point.TimestampEpochMs - bucket[^1].TimestampEpochMs));
                // Auch Wechsel zu/von 000/00 trennt Fahrten (nicht nur Code→Code).
                var lineChanged = !string.IsNullOrEmpty(activeLine) &&
                                  !string.Equals(pointLine, activeLine, StringComparison.Ordinal);
                if (gap > SegmentGap || lineChanged)
                {
                    FlushBucket();
                }
            }

            if (bucket.Count > 0 && !string.IsNullOrEmpty(pointTrip))
            {
                if (string.IsNullOrEmpty(activeTrip))
                {
                    activeTrip = pointTrip;
                    ResetTripPending();
                }
                else if (string.Equals(pointTrip, activeTrip, StringComparison.Ordinal))
                {
                    ResetTripPending();
                }
                else if (string.Equals(pointTrip, pendingTrip, StringComparison.Ordinal))
                {
                    pendingCount++;
                    if (pendingCount >= TripChangeStablePoints)
                    {
                        // Stabiler Fahrtwechsel (z. B. Hin → Rück): alte Fahrt schließen.
                        FlushPrefix(pendingStartIndex);
                        activeTrip = pointTrip;
                        activeLine = string.IsNullOrEmpty(pointLine) ? activeLine : pointLine;
                        ResetTripPending();
                    }
                }
                else
                {
                    pendingTrip = pointTrip;
                    pendingStartIndex = bucket.Count;
                    pendingCount = 1;
                }
            }

            activeLine = pointLine;

            if (bucket.Count == 0 && !string.IsNullOrEmpty(pointTrip))
            {
                activeTrip = pointTrip;
            }

            bucket.Add(point);
        }

        FlushBucket();

        return MergeTripNumberFlicker(segments)
            .OrderByDescending(s => s.StartEpochMs)
            .ToList();
    }

    private static void EmitSegment(
        string phone,
        IReadOnlyList<GpsTripTracePoint> points,
        List<GpsTripSegment> segments)
    {
        if (points.Count == 0)
        {
            return;
        }

        var first = points[0];
        var last = points[^1];
        var meta = ResolveSegmentMeta(points);
        var date = EpochToServiceDate(first.TimestampEpochMs);
        segments.Add(new GpsTripSegment
        {
            Id = $"{phone}|{date}|{first.TimestampEpochMs}",
            Date = date,
            StartEpochMs = first.TimestampEpochMs,
            EndEpochMs = last.TimestampEpochMs,
            LineCourse = meta.LineCourse,
            TripNumber = meta.TripNumber,
            RouteDisplay = meta.RouteDisplay,
            DriverName = meta.DriverName,
            PointCount = points.Count,
            Points = points.ToList()
        });
    }

    /// <summary>
    /// Führt A–B–A zusammen, wenn das Mittelstück kurz ist (typisches Fahrtnummer-Flackern),
    /// nicht aber eine echte längere Rückfahrt zwischen zwei Fahrten.
    /// </summary>
    private static List<GpsTripSegment> MergeTripNumberFlicker(List<GpsTripSegment> segments)
    {
        var list = segments.OrderBy(s => s.StartEpochMs).ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i + 2 < list.Count; i++)
            {
                var a = list[i];
                var b = list[i + 1];
                var c = list[i + 2];
                var tripA = NormalizeTrip(a.TripNumber);
                var tripB = NormalizeTrip(b.TripNumber);
                var tripC = NormalizeTrip(c.TripNumber);
                if (string.IsNullOrEmpty(tripA) ||
                    !string.Equals(tripA, tripC, StringComparison.Ordinal) ||
                    string.Equals(tripA, tripB, StringComparison.Ordinal))
                {
                    continue;
                }

                var lineA = NormalizeLine(a.LineCourse, a.RouteDisplay);
                var lineB = NormalizeLine(b.LineCourse, b.RouteDisplay);
                var lineC = NormalizeLine(c.LineCourse, c.RouteDisplay);
                if (string.IsNullOrEmpty(lineA) ||
                    !string.Equals(lineA, lineB, StringComparison.Ordinal) ||
                    !string.Equals(lineA, lineC, StringComparison.Ordinal))
                {
                    continue;
                }

                var gapAb = TimeSpan.FromMilliseconds(Math.Max(0, b.StartEpochMs - a.EndEpochMs));
                var gapBc = TimeSpan.FromMilliseconds(Math.Max(0, c.StartEpochMs - b.EndEpochMs));
                if (gapAb > SegmentGap || gapBc > SegmentGap)
                {
                    continue;
                }

                var midDuration = TimeSpan.FromMilliseconds(Math.Max(0, b.EndEpochMs - b.StartEpochMs));
                if (midDuration > TripFlickerMaxDuration)
                {
                    continue;
                }

                list[i] = MergeSegments(a, b, c);
                list.RemoveAt(i + 2);
                list.RemoveAt(i + 1);
                changed = true;
                break;
            }
        }

        return list;
    }

    private static GpsTripSegment MergeSegments(params GpsTripSegment[] parts)
    {
        var points = parts.SelectMany(p => p.Points).OrderBy(p => p.TimestampEpochMs).ToList();
        var first = parts[0];
        var meta = ResolveSegmentMeta(points);
        return new GpsTripSegment
        {
            Id = first.Id,
            Date = first.Date,
            StartEpochMs = points[0].TimestampEpochMs,
            EndEpochMs = points[^1].TimestampEpochMs,
            LineCourse = meta.LineCourse ?? first.LineCourse,
            TripNumber = meta.TripNumber ?? first.TripNumber,
            RouteDisplay = meta.RouteDisplay ?? first.RouteDisplay,
            DriverName = meta.DriverName ?? first.DriverName,
            PointCount = points.Count,
            Points = points
        };
    }

    private static string NormalizeLineKey(GpsTripTracePoint point)
    {
        var fromLc = RouteDisplayHelper.NormalizeLineCourse(point.LineCourse);
        if (!string.IsNullOrWhiteSpace(fromLc))
        {
            return fromLc;
        }

        var parsed = RouteDisplayHelper.Parse(point.RouteDisplay ?? string.Empty);
        var fromRoute = EmptyToNull(RouteDisplayHelper.NormalizeLineCourse(parsed.LineCourse));
        return fromRoute ?? UncodedLineCourse;
    }

    private static string? NormalizeTripKey(GpsTripTracePoint point)
    {
        var parsed = RouteDisplayHelper.Parse(point.RouteDisplay ?? string.Empty);
        return EmptyToNull(RouteDisplayHelper.NormalizeTripNumber(parsed.TripNumber));
    }

    private static string? NormalizeTrip(string? trip) =>
        EmptyToNull(RouteDisplayHelper.NormalizeTripNumber(trip));

    private static string NormalizeLine(string? lineCourse, string? routeDisplay)
    {
        var fromLc = RouteDisplayHelper.NormalizeLineCourse(lineCourse);
        if (!string.IsNullOrWhiteSpace(fromLc))
        {
            return fromLc;
        }

        var parsed = RouteDisplayHelper.Parse(routeDisplay ?? string.Empty);
        return EmptyToNull(RouteDisplayHelper.NormalizeLineCourse(parsed.LineCourse))
               ?? UncodedLineCourse;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static (string? LineCourse, string? TripNumber, string? RouteDisplay, string? DriverName) ResolveSegmentMeta(
        IReadOnlyList<GpsTripTracePoint> points)
    {
        var lineVotes = new Dictionary<string, int>(StringComparer.Ordinal);
        var tripVotes = new Dictionary<string, int>(StringComparer.Ordinal);
        var routeVotes = new Dictionary<string, int>(StringComparer.Ordinal);
        var driverVotes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var point in points)
        {
            var parsed = RouteDisplayHelper.Parse(point.RouteDisplay ?? string.Empty);
            var pointLine = RouteDisplayHelper.NormalizeLineCourse(
                !string.IsNullOrWhiteSpace(point.LineCourse) ? point.LineCourse : parsed.LineCourse);
            var trip = RouteDisplayHelper.NormalizeTripNumber(parsed.TripNumber);
            if (!string.IsNullOrWhiteSpace(pointLine))
            {
                lineVotes[pointLine] = lineVotes.GetValueOrDefault(pointLine) + 1;
            }

            if (!string.IsNullOrWhiteSpace(trip))
            {
                tripVotes[trip] = tripVotes.GetValueOrDefault(trip) + 1;
            }

            if (!string.IsNullOrWhiteSpace(point.RouteDisplay))
            {
                var r = point.RouteDisplay.Trim();
                routeVotes[r] = routeVotes.GetValueOrDefault(r) + 1;
            }

            if (!string.IsNullOrWhiteSpace(point.DriverName))
            {
                var dn = point.DriverName.Trim();
                driverVotes[dn] = driverVotes.GetValueOrDefault(dn) + 1;
            }
        }

        static string? Top(Dictionary<string, int> votes) =>
            votes.Count == 0
                ? null
                : votes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => kv.Key)
                    .First();

        var resolvedLine = Top(lineVotes) ?? UncodedLineCourse;
        return (resolvedLine, Top(tripVotes), Top(routeVotes), Top(driverVotes));
    }

    private static string EpochToServiceDate(long epochMs)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(epochMs), tz);
            return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (TimeZoneNotFoundException)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(epochMs), tz);
            return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private static GpsTripTracePoint? TryReadPoint(JsonElement p)
    {
        if (!p.TryGetProperty("lat", out var latEl) || !p.TryGetProperty("lon", out var lonEl))
        {
            return null;
        }

        var lat = latEl.GetDouble();
        var lon = lonEl.GetDouble();
        if (!double.IsFinite(lat) || !double.IsFinite(lon) || (lat == 0 && lon == 0))
        {
            return null;
        }

        var t = p.TryGetProperty("t", out var tEl) && tEl.TryGetInt64(out var ts) ? ts : 0L;
        var speed = p.TryGetProperty("v", out var vEl) && vEl.TryGetInt32(out var kmh) ? kmh : 0;
        return new GpsTripTracePoint
        {
            TimestampEpochMs = t,
            Latitude = lat,
            Longitude = lon,
            SpeedKmh = Math.Max(0, speed),
            LineCourse = ReadOptionalString(p, "lc"),
            RouteDisplay = ReadOptionalString(p, "r"),
            BluetoothConnected = TryReadOptionalBool(p, "bt"),
            PasInfoActive = TryReadOptionalBool(p, "pi"),
            DriverName = ReadOptionalString(p, "dn")
        };
    }

    /// <summary>Optional: 0/1, true/false oder String.</summary>
    private static bool? TryReadOptionalBool(JsonElement p, string property)
    {
        if (!p.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when el.TryGetInt32(out var n) => n != 0,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n != 0,
            _ => null
        };
    }

    private static string ReadPhone(JsonElement root, string fileName)
    {
        var fromJson = ReadOptionalString(root, "phone");
        if (!string.IsNullOrWhiteSpace(fromJson))
        {
            return new string(fromJson.Where(char.IsDigit).ToArray());
        }

        const string prefix = DropboxConstants.GpsTraceFilePrefix;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(stem[prefix.Length..].Where(char.IsDigit).ToArray());
            if (digits.Length > 0)
            {
                return digits;
            }
        }

        return stem;
    }

    private static string? ReadOptionalString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var prop))
        {
            return null;
        }

        var s = prop.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
