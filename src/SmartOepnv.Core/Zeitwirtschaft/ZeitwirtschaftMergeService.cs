using System.Globalization;

using System.Text.Json;

using System.Text.Json.Nodes;



namespace SmartOepnv.Core.Zeitwirtschaft;



public static class ZeitwirtschaftMergeService

{

    public const string DocumentType = "zeitwirtschaft";

    public const string FilePrefix = "zeitwirtschaft_";



    public static string BuildFileName(string phoneDigits) => $"{FilePrefix}{phoneDigits}.json";



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



                        var parsed = ParseEntry(entryObj, devicePhone ?? filePhone, personnel);

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

                    .OrderBy(e => EffectiveStartMs(e))

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

        ZeitwirtschaftMergedEmployee employee,

        int? year = null,

        int? month = null,

        IReadOnlyDictionary<string, string>? vehicleLabels = null)

    {

        var rows = new List<ZeitwirtschaftTimeTableRow>();

        foreach (var entry in employee.Entries)

        {

            if (year is > 0 && month is > 0)

            {

                var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(EffectiveStartMs(entry)).ToLocalTime();

                if (startLocal.Year != year || startLocal.Month != month)

                {

                    continue;

                }

            }



            var effectiveStart = EffectiveStartMs(entry);

            var effectiveEnd = EffectiveEndMs(entry);

            var isVoided = IsVoided(entry);

            var (arbeitszeit, lohnstunden) = isVoided

                ? ("—", "—")

                : CalcDuration(effectiveStart, effectiveEnd);

            var hasCorrection = HasCorrection(entry);

            var kommen = FormatCellWithCorrection(entry.StartEpochMs, entry.StartIso, entry.CorrectedStartEpochMs, entry.CorrectedStartIso);

            var gehen = entry.EndEpochMs is > 0 || entry.CorrectedEndEpochMs is > 0

                    ? FormatCellWithCorrection(

                        entry.EndEpochMs ?? 0,

                        entry.EndIso,

                        entry.CorrectedEndEpochMs,

                        entry.CorrectedEndIso,

                        emptyWhenMissing: entry.EndEpochMs is not > 0 && entry.CorrectedEndEpochMs is not > 0)

                    : "(offen)";

            if (isVoided)

            {

                var reason = string.IsNullOrWhiteSpace(entry.VoidReason) ? "Storno" : entry.VoidReason.Trim();

                kommen = $"{kommen}\n(Storno: {reason})";

                gehen = entry.EndEpochMs is > 0 || entry.CorrectedEndEpochMs is > 0

                    ? $"{gehen}\n(Storno: {reason})"

                    : gehen;

            }

            rows.Add(new ZeitwirtschaftTimeTableRow

            {

                VehiclePhone = entry.DevicePhone,

                VehicleDisplayName = ZeitwirtschaftVehicleLabelResolver.Resolve(entry.DevicePhone, vehicleLabels),

                PersonnelNumber = employee.PersonnelNumber,

                Kommen = kommen,

                Gehen = gehen,

                Arbeitszeit = arbeitszeit,

                Lohnstunden = lohnstunden,

                EntryId = entry.EntryId,

                HasCorrection = hasCorrection,

                IsVoided = isVoided,

                VoidReason = entry.VoidReason

            });

        }



        return rows;

    }



    public static long EffectiveStartMs(ZeitwirtschaftMergedEntry entry) =>

        entry.CorrectedStartEpochMs is > 0 ? entry.CorrectedStartEpochMs.Value : entry.StartEpochMs;



    public static long? EffectiveEndMs(ZeitwirtschaftMergedEntry entry)

    {

        if (entry.CorrectedEndEpochMs is > 0)

        {

            return entry.CorrectedEndEpochMs;

        }



        return entry.EndEpochMs is > 0 ? entry.EndEpochMs : null;

    }



    public static bool HasCorrection(ZeitwirtschaftMergedEntry entry) =>

        entry.CorrectedStartEpochMs is > 0 || entry.CorrectedEndEpochMs is > 0;



    public static bool IsVoided(ZeitwirtschaftMergedEntry entry) => entry.Voided;



    public static string FormatCellWithCorrection(

        long originalMs,

        string? originalIso,

        long? correctedMs,

        string? correctedIso,

        bool emptyWhenMissing = false)

    {

        if (originalMs <= 0 && correctedMs is not > 0)

        {

            return emptyWhenMissing ? "—" : "(offen)";

        }



        var original = originalMs > 0

            ? FormatStamp(originalMs, originalIso)

            : "—";

        if (correctedMs is not > 0)

        {

            return original;

        }



        return $"{original}\n→ {FormatStamp(correctedMs.Value, correctedIso)}";

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



        var totalMinutes = WholeMinutesBetween(startMs, endMs.Value);

        var formatted = FormatMinutesHhMm(totalMinutes);

        return (formatted, formatted);

    }



    /// <summary>
    /// Differenz nur über volle Minuten (wie in Kommen/Gehen angezeigt), Sekunden werden ignoriert.
    /// </summary>
    public static int WholeMinutesBetween(long startMs, long endMs)
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(startMs).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeMilliseconds(endMs).ToLocalTime();
        var startMinute = new DateTimeOffset(
            start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, start.Offset);
        var endMinute = new DateTimeOffset(
            end.Year, end.Month, end.Day, end.Hour, end.Minute, 0, end.Offset);
        return Math.Max(0, (int)(endMinute - startMinute).TotalMinutes);
    }



    public static string FormatDurationHhMm(long durationMs)
    {
        var totalMinutes = (int)Math.Floor(TimeSpan.FromMilliseconds(durationMs).TotalMinutes);
        return FormatMinutesHhMm(totalMinutes);
    }

    public static string FormatMinutesHhMm(int totalMinutes) =>
        $"{totalMinutes / 60}:{totalMinutes % 60:D2}";

    public static int? ParseHhMmToMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "—")
        {
            return null;
        }

        var parts = value.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            return null;
        }

        return hours * 60 + minutes;
    }

    public static string SumDurationHhMm(IEnumerable<ZeitwirtschaftTimeTableRow> rows)
    {
        var totalMinutes = rows
            .Where(r => !r.IsVoided)
            .Select(r => ParseHhMmToMinutes(r.Arbeitszeit))
            .Where(v => v.HasValue)
            .Sum(v => v!.Value);
        return totalMinutes > 0 ? FormatMinutesHhMm(totalMinutes) : "—";
    }

    private static ZeitwirtschaftMergedEntry? ParseEntry(JsonObject entryObj, string devicePhone, string personnel)

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



        long? correctedStart = ReadPositiveLong(entryObj, "correctedStartEpochMs");

        long? correctedEnd = ReadPositiveLong(entryObj, "correctedEndEpochMs");

        long? correctedAt = ReadPositiveLong(entryObj, "correctedAtMs");

        var voided = entryObj["voided"]?.GetValue<bool>() == true;

        long? voidedAt = ReadPositiveLong(entryObj, "voidedAtMs");



        var recorded = entryObj["recordedOnDevice"]?.GetValue<string>()?.Trim();

        return new ZeitwirtschaftMergedEntry

        {

            EntryId = entryObj["id"]?.GetValue<string>() ?? string.Empty,

            PersonnelNumber = personnel,

            Name = string.Empty,

            DevicePhone = string.IsNullOrWhiteSpace(recorded) ? devicePhone : recorded,

            StartEpochMs = startMs,

            EndEpochMs = endMs,

            StartIso = entryObj["startIso"]?.GetValue<string>(),

            EndIso = entryObj["endIso"]?.GetValue<string>(),

            CorrectedStartEpochMs = correctedStart,

            CorrectedEndEpochMs = correctedEnd,

            CorrectedStartIso = entryObj["correctedStartIso"]?.GetValue<string>(),

            CorrectedEndIso = entryObj["correctedEndIso"]?.GetValue<string>(),

            CorrectedAtMs = correctedAt,

            CorrectedBy = entryObj["correctedBy"]?.GetValue<string>(),

            Voided = voided,

            VoidedAtMs = voidedAt,

            VoidedBy = entryObj["voidedBy"]?.GetValue<string>(),

            VoidReason = entryObj["voidReason"]?.GetValue<string>()

        };

    }



    private static long? ReadPositiveLong(JsonObject obj, string key)

    {

        if (obj[key] is JsonValue val && val.TryGetValue(out long parsed) && parsed > 0)

        {

            return parsed;

        }



        return null;

    }



    private static ZeitwirtschaftMergedEntry MergeEntryImmutable(

        ZeitwirtschaftMergedEntry existing,

        ZeitwirtschaftMergedEntry incoming)

    {

        var endEpochMs = existing.EndEpochMs;

        var endIso = existing.EndIso;

        if (existing.EndEpochMs is not > 0 && incoming.EndEpochMs is > 0)

        {

            endEpochMs = incoming.EndEpochMs;

            endIso = incoming.EndIso;

        }



        var correctedStart = existing.CorrectedStartEpochMs;

        var correctedEnd = existing.CorrectedEndEpochMs;

        var correctedStartIso = existing.CorrectedStartIso;

        var correctedEndIso = existing.CorrectedEndIso;

        var correctedAt = existing.CorrectedAtMs;

        var correctedBy = existing.CorrectedBy;

        var voided = existing.Voided;

        var voidedAt = existing.VoidedAtMs;

        var voidedBy = existing.VoidedBy;

        var voidReason = existing.VoidReason;

        if (incoming.CorrectedAtMs is > 0 &&

            (existing.CorrectedAtMs is null or 0 || incoming.CorrectedAtMs > existing.CorrectedAtMs))

        {

            correctedStart = incoming.CorrectedStartEpochMs;

            correctedEnd = incoming.CorrectedEndEpochMs;

            correctedStartIso = incoming.CorrectedStartIso;

            correctedEndIso = incoming.CorrectedEndIso;

            correctedAt = incoming.CorrectedAtMs;

            correctedBy = incoming.CorrectedBy;

        }



        if (incoming.VoidedAtMs is > 0 &&

            (existing.VoidedAtMs is null or 0 || incoming.VoidedAtMs > existing.VoidedAtMs))

        {

            voided = incoming.Voided;

            voidedAt = incoming.VoidedAtMs;

            voidedBy = incoming.VoidedBy;

            voidReason = incoming.VoidReason;

        }



        if (endEpochMs == existing.EndEpochMs &&

            endIso == existing.EndIso &&

            correctedStart == existing.CorrectedStartEpochMs &&

            correctedEnd == existing.CorrectedEndEpochMs &&

            correctedAt == existing.CorrectedAtMs &&

            voided == existing.Voided &&

            voidedAt == existing.VoidedAtMs &&

            voidReason == existing.VoidReason)

        {

            return existing;

        }



        return new ZeitwirtschaftMergedEntry

        {

            EntryId = existing.EntryId,

            PersonnelNumber = existing.PersonnelNumber,

            Name = existing.Name,

            DevicePhone = existing.DevicePhone,

            StartEpochMs = existing.StartEpochMs,

            EndEpochMs = endEpochMs,

            StartIso = existing.StartIso,

            EndIso = endIso,

            CorrectedStartEpochMs = correctedStart,

            CorrectedEndEpochMs = correctedEnd,

            CorrectedStartIso = correctedStartIso,

            CorrectedEndIso = correctedEndIso,

            CorrectedAtMs = correctedAt,

            CorrectedBy = correctedBy,

            Voided = voided,

            VoidedAtMs = voidedAt,

            VoidedBy = voidedBy,

            VoidReason = voidReason

        };

    }



    private sealed class DriverAccumulator(string personnelNumber)

    {

        public string PersonnelNumber { get; } = personnelNumber;

        public string Name { get; set; } = string.Empty;

        public Dictionary<string, ZeitwirtschaftMergedEntry> Entries { get; } =

            new(StringComparer.Ordinal);

    }

}

