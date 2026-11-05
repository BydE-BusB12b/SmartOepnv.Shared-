using System.Text.Json.Nodes;

namespace SmartOepnv.Core.Verkauf;

public sealed class VerkaufSaleLine
{
    public required string ProductId { get; init; }
    public required string Name { get; init; }
    public int UnitPriceCents { get; init; }
    public int Qty { get; init; }
    public int LineTotalCents => UnitPriceCents * Qty;
}

public sealed class VerkaufSale
{
    public required string Id { get; init; }
    /// <summary>5-stellige fortlaufende Nr. des Verkaufsgeräts (nicht fahrergebunden).</summary>
    public string SaleNumber { get; init; } = string.Empty;
    /// <summary>3–4-stellige Wagennummer des Verkaufsgeräts.</summary>
    public string WagenNumber { get; init; } = string.Empty;
    /// <summary>Ticketnummer = SaleNumber + WagenNumber (gerätgebunden).</summary>
    public string TicketNumber { get; init; } = string.Empty;
    public long SoldAtMs { get; init; }
    public string SoldAtIso { get; init; } = string.Empty;
    public string VehiclePhone { get; init; } = string.Empty;
    public int TotalCents { get; init; }
    public bool Voided { get; init; }
    public long? VoidedAtMs { get; init; }
    public required IReadOnlyList<VerkaufSaleLine> Lines { get; init; }
}

public sealed class VerkaufDriverDocument
{
    public required string PersonnelNumber { get; init; }
    public required string Name { get; init; }
    public long UpdatedAtMs { get; init; }
    public bool Locked { get; init; }
    public long? UnlockedAtMs { get; init; }
    public string? UnlockedBy { get; init; }
    public long PeriodResetAtMs { get; init; }
    public long? FirstSaleAtMs { get; init; }
    public required IReadOnlyList<string> SalesDayDates { get; init; }
    public required IReadOnlyList<VerkaufSale> Sales { get; init; }
    public string SourceFileName { get; init; } = string.Empty;

    public const int MaxSalesDays = 5;

    /// <summary>Ein Betriebstag dauert 24 Stunden ab der Minute des ersten Verkaufs.</summary>
    public const long SalesDayMs = 24L * 60 * 60 * 1000;

    /// <summary>
    /// Der nächste Betriebstag beginnt eine Minute nach Ablauf der 24 Stunden
    /// (Verkauf 21:20 → Tag 2 ab 21:21).
    /// </summary>
    public const long SalesDayRollGraceMs = 60_000L;

    public int SalesDayCount => SalesDayDates.Count;

    /// <summary>Frühester Verkaufs- oder Stornozeitpunkt der aktuellen Periode.</summary>
    public long? FirstActivityMs
    {
        get
        {
            long? first = null;
            foreach (var sale in Sales)
            {
                if (sale.SoldAtMs > 0 && sale.SoldAtMs >= PeriodResetAtMs &&
                    (first is null || sale.SoldAtMs < first))
                {
                    first = sale.SoldAtMs;
                }

                if (sale.Voided && sale.VoidedAtMs is long voidAt &&
                    voidAt > 0 && voidAt >= PeriodResetAtMs &&
                    (first is null || voidAt < first))
                {
                    first = voidAt;
                }
            }

            if (first is null && FirstSaleAtMs is long stored && stored > 0 && stored >= PeriodResetAtMs)
            {
                first = stored;
            }

            return first;
        }
    }

    public bool IsPeriodExpired =>
        IsPeriodExpiredAt(FirstActivityMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>24-Stunden-Betriebstag: 1 ab dem ersten Verkauf, höchstens 5.</summary>
    public int ElapsedSalesDayCount =>
        CountSalesDays(FirstActivityMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>Lokaler Minutenanfang des ersten Vorgangs.</summary>
    public static long OperatingDayOriginMs(long firstMs)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(firstMs).ToLocalTime();
        var minute = new DateTimeOffset(
            local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Offset);
        return minute.ToUnixTimeMilliseconds();
    }

    public static int CountSalesDays(long? firstMs, long nowMs)
    {
        if (firstMs is not long first || first <= 0) return 0;
        var elapsed = nowMs - OperatingDayOriginMs(first);
        if (elapsed < 0) return 1;
        var index = (int)((elapsed - SalesDayRollGraceMs) / SalesDayMs) + 1;
        if (index < 1) return 1;
        return Math.Min(index, MaxSalesDays);
    }

    public static bool IsPeriodExpiredAt(long? firstMs, long nowMs)
    {
        if (firstMs is not long first || first <= 0) return false;
        var origin = OperatingDayOriginMs(first);
        return nowMs >= origin + MaxSalesDays * SalesDayMs + SalesDayRollGraceMs;
    }

    public string StatusLabel => Locked || IsPeriodExpired
        ? $"Gesperrt ({ElapsedSalesDayCount}/5 Tage)"
        : $"{ElapsedSalesDayCount}/5 Verkaufstage";

    public VerkaufDriverDocument WithDisplay(string name, string personnelNumber) => new()
    {
        PersonnelNumber = personnelNumber,
        Name = name,
        UpdatedAtMs = UpdatedAtMs,
        Locked = Locked,
        UnlockedAtMs = UnlockedAtMs,
        UnlockedBy = UnlockedBy,
        PeriodResetAtMs = PeriodResetAtMs,
        FirstSaleAtMs = FirstSaleAtMs,
        SalesDayDates = SalesDayDates,
        Sales = Sales,
        SourceFileName = SourceFileName
    };
}

public static class VerkaufDocumentParser
{
    public const string Type = "verkauf";
    public const string FilePrefix = "verkauf_";

    public static string BuildFileName(string personnelNumber) =>
        $"{FilePrefix}{NormalizePersonnel(personnelNumber)}.json";

    public static string NormalizePersonnel(string raw) =>
        new string(raw.Trim().Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());

    public static VerkaufDriverDocument? TryParse(string fileName, string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null) return null;
            if (!string.Equals(root["type"]?.GetValue<string>(), Type, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var pn = root["personnelNumber"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(pn) && fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                pn = Path.GetFileNameWithoutExtension(fileName)[FilePrefix.Length..];
            }

            var sales = new List<VerkaufSale>();
            if (root["sales"] is JsonArray arr)
            {
                foreach (var node in arr)
                {
                    if (node is not JsonObject o) continue;
                    var lines = new List<VerkaufSaleLine>();
                    if (o["lines"] is JsonArray lineArr)
                    {
                        foreach (var ln in lineArr)
                        {
                            if (ln is not JsonObject lo) continue;
                            lines.Add(new VerkaufSaleLine
                            {
                                ProductId = lo["productId"]?.GetValue<string>() ?? string.Empty,
                                Name = lo["name"]?.GetValue<string>() ?? string.Empty,
                                UnitPriceCents = lo["unitPriceCents"]?.GetValue<int>() ?? 0,
                                Qty = lo["qty"]?.GetValue<int>() ?? 0
                            });
                        }
                    }

                    sales.Add(new VerkaufSale
                    {
                        Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                        SaleNumber = o["saleNumber"]?.GetValue<string>() ?? string.Empty,
                        WagenNumber = o["wagenNumber"]?.GetValue<string>() ?? string.Empty,
                        TicketNumber = o["ticketNumber"]?.GetValue<string>() ?? string.Empty,
                        SoldAtMs = o["soldAtMs"]?.GetValue<long>() ?? 0L,
                        SoldAtIso = o["soldAtIso"]?.GetValue<string>() ?? string.Empty,
                        VehiclePhone = o["vehiclePhone"]?.GetValue<string>() ?? string.Empty,
                        TotalCents = o["totalCents"]?.GetValue<int>() ?? 0,
                        Voided = o["voided"]?.GetValue<bool>() == true,
                        VoidedAtMs = o["voidedAtMs"]?.GetValue<long?>(),
                        Lines = lines
                    });
                }
            }

            var days = new List<string>();
            if (root["salesDayDates"] is JsonArray dayArr)
            {
                foreach (var d in dayArr)
                {
                    var s = d?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(s)) days.Add(s);
                }
            }

            return new VerkaufDriverDocument
            {
                PersonnelNumber = pn,
                Name = root["name"]?.GetValue<string>() ?? pn,
                UpdatedAtMs = root["updatedAtMs"]?.GetValue<long>() ?? 0L,
                Locked = root["locked"]?.GetValue<bool>() == true,
                UnlockedAtMs = root["unlockedAtMs"]?.GetValue<long?>(),
                UnlockedBy = root["unlockedBy"]?.GetValue<string>(),
                PeriodResetAtMs = root["periodResetAtMs"]?.GetValue<long>() ?? 0L,
                FirstSaleAtMs = root["firstSaleAtMs"]?.GetValue<long?>(),
                SalesDayDates = days,
                Sales = sales.OrderByDescending(s => s.SoldAtMs).ToList(),
                SourceFileName = fileName
            };
        }
        catch
        {
            return null;
        }
    }

    public static VerkaufDriverDocument CreateEmpty(string personnelNumber, string name) => new()
    {
        PersonnelNumber = personnelNumber.Trim(),
        Name = name.Trim(),
        SalesDayDates = [],
        Sales = [],
        SourceFileName = BuildFileName(personnelNumber)
    };

    public static string BuildEmptyJson(string personnelNumber, string name)
    {
        var root = new JsonObject
        {
            ["type"] = Type,
            ["personnelNumber"] = personnelNumber.Trim(),
            ["name"] = name.Trim(),
            ["updatedAtMs"] = 0,
            ["locked"] = false,
            ["unlockedAtMs"] = null,
            ["unlockedBy"] = null,
            ["periodResetAtMs"] = 0,
            ["firstSaleAtMs"] = null,
            ["salesDayDates"] = new JsonArray(),
            ["sales"] = new JsonArray()
        };
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public static string ApplyUnlock(string json, string unlockedBy)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Ungültiges JSON.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        root["locked"] = false;
        root["unlockedAtMs"] = now;
        root["unlockedBy"] = unlockedBy;
        root["periodResetAtMs"] = now;
        root["salesDayDates"] = new JsonArray();
        root["firstSaleAtMs"] = null;
        root["updatedAtMs"] = now;
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
