using System.Text.Json.Nodes;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Pausenstempel wie in der App: nur bei angemeldeter, offener Schicht.
/// Ohne offenen Arbeitsbeginn wird nichts geschrieben.
/// </summary>
public static class ZeitPause
{
    public static bool Start(FahrSession session)
    {
        return Change(session, ending: false);
    }

    public static bool End(FahrSession session)
    {
        return Change(session, ending: true);
    }

    /// <summary>Schicht beim Zwangsabmelden schließen, falls noch offen.</summary>
    public static void EndShift(FahrSession session)
    {
        End(session);
        Mutate(session, entry =>
        {
            if ((entry["endEpochMs"]?.GetValue<long>() ?? 0) <= 0)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                entry["endEpochMs"] = now;
                entry["endIso"] = Iso(now);
            }
        });
    }

    private static bool Change(FahrSession session, bool ending)
    {
        var inserted = false;
        var changed = Mutate(session, entry =>
        {
            var pauses = entry["pauses"] as JsonArray ?? new JsonArray();
            entry["pauses"] = pauses;
            var running = pauses.OfType<JsonObject>().LastOrDefault(p =>
                (p["startEpochMs"]?.GetValue<long>() ?? 0) > 0 &&
                (p["endEpochMs"]?.GetValue<long>() ?? 0) <= 0);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!ending)
            {
                if (running is not null)
                {
                    return;
                }

                pauses.Add(new JsonObject
                {
                    ["startEpochMs"] = now,
                    ["startIso"] = Iso(now)
                });
                inserted = true;
            }
            else if (running is not null)
            {
                running["endEpochMs"] = now;
                running["endIso"] = Iso(now);
                inserted = true;
            }
        });
        return changed && inserted;
    }

    private static bool Mutate(FahrSession session, Action<JsonObject> change)
    {
        if (session.Employee is null || string.IsNullOrWhiteSpace(session.DevicePhone))
        {
            return false;
        }

        var path = FahrbetriebPages.ZeitPath(session);
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? FahrbetriebPages.NewZeit(session)
            : FahrbetriebPages.NewZeit(session);
        if (root["drivers"] is not JsonObject drivers)
        {
            drivers = new JsonObject();
            root["drivers"] = drivers;
        }

        var pn = session.Employee.PersonnelNumber;
        if (drivers[pn] is not JsonObject driver)
        {
            driver = new JsonObject
            {
                ["personnelNumber"] = pn,
                ["name"] = session.Employee.Name,
                ["entries"] = new JsonArray()
            };
            drivers[pn] = driver;
        }

        var entries = driver["entries"] as JsonArray ?? new JsonArray();
        driver["entries"] = entries;
        var open = FahrbetriebPages.OpenShift(entries);
        if (open is null)
        {
            return false;
        }

        change(open);
        root["updatedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        root["devicePhone"] = session.DevicePhone;
        File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        _ = UploadAsync(session, path);
        return true;
    }

    private static async Task UploadAsync(FahrSession session, string path)
    {
        try
        {
            var client = FahrbetriebPages.OpenDropbox();
            if (client is null || !File.Exists(path))
            {
                return;
            }

            var name = $"zeitwirtschaft_{FahrbetriebPages.Digits(session.DevicePhone)}.json";
            await client.UploadNamedFileAsync(name, await File.ReadAllTextAsync(path));
        }
        catch (Exception ex)
        {
            FahrProtokoll.Add("Pausenstempel: " + ex.Message);
        }
    }

    private static string Iso(long epochMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
}
