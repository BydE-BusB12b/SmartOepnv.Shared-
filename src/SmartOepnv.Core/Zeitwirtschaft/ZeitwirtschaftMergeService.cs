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

        IReadOnlyDictionary<string, string>? vehicleLabels = null,

        ZeitwirtschaftLegalContext? legal = null)

    {

        var rows = new List<ZeitwirtschaftTimeTableRow>();
        var assessment = ZeitwirtschaftCompliance.Assess(employee, legal, year, month, vehicleLabels);

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



            var isVoided = IsVoided(entry);

            if (!assessment.Figures.TryGetValue(entry.EntryId, out var figures))
            {
                figures = ZeitwirtschaftShiftFigures.Empty;
            }

            var pauseLines = DescribePauses(entry);
            var pauseText = pauseLines.Count == 0 ? "—" : string.Join("\n", pauseLines.Select(line => line.Text));
            var pauseMinutes = SumClosedPauseMinutes(entry.Pauses);
            var breakHint = string.Join("\n", figures.Hints);
            if (entry.Manual)
            {
                var nachtrag = string.IsNullOrWhiteSpace(entry.ManualReason)
                    ? "Nachtrag, Stempelung hat gefehlt."
                    : "Nachtrag: " + entry.ManualReason.Trim();
                breakHint = breakHint.Length == 0 ? nachtrag : nachtrag + "\n" + breakHint;
            }
            if (assessment.PeriodHints.TryGetValue(entry.EntryId, out var extra) && extra.Length > 0)
            {
                breakHint = breakHint.Length == 0 ? extra : breakHint + "\n" + extra;
            }

            string arbeitszeit;
            string lohnstunden;
            if (figures.Open)
            {
                (arbeitszeit, lohnstunden) = ("—", "—");
            }
            else if (isVoided)
            {
                arbeitszeit = FormatMinutesHhMm(figures.PresenceMinutes);
                lohnstunden = "—";
            }
            else
            {
                arbeitszeit = FormatMinutesHhMm(figures.PresenceMinutes);
                lohnstunden = FormatMinutesHhMm(figures.PayMinutes);
            }

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

                VehicleDisplayName = FormatPlace(
                    PlaceLabel(entry.DevicePhone, entry.CheckInPoint, vehicleLabels),
                    PlaceLabel(null, entry.CheckOutPoint, vehicleLabels),
                    ZeitwirtschaftVehicleLabelResolver.TryVehicleName(entry.DevicePhone, vehicleLabels)
                        ?? ZeitwirtschaftVehicleLabelResolver.Resolve(entry.DevicePhone, vehicleLabels)),

                PersonnelNumber = employee.PersonnelNumber,

                Kommen = kommen,

                Gehen = gehen,

                Arbeitszeit = arbeitszeit,

                Lohnstunden = lohnstunden,

                Pause = pauseText,

                PauseLines = pauseLines,

                PauseMinutes = isVoided ? 0 : pauseMinutes,

                Hinweis = breakHint,

                EntryId = entry.EntryId,

                HasCorrection = hasCorrection,

                IsVoided = isVoided,

                VoidReason = entry.VoidReason

            });

        }

        rows.AddRange(assessment.MissingRows);
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
        IsRealCorrection(entry.StartEpochMs, entry.CorrectedStartEpochMs) ||
        IsRealCorrection(entry.EndEpochMs ?? 0, entry.CorrectedEndEpochMs) ||
        entry.Pauses.Any(pause =>
            IsRealCorrection(pause.StartEpochMs, pause.CorrectedStartEpochMs) ||
            IsRealCorrection(pause.EndEpochMs ?? 0, pause.CorrectedEndEpochMs));

    /// <summary>Gespeicherte Korrektur, die sich in der angezeigten Minute von der Stempelzeit unterscheidet.</summary>
    public static bool IsRealCorrection(long originalMs, long? correctedMs) =>
        correctedMs is > 0 && (originalMs <= 0 || !SameMinute(originalMs, correctedMs.Value));

    public static bool SameMinute(long leftMs, long rightMs) =>
        leftMs > 0 &&
        rightMs > 0 &&
        FormatStamp(leftMs, null) == FormatStamp(rightMs, null);



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

        if (correctedMs is not > 0 ||
            (originalMs > 0 && SameMinute(originalMs, correctedMs.Value)))
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

    /// <summary>
    /// Eine Zeile je Pause. Orange (Ignored) sind Pausen, die nicht als Ruhepause zählen:
    /// kürzer als 15 Minuten, offen, oder außerhalb der Schicht.
    /// </summary>
    public static IReadOnlyList<ZeitwirtschaftPauseLine> DescribePauses(ZeitwirtschaftMergedEntry entry)
    {
        var shiftStart = EffectiveStartMs(entry);
        var shiftEnd = EffectiveEndMs(entry);
        var qualifying = shiftEnd is > 0
            ? ZeitwirtschaftArbzg.ClosedIntervals(shiftStart, shiftEnd.Value, entry.Pauses)
                .Where(interval => WholeMinutesBetween(interval.StartMs, interval.EndMs) >= ZeitwirtschaftArbzg.QualifyingSegmentMinutes)
                .ToList()
            : [];
        var lines = new List<ZeitwirtschaftPauseLine>();
        foreach (var pause in entry.Pauses.OrderBy(p => p.StartEpochMs))
        {
            if (pause.StartEpochMs <= 0)
            {
                continue;
            }

            var originalStart = DateTimeOffset.FromUnixTimeMilliseconds(pause.StartEpochMs).ToLocalTime();
            var originalEnd = pause.EndEpochMs is > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(pause.EndEpochMs.Value).ToLocalTime()
                : (DateTimeOffset?)null;
            var effectiveStart = DateTimeOffset.FromUnixTimeMilliseconds(pause.EffectiveStartMs).ToLocalTime();
            var effectiveEnd = pause.EffectiveEndMs is > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(pause.EffectiveEndMs.Value).ToLocalTime()
                : (DateTimeOffset?)null;
            var originalText = RangeText(originalStart, originalEnd);
            var changed = IsRealCorrection(pause.StartEpochMs, pause.CorrectedStartEpochMs) ||
                          IsRealCorrection(pause.EndEpochMs ?? 0, pause.CorrectedEndEpochMs);
            var text = changed
                ? originalText + "\n→ " + RangeText(effectiveStart, effectiveEnd)
                : originalText;
            lines.Add(new ZeitwirtschaftPauseLine
            {
                Text = text,
                Ignored = !PauseCounts(pause, shiftStart, shiftEnd, qualifying)
            });
        }

        return lines;
    }

    private static bool PauseCounts(
        ZeitwirtschaftPause pause,
        long shiftStartMs,
        long? shiftEndMs,
        IReadOnlyList<ZeitwirtschaftArbzg.Interval> qualifying)
    {
        if (pause.EffectiveEndMs is not long end)
        {
            return false;
        }

        if (shiftEndMs is not long shiftEnd)
        {
            return WholeMinutesBetween(pause.EffectiveStartMs, end) >= ZeitwirtschaftArbzg.QualifyingSegmentMinutes;
        }

        var start = pause.EffectiveStartMs;
        if (end <= shiftStartMs || start >= shiftEnd || end <= start)
        {
            return false;
        }

        if (start < shiftStartMs)
        {
            start = shiftStartMs;
        }

        if (end > shiftEnd)
        {
            end = shiftEnd;
        }

        return qualifying.Any(interval => start < interval.EndMs && end > interval.StartMs);
    }

    private static string RangeText(DateTimeOffset start, DateTimeOffset? end)
    {
        var startText = start.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (end is not DateTimeOffset finish)
        {
            return startText + "–offen";
        }

        var endText = finish.Date == start.Date
            ? finish.ToString("HH:mm", CultureInfo.CurrentCulture)
            : finish.ToString("dd.MM. HH:mm", CultureInfo.CurrentCulture);
        return startText + "–" + endText;
    }

    public static int SumClosedPauseMinutes(IReadOnlyList<ZeitwirtschaftPause> pauses)
    {
        var total = 0;
        foreach (var pause in pauses)
        {
            if (pause.EffectiveStartMs > 0 && pause.EffectiveEndMs is > 0)
            {
                total += WholeMinutesBetween(pause.EffectiveStartMs, pause.EffectiveEndMs.Value);
            }
        }

        return total;
    }

    public static string SumPauseMinutesHhMm(
        IEnumerable<ZeitwirtschaftTimeTableRow> rows,
        bool includeVoided = false)
    {
        var totalMinutes = rows
            .Where(r => includeVoided || !r.IsVoided)
            .Sum(r => r.PauseMinutes);
        return totalMinutes > 0 ? FormatMinutesHhMm(totalMinutes) : "—";
    }

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

    public static string SumDurationHhMm(IEnumerable<ZeitwirtschaftTimeTableRow> rows) =>
        SumFieldHhMm(rows, row => row.Arbeitszeit);

    public static string SumFieldHhMm(
        IEnumerable<ZeitwirtschaftTimeTableRow> rows,
        Func<ZeitwirtschaftTimeTableRow, string> field,
        bool includeVoided = false)
    {
        var totalMinutes = rows
            .Where(r => includeVoided || !r.IsVoided)
            .Select(r => ParseHhMmToMinutes(field(r)))
            .Where(v => v.HasValue)
            .Sum(v => v!.Value);
        return totalMinutes > 0 ? FormatMinutesHhMm(totalMinutes) : "—";
    }

    private static bool? ReadOptionalBool(JsonObject obj, string key)
    {
        if (obj[key] is JsonValue value && value.TryGetValue(out bool parsed))
        {
            return parsed;
        }

        return null;
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

            CheckInPoint = entryObj["checkInPoint"]?.GetValue<string>()?.Trim(),

            CheckOutPoint = entryObj["checkOutPoint"]?.GetValue<string>()?.Trim(),

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

            VoidReason = entryObj["voidReason"]?.GetValue<string>(),

            Pauses = ReadPauses(entryObj),

            SixthDuty = ReadOptionalBool(entryObj, "sixthDuty"),

            Manual = entryObj["manual"]?.GetValue<bool>() == true,

            ManualReason = entryObj["manualReason"]?.GetValue<string>()

        };

    }

    private static IReadOnlyList<ZeitwirtschaftPause> ReadPauses(JsonObject entryObj)
    {
        if (entryObj["pauses"] is not JsonArray arr)
        {
            return [];
        }

        var list = new List<ZeitwirtschaftPause>();
        foreach (var node in arr.OfType<JsonObject>())
        {
            var start = ReadPositiveLong(node, "startEpochMs");
            if (start is not > 0)
            {
                continue;
            }

            list.Add(new ZeitwirtschaftPause
            {
                StartEpochMs = start.Value,
                EndEpochMs = ReadPositiveLong(node, "endEpochMs"),
                CorrectedStartEpochMs = ReadPositiveLong(node, "correctedStartEpochMs"),
                CorrectedEndEpochMs = ReadPositiveLong(node, "correctedEndEpochMs")
            });
        }

        return list;
    }

    private static string? FirstPlace(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }

    /// <summary>
    /// Anmeldeort ist das Kennzeichen des Geräts. Ein beim Stempeln gespeicherter Name
    /// gilt nur, wenn die Nummer in der Fahrzeugliste fehlt.
    /// </summary>
    private static string? PlaceLabel(
        string? phone,
        string? stamped,
        IReadOnlyDictionary<string, string>? vehicleLabels)
    {
        var fromRegistry = ZeitwirtschaftVehicleLabelResolver.TryVehicleName(phone, vehicleLabels);
        if (!string.IsNullOrWhiteSpace(fromRegistry))
        {
            return fromRegistry;
        }

        return string.IsNullOrWhiteSpace(stamped) ? null : stamped.Trim();
    }

    private static string FormatPlace(string? checkIn, string? checkOut, string fallback)
    {
        var start = string.IsNullOrWhiteSpace(checkIn) ? null : checkIn.Trim();
        var end = string.IsNullOrWhiteSpace(checkOut) ? null : checkOut.Trim();
        if (start is not null && end is not null)
        {
            return string.Equals(start, end, StringComparison.Ordinal) ? start : $"{start} → {end}";
        }

        return start ?? end ?? fallback;
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

        var checkInPoint = FirstPlace(existing.CheckInPoint, incoming.CheckInPoint);

        var checkOutPoint = FirstPlace(existing.CheckOutPoint, incoming.CheckOutPoint);

        if (existing.EndEpochMs is not > 0 && incoming.EndEpochMs is > 0)

        {

            endEpochMs = incoming.EndEpochMs;

            endIso = incoming.EndIso;

            checkOutPoint = FirstPlace(incoming.CheckOutPoint, existing.CheckOutPoint);

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

        var pauses = MergePauses(existing.Pauses, incoming.Pauses);
        var incomingCorrectionWins = incoming.CorrectedAtMs is > 0 &&
            (existing.CorrectedAtMs is null or 0 || incoming.CorrectedAtMs > existing.CorrectedAtMs);
        if (incomingCorrectionWins)
        {
            pauses = OverlayPauseCorrections(pauses, incoming.Pauses);
        }

        var sixth = incoming.SixthDuty ?? existing.SixthDuty;



        if (endEpochMs == existing.EndEpochMs &&

            endIso == existing.EndIso &&

            correctedStart == existing.CorrectedStartEpochMs &&

            correctedEnd == existing.CorrectedEndEpochMs &&

            correctedAt == existing.CorrectedAtMs &&

            voided == existing.Voided &&

            voidedAt == existing.VoidedAtMs &&

            voidReason == existing.VoidReason &&

            SamePauses(pauses, existing.Pauses) &&

            sixth == existing.SixthDuty &&

            checkInPoint == existing.CheckInPoint &&

            checkOutPoint == existing.CheckOutPoint &&

            (existing.Manual || !incoming.Manual) &&

            (existing.ManualReason ?? incoming.ManualReason) == existing.ManualReason)

        {

            return existing;

        }



        return new ZeitwirtschaftMergedEntry

        {

            EntryId = existing.EntryId,

            PersonnelNumber = existing.PersonnelNumber,

            Name = existing.Name,

            DevicePhone = existing.DevicePhone,

            CheckInPoint = checkInPoint,

            CheckOutPoint = checkOutPoint,

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

            VoidReason = voidReason,

            Pauses = pauses,

            SixthDuty = sixth,

            Manual = existing.Manual || incoming.Manual,

            ManualReason = string.IsNullOrWhiteSpace(existing.ManualReason)
                ? incoming.ManualReason
                : existing.ManualReason

        };

    }

    private static IReadOnlyList<ZeitwirtschaftPause> MergePauses(
        IReadOnlyList<ZeitwirtschaftPause> existing,
        IReadOnlyList<ZeitwirtschaftPause> incoming)
    {
        var byStart = new Dictionary<long, ZeitwirtschaftPause>();
        AbsorbPauses(byStart, existing);
        AbsorbPauses(byStart, incoming);
        return byStart.OrderBy(p => p.Key).Select(p => p.Value).ToList();
    }

    private static void AbsorbPauses(Dictionary<long, ZeitwirtschaftPause> byStart, IReadOnlyList<ZeitwirtschaftPause> source)
    {
        foreach (var pause in source)
        {
            if (pause.StartEpochMs <= 0)
            {
                continue;
            }

            if (!byStart.TryGetValue(pause.StartEpochMs, out var previous))
            {
                byStart[pause.StartEpochMs] = pause;
                continue;
            }

            var end = LaterEnd(previous.EndEpochMs, pause.EndEpochMs);
            byStart[pause.StartEpochMs] = new ZeitwirtschaftPause
            {
                StartEpochMs = previous.StartEpochMs,
                EndEpochMs = end,
                CorrectedStartEpochMs = pause.CorrectedStartEpochMs ?? previous.CorrectedStartEpochMs,
                CorrectedEndEpochMs = pause.CorrectedEndEpochMs ?? previous.CorrectedEndEpochMs
            };
        }
    }

    private static long? LaterEnd(long? left, long? right)
    {
        if (left is not > 0)
        {
            return right;
        }

        if (right is not > 0)
        {
            return left;
        }

        return right > left ? right : left;
    }

    /// <summary>
    /// Die neuere Korrektur gilt vollständig, auch wenn eine Pausenzeit zurück auf die Stempelzeit gesetzt wurde.
    /// </summary>
    private static IReadOnlyList<ZeitwirtschaftPause> OverlayPauseCorrections(
        IReadOnlyList<ZeitwirtschaftPause> pauses,
        IReadOnlyList<ZeitwirtschaftPause> authority)
    {
        var byStart = authority.ToDictionary(pause => pause.StartEpochMs);
        return pauses.Select(pause =>
        {
            if (!byStart.TryGetValue(pause.StartEpochMs, out var source))
            {
                return pause;
            }

            return new ZeitwirtschaftPause
            {
                StartEpochMs = pause.StartEpochMs,
                EndEpochMs = pause.EndEpochMs,
                CorrectedStartEpochMs = source.CorrectedStartEpochMs,
                CorrectedEndEpochMs = source.CorrectedEndEpochMs
            };
        }).ToList();
    }

    private static bool SamePauses(IReadOnlyList<ZeitwirtschaftPause> left, IReadOnlyList<ZeitwirtschaftPause> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].StartEpochMs != right[i].StartEpochMs ||
                left[i].EndEpochMs != right[i].EndEpochMs ||
                left[i].CorrectedStartEpochMs != right[i].CorrectedStartEpochMs ||
                left[i].CorrectedEndEpochMs != right[i].CorrectedEndEpochMs)
            {
                return false;
            }
        }

        return true;
    }



    private sealed class DriverAccumulator(string personnelNumber)

    {

        public string PersonnelNumber { get; } = personnelNumber;

        public string Name { get; set; } = string.Empty;

        public Dictionary<string, ZeitwirtschaftMergedEntry> Entries { get; } =

            new(StringComparer.Ordinal);

    }

}

