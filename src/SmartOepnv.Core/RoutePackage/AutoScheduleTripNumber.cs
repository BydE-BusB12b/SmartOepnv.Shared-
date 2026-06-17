using System.Globalization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>4-stellige Fahrtnummern für die automatische Fahrplanerstellung.</summary>
public static class AutoScheduleTripNumber
{
    public const int DigitCount = 4;

    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var digits = new string(raw.Trim().Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || digits.Length > DigitCount)
        {
            return false;
        }

        normalized = digits.PadLeft(DigitCount, '0');
        return true;
    }

    public static string Format(int value) =>
        value.ToString($"D{DigitCount}", CultureInfo.InvariantCulture);
}
