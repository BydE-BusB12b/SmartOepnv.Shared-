using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.Zeitwirtschaft;

public static class ZeitwirtschaftMergeService
{
    public const string DocumentType = "zeitwirtschaft";
    public const string FilePrefix = "zeitwirtschaft_";

    public static string? PhoneFromFileName(string fileName)
    {
        if (!fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var digits = fileName[FilePrefix.Length..^".json".Length];
        return string.IsNullOrWhiteSpace(digits) ? null : digits;
    }

    public static ZeitwirtschaftMergedData MergeDocuments(IReadOnlyList<(string FilePhone, string Json)> documents)
    {
        var drivers = new Dictionary<string, DriverAccumulator>(StringComparer.Ordinal);

        foreach (var (filePhone, json) in documents)
        {
            JsonObject? root;
            try
            {
                root = JsonNode.Parse(json)?.AsObject();
            }
            catch (JsonException)
            {
                continue;
            }

            if (root is null ||
                !string.Equals(root["type"]?.GetValue<string>(), DocumentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var devicePhone = root["devicePhone"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(devicePhone))
            {
                devicePhone = filePhone;
            }

            if (root["drivers"] is not JsonObject driversObj)
            {
                continue;
            }

            foreach (var (personnelKey, driverNode) in driversObj)
            {
                if (driverNode is not JsonObject driverObj)
                {
                    continue;
                }

                var personnel = driverObj["personnelNumber"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(personnel))
                {
                    personnel = personnelKey;
                }

                var acc = drivers.GetValueOrDefault(personnel) ?? new DriverAccumulator(personnel);
                var name = driverObj["name"]?.GetValue<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    acc.Name = name;
                }

                if (driverObj["entries"] is JsonArray entries)
                {
                    foreach (var entryNode in entries)
                    {
                        if (entryNode is not JsonObject entryObj)
                        {
                            continue;
                        }

                        var id = entryObj["id"]?.GetValue<string>()?.Trim();
                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        var parsed = ParseEntry(entryObj, devicePhone ?? filePhone);
                        if (parsed is null)
                        {
                            continue;
                        }

                        if (!acc.Entries.TryGetValue(id, out var existing))
                        {
                            acc.Entries[id] = parsed;
                        }
                        else
                        {
                            acc.Entries[id] = MergeEntryImmutable(existing, parsed);
                        }
                    }
                }

                drivers[personnel] = acc;
            }
        }

        var employees = drivers.Values
            .Select(d => new ZeitwirtschaftMergedEmployee
            {
                PersonnelNumber = d.PersonnelNumber,
                Name = d.Name,
                Entries = d.Entries.Values
                    .OrderBy(e => e.StartEpochMs)
                    .ToList()
            })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.PersonnelNumber, StringComparer.Ordinal)
            .ToList();

        return new ZeitwirtschaftMergedData
        {
            Employees = employees,
            SourceFileCount = documents.Count,
            TotalEntryCount = employees.Sum(e => e.Entries.Count)
        };
    }

    public static IReadOnlyList<ZeitwirtschaftTimeTableRow> BuildTableRows(
        ZeitwirtschaftMergedEmployee employee)
    {
        var rows = new List<ZeitwirtschaftTimeTableRow>();
        foreach (var entry in employee.Entries)
        {
            var (arbeitszeit, lohnstunden) = CalcDuration(entry.StartEpochMs, entry.EndEpochMs);
            rows.Add(new ZeitwirtschaftTimeTableRow
            {
                VehiclePhone = entry.DevicePhone,
                Kommen = FormatStamp(entry.StartEpochMs, entry.StartIso),
                Gehen = entry.EndEpochMs is > 0
                    ? FormatStamp(entry.EndEpochMs.Value, entry.EndIso)
                    : "(offen)",
                Arbeitszeit = arbeitszeit,
                Lohnstunden = lohnstunden,
                EntryId = entry.EntryId
            });
        }

        return rows;
    }

    public static string FormatStamp(long epochMs, string? isoFallback)
    {
        if (epochMs > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMs)
                .ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
        }

        if (!string.IsNullOrWhiteSpace(isoFallback) &&
            DateTimeOffset.TryParse(isoFallback, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
        }

        return "—";
    }

    public static (string Arbeitszeit, string Lohnstunden) CalcDuration(long startMs, long? endMs)
    {
        if (endMs is not > 0 || endMs.Value <= startMs)
        {
            return ("—", "—");
        }

        var span = TimeSpan.FromMilliseconds(endMs.Value - startMs);
        var hours = (int)Math.Floor(span.TotalHours);
        var arbeitszeit = $"{hours}:{span.Minutes:D2}";
        var lohnDecimal = Math.Round(span.TotalHours, 2, MidpointRounding.AwayFromZero);
        var lohnstunden = lohnDecimal.ToString("0.00", CultureInfo.GetCultureInfo("de-DE"));
        return (arbeitszeit, lohnstunden);
    }

    private static ZeitwirtschaftMergedEntry? ParseEntry(JsonObject entryObj, string devicePhone)
    {
        var startMs = entryObj["startEpochMs"]?.GetValue<long>() ?? 0;
        if (startMs <= 0)
        {
            return null;
        }

        long? endMs = null;
        if (entryObj["endEpochMs"] is JsonValue endVal && endVal.TryGetValue(out long parsedEnd) && parsedEnd > 0)
        {
            endMs = parsedEnd;
        }

        var recorded = entryObj["recordedOnDevice"]?.GetValue<string>()?.Trim();
        return new ZeitwirtschaftMergedEntry
        {
            EntryId = entryObj["id"]?.GetValue<string>() ?? string.Empty,
            PersonnelNumber = string.Empty,
            Name = string.Empty,
            DevicePhone = string.IsNullOrWhiteSpace(recorded) ? devicePhone : recorded,
            StartEpochMs = startMs,
            EndEpochMs = endMs,
            StartIso = entryObj["startIso"]?.GetValue<string>(),
            EndIso = entryObj["endIso"]?.GetValue<string>()
        };
    }

    private static ZeitwirtschaftMergedEntry MergeEntryImmutable(
        ZeitwirtschaftMergedEntry existing,
        ZeitwirtschaftMergedEntry incoming)
    {
        if (IsEntryFullyLocked(existing))
        {
            return existing;
        }

        if (existing.EndEpochMs is not > 0 && incoming.EndEpochMs is > 0)
        {
            return new ZeitwirtschaftMergedEntry
            {
                EntryId = existing.EntryId,
                PersonnelNumber = existing.PersonnelNumber,
                Name = existing.Name,
                DevicePhone = existing.DevicePhone,
                StartEpochMs = existing.StartEpochMs,
                EndEpochMs = incoming.EndEpochMs,
                StartIso = existing.StartIso,
                EndIso = incoming.EndIso
            };
        }

        return existing;
    }

    private static bool IsEntryFullyLocked(ZeitwirtschaftMergedEntry entry) =>
        entry.EndEpochMs is > 0;

    private sealed class DriverAccumulator(string personnelNumber)
    {
        public string PersonnelNumber { get; } = personnelNumber;
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, ZeitwirtschaftMergedEntry> Entries { get; } =
            new(StringComparer.Ordinal);
    }
}
