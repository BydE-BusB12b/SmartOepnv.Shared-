namespace SmartOepnv.Core.Mascot;

/// <summary>Ein Satz zum Tageswetter für den ersten Klick nach dem Start.</summary>
public static class MascotWeatherText
{
    public static string Describe(int weatherCode, int degrees)
    {
        var t = degrees.ToString(System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
        if (weatherCode is 56 or 57 or 66 or 67)
        {
            return $"Heute droht Blitzeis. Es werden heute {t} Grad.";
        }

        if (weatherCode is >= 95 and <= 99)
        {
            return $"Heute droht Gewitter. Es werden heute {t} Grad.";
        }

        if (weatherCode is 71 or 73 or 75 or 77 or 85 or 86)
        {
            return $"Heute fällt Schnee. Es werden heute {t} Grad.";
        }

        if (weatherCode is >= 51 and <= 65 or 80 or 81 or 82)
        {
            return $"Heute wird es nass. Es werden heute {t} Grad.";
        }

        if (weatherCode is 45 or 48)
        {
            return $"Heute bleibt es neblig bei {t} Grad.";
        }

        return $"Heute bleibt es trocken bei {t} Grad.";
    }
}
