using System.Globalization;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.Zeitwirtschaft;

public static class ZeitwirtschaftDocumentEditor
{
    public static bool TryApplyCorrection(
        JsonObject root,
        string personnelNumber,
        string entryId,
        long correctedStartMs,
        long? correctedEndMs,
        string correctedBy,
        out string? error)
    {
        error = null;
        if (correctedStartMs <= 0)
        {
            error = "Korrigierter Beginn ist ungültig.";
            return false;
        }

        if (correctedEndMs is > 0 && correctedEndMs.Value <= correctedStartMs)
        {
            error = "Arbeitsende muss nach dem Beginn liegen.";
            return false;
        }

        var entry = FindEntry(root, personnelNumber, entryId);
        if (entry is null)
        {
            error = "Eintrag nicht gefunden.";
            return false;
        }

        entry["correctedStartEpochMs"] = correctedStartMs;
        entry["correctedStartIso"] = FormatIso(correctedStartMs);
        if (correctedEndMs is > 0)
        {
            entry["correctedEndEpochMs"] = correctedEndMs.Value;
            entry["correctedEndIso"] = FormatIso(correctedEndMs.Value);
        }
        else
        {
            entry.Remove("correctedEndEpochMs");
            entry.Remove("correctedEndIso");
        }

        entry["correctedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        entry["correctedBy"] = correctedBy;
        root["updatedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return true;
    }

    public static JsonObject? FindEntry(JsonObject root, string personnelNumber, string entryId)
    {
        var drivers = root["drivers"] as JsonObject;
        if (drivers is null)
        {
            return null;
        }

        JsonObject? driver = null;
        if (drivers[personnelNumber] is JsonObject direct)
        {
            driver = direct;
        }
        else
        {
            foreach (var prop in drivers)
            {
                if (prop.Value is JsonObject obj &&
                    string.Equals(
                        obj["personnelNumber"]?.GetValue<string>()?.Trim(),
                        personnelNumber,
                        StringComparison.OrdinalIgnoreCase))
                {
                    driver = obj;
                    break;
                }
            }
        }

        if (driver?["entries"] is not JsonArray entries)
        {
            return null;
        }

        foreach (var node in entries.OfType<JsonObject>())
        {
            if (string.Equals(node["id"]?.GetValue<string>(), entryId, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }

    public static string Serialize(JsonObject root) =>
        root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static string FormatIso(long epochMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(epochMs)
            .ToLocalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
