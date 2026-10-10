namespace SmartOepnv.Core.Voip;

public static class VoipPhone
{
    /// <summary>Telefonnummer → nur Ziffern.</summary>
    public static string Normalize(string? raw) =>
        new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>Peer-ID für Signaling: „dispatch“ bleibt erhalten, Fahrzeuge als Ziffern.</summary>
    public static string NormalizePeerId(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (string.Equals(value, VoipConstants.RoleDispatch, StringComparison.OrdinalIgnoreCase))
        {
            return VoipConstants.RoleDispatch;
        }

        return Normalize(value);
    }

    public static string ConfigFileNameForPhone(string phoneRaw)
    {
        var normalized = Normalize(phoneRaw);
        return string.IsNullOrEmpty(normalized)
            ? string.Empty
            : $"{VoipConstants.VehicleConfigPrefix}{normalized}.json";
    }

    /// <summary>Alle gängigen Dropbox-Dateinamen (0151… vs. 49151…).</summary>
    public static IReadOnlyList<string> ConfigFileNameVariantsForPhone(string? phoneRaw)
    {
        var variants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var digits in PhoneDigitVariants(phoneRaw))
        {
            variants.Add($"{VoipConstants.VehicleConfigPrefix}{digits}.json");
        }

        return variants.ToArray();
    }

    public static IEnumerable<string> PhoneDigitVariants(string? phoneRaw)
    {
        var digits = Normalize(phoneRaw);
        if (string.IsNullOrEmpty(digits))
        {
            yield break;
        }

        yield return digits;

        var german = NormalizeGermanKey(phoneRaw);
        if (!string.IsNullOrEmpty(german) && !string.Equals(german, digits, StringComparison.Ordinal))
        {
            yield return german;
        }

        if (digits.StartsWith('0') && digits.Length > 1)
        {
            yield return "49" + digits[1..];
        }

        if (digits.StartsWith("49", StringComparison.Ordinal) && digits.Length > 2)
        {
            yield return "0" + digits[2..];
        }
    }

    /// <summary>Vergleicht Nummern trotz 0/49-Präfix (z. B. 0177… vs. 49177…).</summary>
    public static bool Match(string? a, string? b)
    {
        var da = NormalizeGermanKey(a);
        var db = NormalizeGermanKey(b);
        return !string.IsNullOrEmpty(da) && da == db;
    }

    public static string NormalizeGermanKey(string? raw)
    {
        var digits = Normalize(raw);
        if (string.IsNullOrEmpty(digits))
        {
            return string.Empty;
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (digits.StartsWith('0'))
        {
            digits = "49" + digits[1..];
        }

        return digits;
    }
}
