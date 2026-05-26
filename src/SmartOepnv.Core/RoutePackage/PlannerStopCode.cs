namespace SmartOepnv.Core.RoutePackage;

/// <summary>5-stellige Haltestellen-ID (nur Planer / Haltestellenverwaltung, nicht ITCS-Fahrtmodus).</summary>
public static class PlannerStopCode
{
    public const int DigitCount = 5;

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        if (digits.Length >= DigitCount)
        {
            return digits[^DigitCount..];
        }

        return digits.PadLeft(DigitCount, '0');
    }

    public static bool IsValid(string? raw) => Normalize(raw).Length == DigitCount;

    public static string SuggestNext(IEnumerable<string?> existingCodes)
    {
        var used = new HashSet<int>();
        foreach (var raw in existingCodes)
        {
            var norm = Normalize(raw);
            if (norm.Length == DigitCount &&
                int.TryParse(norm, out var n) &&
                n is >= 0 and <= 99_999)
            {
                used.Add(n);
            }
        }

        for (var i = 1; i <= 99_999; i++)
        {
            if (!used.Contains(i))
            {
                return i.ToString($"D{DigitCount}");
            }
        }

        return "00001";
    }
}
