using System.Text.Json.Nodes;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// GPS-Spur der letzten 7 Tage, lokal und nach Dropbox unter gps_trace_.
/// </summary>
public static class GpsSpur
{
    private static readonly object Gate = new();
    private static long _lastUpload;
    private static long _lastPoint;

    public static void Note(FahrSession session, double lat, double lon, double speedMps)
    {
        if (Math.Abs(lat) < 0.0001 && Math.Abs(lon) < 0.0001)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastPoint < 5000)
        {
            return;
        }

        _lastPoint = now;
        Append(session, "point", lat, lon, speedMps, "");
        MaybeUpload(session);
    }

    public static void Event(FahrSession session, string kind, string text) =>
        Append(session, kind, session.Trip.LastLat, session.Trip.LastLon, session.Trip.LastSpeedMps, text);

    private static void Append(FahrSession session, string kind, double lat, double lon, double speedMps, string text)
    {
        lock (Gate)
        {
            var path = Path.Combine(session.WorkspaceDir, "gps_trip_trace.json");
            var root = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject()
                : new JsonObject();
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            if (root["days"] is not JsonObject days)
            {
                days = new JsonObject();
                root["days"] = days;
            }

            if (days[day] is not JsonArray rows)
            {
                rows = new JsonArray();
                days[day] = rows;
            }

            rows.Add(new JsonObject
            {
                ["atMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["kind"] = kind,
                ["lat"] = lat,
                ["lon"] = lon,
                ["speedKmh"] = (int)Math.Max(0, speedMps * 3.6),
                ["route"] = session.RouteName ?? "",
                ["text"] = text,
                ["pas"] = PasInfo.Active
            });
            var cutoff = DateTime.Now.Date.AddDays(-7).ToString("yyyy-MM-dd");
            foreach (var key in days.Select(pair => pair.Key).Where(key => string.CompareOrdinal(key, cutoff) < 0).ToList())
            {
                days.Remove(key);
            }

            root["devicePhone"] = session.DevicePhone;
            root["updatedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            File.WriteAllText(path, root.ToJsonString());
        }
    }

    private static void MaybeUpload(FahrSession session)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastUpload < 60_000)
        {
            return;
        }

        _lastUpload = now;
        _ = Task.Run(async () =>
        {
            try
            {
                var client = FahrbetriebPages.OpenDropbox();
                var path = Path.Combine(session.WorkspaceDir, "gps_trip_trace.json");
                if (client is null || !File.Exists(path))
                {
                    return;
                }

                var name = $"gps_trace_{FahrbetriebPages.Digits(session.DevicePhone)}.json";
                await client.UploadNamedFileAsync(name, await File.ReadAllTextAsync(path));
            }
            catch (Exception ex)
            {
                FahrProtokoll.Add("GPS-Spur: " + ex.Message);
            }
        });
    }
}

public static class PasInfo
{
    public static bool Active { get; set; }
}
