using System.Globalization;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.Zeitwirtschaft;

public static class ZeitwirtschaftDocumentEditor
{
    public readonly record struct PauseCorrection(
        long OriginalStartMs,
        long? CorrectedStartMs,
        long? CorrectedEndMs);

    /// <summary>
    /// Schreibt nur die Zeiten, die von der Stempelzeit abweichen.
    /// Null entfernt eine vorhandene Korrektur und lässt das Original stehen.
    /// </summary>
    public static bool TryApplyCorrection(
        JsonObject root,
        string personnelNumber,
        string entryId,
        long? correctedStartMs,
        long? correctedEndMs,
        IReadOnlyList<PauseCorrection> pauses,
        string correctedBy,
        out string? error)
    {
        error = null;
        if (correctedStartMs is <= 0)
        {
            error = "Korrigiertes Kommen ist ungültig.";
            return false;
        }

        if (correctedEndMs is <= 0)
        {
            error = "Korrigiertes Gehen ist ungültig.";
            return false;
        }

        var entry = FindEntry(root, personnelNumber, entryId);
        if (entry is null)
        {
            error = "Eintrag nicht gefunden.";
            return false;
        }

        var originalStart = ReadPositiveLong(entry, "startEpochMs") ?? 0;
        var originalEnd = ReadPositiveLong(entry, "endEpochMs");
        var effectiveStart = correctedStartMs ?? originalStart;
        var effectiveEnd = correctedEndMs ?? originalEnd;
        if (effectiveStart <= 0)
        {
            error = "Kommen fehlt.";
            return false;
        }

        if (effectiveEnd is > 0 && effectiveEnd.Value <= effectiveStart)
        {
            error = "Gehen muss nach Kommen liegen.";
            return false;
        }

        if (entry["pauses"] is JsonArray pauseArray)
        {
            foreach (var pause in pauses)
            {
                var node = FindPause(pauseArray, pause.OriginalStartMs);
                if (node is null)
                {
                    error = "Pause nicht gefunden.";
                    return false;
                }

                var pauseStart = pause.CorrectedStartMs ?? pause.OriginalStartMs;
                var pauseEnd = pause.CorrectedEndMs ?? ReadPositiveLong(node, "endEpochMs");
                if (pause.CorrectedEndMs is > 0 && !(pauseEnd > pauseStart))
                {
                    error = "Pausenende muss nach dem Pausenbeginn liegen.";
                    return false;
                }

                if (effectiveEnd is > 0 && pauseEnd is > 0 &&
                    (pauseStart < effectiveStart || pauseEnd > effectiveEnd))
                {
                    error = "Die Pause muss zwischen Kommen und Gehen liegen.";
                    return false;
                }
            }
        }
        else if (pauses.Count > 0)
        {
            error = "Pause nicht gefunden.";
            return false;
        }

        WriteOrClear(entry, "correctedStartEpochMs", "correctedStartIso", correctedStartMs);
        WriteOrClear(entry, "correctedEndEpochMs", "correctedEndIso", correctedEndMs);
        if (entry["pauses"] is JsonArray stored)
        {
            foreach (var pause in pauses)
            {
                var node = FindPause(stored, pause.OriginalStartMs);
                if (node is null)
                {
                    continue;
                }

                WriteOrClear(node, "correctedStartEpochMs", "correctedStartIso", pause.CorrectedStartMs);
                WriteOrClear(node, "correctedEndEpochMs", "correctedEndIso", pause.CorrectedEndMs);
            }
        }

        entry["correctedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        entry["correctedBy"] = correctedBy;
        root["updatedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return true;
    }

    /// <summary>
    /// Legt eine fehlende Schicht an. Die Zeiten sind der Nachtrag selbst und bleiben als solcher gekennzeichnet.
    /// </summary>
    public static bool TryAppendManualEntry(
        JsonObject root,
        string personnelNumber,
        string name,
        string devicePhone,
        string placeLabel,
        long startMs,
        long endMs,
        long? pauseStartMs,
        long? pauseEndMs,
        string reason,
        string recordedBy,
        out string? error)
    {
        error = null;
        var personnel = personnelNumber.Trim();
        var why = reason.Trim();
        if (personnel.Length == 0)
        {
            error = "Fahrer fehlt.";
            return false;
        }

        if (why.Length < 3)
        {
            error = "Bitte den Grund angeben, zum Beispiel Stempelung vergessen.";
            return false;
        }

        if (startMs <= 0 || endMs <= 0 || endMs <= startMs)
        {
            error = "Gehen muss nach Kommen liegen.";
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (startMs > now || endMs > now)
        {
            error = "Kommen und Gehen dürfen nicht in der Zukunft liegen.";
            return false;
        }

        if (endMs - startMs > 24L * 60 * 60 * 1000)
        {
            error = "Die Schicht ist länger als 24 Stunden.";
            return false;
        }

        if (pauseStartMs is > 0 || pauseEndMs is > 0)
        {
            if (pauseStartMs is not > 0 || pauseEndMs is not > 0 || pauseEndMs <= pauseStartMs)
            {
                error = "Pausenende muss nach dem Pausenbeginn liegen.";
                return false;
            }

            if (pauseStartMs < startMs || pauseEndMs > endMs)
            {
                error = "Die Pause muss zwischen Kommen und Gehen liegen.";
                return false;
            }
        }

        if (root["drivers"] is not JsonObject drivers)
        {
            drivers = new JsonObject();
            root["drivers"] = drivers;
        }

        if (drivers[personnel] is not JsonObject driver)
        {
            driver = new JsonObject
            {
                ["personnelNumber"] = personnel,
                ["name"] = name.Trim(),
                ["entries"] = new JsonArray()
            };
            drivers[personnel] = driver;
        }

        if (driver["entries"] is not JsonArray entries)
        {
            entries = new JsonArray();
            driver["entries"] = entries;
        }

        var entry = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["personnelNumber"] = personnel,
            ["name"] = name.Trim(),
            ["recordedOnDevice"] = devicePhone,
            ["checkInPoint"] = placeLabel,
            ["checkOutPoint"] = placeLabel,
            ["checkOutDevice"] = devicePhone,
            ["startEpochMs"] = startMs,
            ["startIso"] = FormatIso(startMs),
            ["endEpochMs"] = endMs,
            ["endIso"] = FormatIso(endMs),
            ["pauses"] = new JsonArray(),
            ["startLocked"] = true,
            ["locked"] = true,
            ["manual"] = true,
            ["manualAtMs"] = now,
            ["manualBy"] = recordedBy,
            ["manualReason"] = why
        };
        if (pauseStartMs is > 0 && pauseEndMs is > 0)
        {
            entry["pauses"]!.AsArray().Add(new JsonObject
            {
                ["startEpochMs"] = pauseStartMs.Value,
                ["startIso"] = FormatIso(pauseStartMs.Value),
                ["endEpochMs"] = pauseEndMs.Value,
                ["endIso"] = FormatIso(pauseEndMs.Value)
            });
        }

        entries.Add(entry);
        root["type"] = ZeitwirtschaftMergeService.DocumentType;
        root["devicePhone"] = devicePhone;
        root["updatedAtMs"] = now;
        return true;
    }

    public static bool TryApplyVoid(
        JsonObject root,
        string personnelNumber,
        string entryId,
        string voidReason,
        string voidedBy,
        out string? error)
    {
        error = null;
        var reason = voidReason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            error = "Bitte einen Storno-Grund angeben (z. B. Test).";
            return false;
        }

        var entry = FindEntry(root, personnelNumber, entryId);
        if (entry is null)
        {
            error = "Eintrag nicht gefunden.";
            return false;
        }

        if (entry["voided"]?.GetValue<bool>() == true)
        {
            error = "Eintrag ist bereits storniert.";
            return false;
        }

        entry["voided"] = true;
        entry["voidedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        entry["voidedBy"] = voidedBy;
        entry["voidReason"] = reason;
        root["updatedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return true;
    }

    public static bool TryApplySixthDuty(
        JsonObject root,
        string personnelNumber,
        string entryId,
        bool sixthDuty,
        out string? error)
    {
        error = null;
        var entry = FindEntry(root, personnelNumber, entryId);
        if (entry is null)
        {
            error = "Eintrag nicht gefunden.";
            return false;
        }

        entry["sixthDuty"] = sixthDuty;
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

    private static JsonObject? FindPause(JsonArray pauses, long originalStartMs)
    {
        foreach (var node in pauses.OfType<JsonObject>())
        {
            if (ReadPositiveLong(node, "startEpochMs") == originalStartMs)
            {
                return node;
            }
        }

        return null;
    }

    private static void WriteOrClear(JsonObject node, string epochName, string isoName, long? epochMs)
    {
        if (epochMs is > 0)
        {
            node[epochName] = epochMs.Value;
            node[isoName] = FormatIso(epochMs.Value);
            return;
        }

        node.Remove(epochName);
        node.Remove(isoName);
    }

    private static long? ReadPositiveLong(JsonObject node, string name)
    {
        if (node[name] is JsonValue value && value.TryGetValue(out long parsed) && parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    private static string FormatIso(long epochMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(epochMs)
            .ToLocalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
