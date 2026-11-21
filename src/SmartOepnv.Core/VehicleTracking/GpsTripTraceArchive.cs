using System.Text.Json.Nodes;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.VehicleTracking;

/// <summary>
/// Archiv der Fahrtenprüfung im Planer, begrenzt auf <see cref="KeepDays"/> Kalendertage (Europe/Berlin).
/// Die Dropbox-Datei bleibt der 7-Tage-Übertragungsstand vom Fahrzeug.
/// </summary>
public static class GpsTripTraceArchive
{
    public const int KeepDays = 30;
    public const string FolderName = "gps_traces";

    public static string DirectoryFor(string appSubfolder) =>
        Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace", FolderName);

    /// <summary>
    /// Übernimmt <paramref name="incomingJson"/> in das Archiv des Fahrzeugs und liefert den vollen Stand.
    /// </summary>
    public static string Absorb(string directory, string phone, string? incomingJson)
    {
        var digits = Digits(phone);
        if (digits.Length == 0)
        {
            return incomingJson ?? string.Empty;
        }

        Directory.CreateDirectory(directory);
        var path = FilePath(directory, digits);
        var archive = ReadObject(path);

        if (!string.IsNullOrWhiteSpace(incomingJson))
        {
            try
            {
                if (JsonNode.Parse(incomingJson) is JsonObject incoming)
                {
                    MergeInto(archive, incoming, digits);
                }
            }
            catch
            {
                // Unlesbarer Eingang lässt das bestehende Archiv stehen.
            }
        }

        Prune(archive);
        if (!HasTrace(archive))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return string.Empty;
        }

        var text = archive.ToJsonString();
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
        return text;
    }

    public static IEnumerable<(string Phone, string Json, string FileName)> ReadAll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, DropboxConstants.GpsTraceFilePrefix + "*.json"))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonObject archive;
            try
            {
                archive = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            }
            catch
            {
                continue;
            }

            Prune(archive);
            if (!HasTrace(archive))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Nächster Ladevorgang versucht es erneut.
                }

                continue;
            }

            var json = archive.ToJsonString();
            try
            {
                File.WriteAllText(path, json);
            }
            catch
            {
                // Anzeige aus dem bereinigten Stand, Schreiben beim nächsten Mal.
            }

            var phone = Digits(Path.GetFileNameWithoutExtension(name));
            if (phone.StartsWith(DropboxConstants.GpsTraceFilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                phone = Digits(phone[DropboxConstants.GpsTraceFilePrefix.Length..]);
            }

            if (phone.Length == 0)
            {
                continue;
            }

            yield return (phone, json, name);
        }
    }

    private static string FilePath(string directory, string digits) =>
        Path.Combine(directory, DropboxConstants.GpsTraceFilePrefix + digits + ".json");

    private static JsonObject ReadObject(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static void MergeInto(JsonObject archive, JsonObject incoming, string phone)
    {
        archive["v"] = 1;
        archive["phone"] = phone;

        var incomingName = incoming["name"]?.GetValue<string>()?.Trim();
        if (!string.IsNullOrEmpty(incomingName))
        {
            archive["name"] = incomingName;
        }

        var updated = Math.Max(ReadLong(archive["updatedAt"]), ReadLong(incoming["updatedAt"]));
        if (updated > 0)
        {
            archive["updatedAt"] = updated;
        }

        archive["days"] = MergeDays(archive["days"] as JsonArray, incoming["days"] as JsonArray);
        archive["events"] = MergeEvents(archive["events"] as JsonArray, incoming["events"] as JsonArray);
    }

    private static void Prune(JsonObject archive)
    {
        var (cutoffDate, cutoffMs) = RetentionWindow();
        if (archive["days"] is JsonArray days)
        {
            var keptDays = new JsonArray();
            foreach (var node in days)
            {
                var date = node?["d"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrEmpty(date) || string.CompareOrdinal(date, cutoffDate) < 0)
                {
                    continue;
                }

                keptDays.Add(node!.DeepClone());
            }

            archive["days"] = keptDays;
        }

        if (archive["events"] is JsonArray events)
        {
            var keptEvents = new JsonArray();
            foreach (var node in events)
            {
                if (ReadLong(node?["t"]) < cutoffMs)
                {
                    continue;
                }

                keptEvents.Add(node!.DeepClone());
            }

            archive["events"] = keptEvents;
        }
    }

    private static bool HasTrace(JsonObject archive)
    {
        if (archive["days"] is JsonArray days && days.Count > 0)
        {
            return true;
        }

        return archive["events"] is JsonArray events && events.Count > 0;
    }

    private static (string CutoffDate, long CutoffEpochMs) RetentionWindow()
    {
        var berlin = BerlinTime();
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, berlin).Date;
        var cutoff = today.AddDays(-(KeepDays - 1));
        var cutoffMs = new DateTimeOffset(cutoff, berlin.GetUtcOffset(cutoff)).ToUnixTimeMilliseconds();
        return (cutoff.ToString("yyyy-MM-dd"), cutoffMs);
    }

    private static TimeZoneInfo BerlinTime()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }

    private static JsonArray MergeDays(JsonArray? existing, JsonArray? incoming)
    {
        var byDate = new SortedDictionary<string, SortedDictionary<long, JsonObject>>(StringComparer.Ordinal);
        AbsorbDays(byDate, existing);
        AbsorbDays(byDate, incoming);

        var days = new JsonArray();
        foreach (var (date, points) in byDate)
        {
            var pointArray = new JsonArray();
            foreach (var point in points.Values)
            {
                pointArray.Add(point);
            }

            days.Add(new JsonObject
            {
                ["d"] = date,
                ["p"] = pointArray
            });
        }

        return days;
    }

    private static void AbsorbDays(
        SortedDictionary<string, SortedDictionary<long, JsonObject>> byDate,
        JsonArray? days)
    {
        if (days is null)
        {
            return;
        }

        foreach (var dayNode in days)
        {
            if (dayNode is not JsonObject day)
            {
                continue;
            }

            var date = day["d"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(date))
            {
                continue;
            }

            if (!byDate.TryGetValue(date, out var points))
            {
                points = new SortedDictionary<long, JsonObject>();
                byDate[date] = points;
            }

            if (day["p"] is not JsonArray pointArray)
            {
                continue;
            }

            foreach (var pointNode in pointArray)
            {
                if (pointNode is not JsonObject point)
                {
                    continue;
                }

                var stamp = ReadLong(point["t"]);
                if (stamp <= 0)
                {
                    continue;
                }

                points[stamp] = point.DeepClone().AsObject();
            }
        }
    }

    private static JsonArray MergeEvents(JsonArray? existing, JsonArray? incoming)
    {
        var byKey = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        AbsorbEvents(byKey, existing);
        AbsorbEvents(byKey, incoming);

        var events = new JsonArray();
        foreach (var ev in byKey.Values)
        {
            events.Add(ev);
        }

        return events;
    }

    private static void AbsorbEvents(SortedDictionary<string, JsonObject> byKey, JsonArray? events)
    {
        if (events is null)
        {
            return;
        }

        foreach (var node in events)
        {
            if (node is not JsonObject ev)
            {
                continue;
            }

            var stamp = ReadLong(ev["t"]);
            var kind = ev["k"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (stamp <= 0 || kind.Length == 0)
            {
                continue;
            }

            var user = ev["u"]?.GetValue<string>() ?? string.Empty;
            var detail = ev["d"]?.GetValue<string>() ?? string.Empty;
            var key = stamp.ToString() + "|" + kind + "|" + user + "|" + detail;
            byKey[key] = ev.DeepClone().AsObject();
        }
    }

    private static long ReadLong(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<long>(out var asLong))
        {
            return asLong;
        }

        if (value.TryGetValue<int>(out var asInt))
        {
            return asInt;
        }

        if (value.TryGetValue<double>(out var asDouble) && double.IsFinite(asDouble))
        {
            return (long)asDouble;
        }

        return 0;
    }

    private static string Digits(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsDigit).ToArray());
    }
}
