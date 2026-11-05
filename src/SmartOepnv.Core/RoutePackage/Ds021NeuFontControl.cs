namespace SmartOepnv.Core.RoutePackage;

/// <summary>DS021neu-Schriftsteuerung im Steuerblock <c>.CM????</c> (wie GPSAnsagen).</summary>
public sealed class Ds021NeuFontControl
{
    public enum Weight
    {
        Normal,
        Bold
    }

    public Weight Line1Weight { get; set; } = Weight.Normal;
    public int Line1Height { get; set; }
    public Weight Line2Weight { get; set; } = Weight.Normal;
    public int Line2Height { get; set; }

    public static Ds021NeuFontControl Default { get; } = new();

    public bool IsDefaultNormal() =>
        Line1Weight == Weight.Normal &&
        Line1Height == 0 &&
        Line2Weight == Weight.Normal &&
        Line2Height == 0;

    public string ControlSuffix()
    {
        var h1 = Math.Clamp(Line1Height, 0, 9);
        var h2 = Math.Clamp(Line2Height, 0, 9);
        return $".CM{WeightCode(Line1Weight)}{h1}{WeightCode(Line2Weight)}{h2}";
    }

    public static Ds021NeuFontControl ParseStored(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Default;
        }

        var cleaned = raw.Trim().ToUpperInvariant();
        if (cleaned.StartsWith(".CM", StringComparison.Ordinal))
        {
            cleaned = cleaned[3..];
        }

        if (cleaned.Length != 4)
        {
            return Default;
        }

        return new Ds021NeuFontControl
        {
            Line1Weight = WeightFromCode(cleaned[0]),
            Line1Height = char.IsDigit(cleaned[1]) ? cleaned[1] - '0' : 0,
            Line2Weight = WeightFromCode(cleaned[2]),
            Line2Height = char.IsDigit(cleaned[3]) ? cleaned[3] - '0' : 0
        };
    }

    /// <summary>Kompakte Speicherform ohne <c>.CM</c>, z. B. <c>F6F5</c>.</summary>
    public static string? EncodeStored(Ds021NeuFontControl control)
    {
        if (control.IsDefaultNormal())
        {
            return null;
        }

        return control.ControlSuffix()[3..];
    }

    private static char WeightCode(Weight weight) => weight == Weight.Bold ? 'F' : 'N';

    private static Weight WeightFromCode(char code) =>
        char.ToUpperInvariant(code) == 'F' ? Weight.Bold : Weight.Normal;
}
