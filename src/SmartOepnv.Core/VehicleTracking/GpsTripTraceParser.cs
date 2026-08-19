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
    public int PointCount { get; init; }
    public IReadOnlyList<GpsTripTracePoint> Points { get; init; } = [];
}

public static class GpsTripTraceParser
{
    private static readonly TimeSpan SegmentGap = TimeSpan.FromMinutes(20);

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
            return new GpsTripTraceFile
            {
                FileName = fileName,
                Phone = phone,
                VehicleName = name,
                UpdatedAtEpochMs = updated,
                Days = days
            };
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<GpsTripSegment> BuildSegments(GpsTripTraceFile file)
    {
        var segments = new List<GpsTripSegment>();
        foreach (var day in file.Days)
        {
            GpsTripTracePoint? openStart = null;
            var bucket = new List<GpsTripTracePoint>();
            string? key = null;

            void Flush()
            {
                if (bucket.Count == 0 || openStart is null)
                {
                    return;
                }

                var first = bucket[0];
                var last = bucket[^1];
                var parsed = RouteDisplayHelper.Parse(first.RouteDisplay ?? string.Empty);
                var line = first.LineCourse;
                if (string.IsNullOrWhiteSpace(line))
                {
                    line = parsed.LineCourse;
                }

                var trip = parsed.TripNumber;
                segments.Add(new GpsTripSegment
                {
                    Id = $"{file.Phone}|{day.Date}|{first.TimestampEpochMs}",
                    Date = day.Date,
                    StartEpochMs = first.TimestampEpochMs,
                    EndEpochMs = last.TimestampEpochMs,
                    LineCourse = string.IsNullOrWhiteSpace(line) ? null : line,
                    TripNumber = string.IsNullOrWhiteSpace(trip) ? null : trip,
                    RouteDisplay = first.RouteDisplay,
                    PointCount = bucket.Count,
                    Points = bucket.ToList()
                });
                bucket.Clear();
                openStart = null;
                key = null;
            }

            foreach (var point in day.Points.OrderBy(p => p.TimestampEpochMs))
            {
                var pointKey = $"{point.LineCourse}|{point.RouteDisplay}";
                if (bucket.Count > 0)
                {
                    var gap = TimeSpan.FromMilliseconds(
                        Math.Max(0, point.TimestampEpochMs - bucket[^1].TimestampEpochMs));
                    if (gap > SegmentGap || !string.Equals(pointKey, key, StringComparison.Ordinal))
                    {
                        Flush();
                    }
                }

                if (bucket.Count == 0)
                {
                    openStart = point;
                    key = pointKey;
                }

                bucket.Add(point);
            }

            Flush();
        }

        return segments
            .OrderByDescending(s => s.StartEpochMs)
            .ToList();
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
            RouteDisplay = ReadOptionalString(p, "r")
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
