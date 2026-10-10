namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Speicherung im Planer: nur Wabe (3 Ziffern) + optional Kurzstrecke.
/// Linie (DS004 Ziffer 1–3) setzt die App zur Fahrt aus der Route.
/// DS004a Voreinstellung: <c>0031</c> (Kurzstrecke 3, Druckfreigabe 1).
/// </summary>
public static class EntwerterStopCode
{
    /// <summary>DS004a Ziffer 3, wenn keine Kurzstrecke gespeichert ist → Telegramm <c>eA0031</c>.</summary>
    public const int DefaultKurzstrecke = 3;

    /// <summary>Voreingestellte DS004a-Ziffern (ohne <c>eA</c>-Präfix).</summary>
    public const string DefaultDs004aFour = "0031";

    public static string ExtractWabe(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        return digits.Length >= 3 ? digits[..3] : digits.PadLeft(3, '0');
    }

    public static int ExtractKurzstrecke(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            >= 7 => digits[6] - '0',
            4 => digits[3] - '0',
            _ => 0
        };
    }

    /// <summary>
    /// Kurzstrecke für Anzeige/Telegramm: gespeicherte Ziffer, sonst <see cref="DefaultKurzstrecke"/> (DS004a <c>0031</c>).
    /// Explizit gespeicherte <c>0</c> (4. Ziffer) bleibt 0.
    /// </summary>
    public static int EffectiveKurzstrecke(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            >= 7 => digits[6] - '0',
            4 => digits[3] - '0',
            _ => DefaultKurzstrecke
        };
    }

    public static string FormatDs004a(string? rawEntwerterCode) =>
        $"eA00{EffectiveKurzstrecke(rawEntwerterCode)}1";

    public static string Store(string? wabe, int kurzstrecke)
    {
        var rawDigits = new string((wabe ?? string.Empty).Where(char.IsDigit).ToArray());
        // Leeres Feld oder nur Nullen = keine Wabe (Löschen muss greifen)
        if (rawDigits.Length == 0 || rawDigits.Trim('0').Length == 0)
        {
            return string.Empty;
        }

        var w = ExtractWabe(rawDigits);
        if (string.IsNullOrEmpty(w))
        {
            return string.Empty;
        }

        // Kurzstrecke immer mitschreiben (auch 0), damit Default 3 persistent wird
        var k = Math.Clamp(kurzstrecke, 0, 9);
        return $"{w}{k}";
    }

    public static string PadDrei(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        return digits.Length >= 3 ? digits[^3..] : digits.PadLeft(3, '0');
    }
}
