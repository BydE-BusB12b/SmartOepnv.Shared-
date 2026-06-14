namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Regel für automatische Leerfahrten zwischen zwei Haltestellen.</summary>
public sealed class DutyTemplateEmptyRunRule
{
    public string FromStop { get; set; } = string.Empty;

    public string ToStop { get; set; } = string.Empty;

    public int DurationMinutes { get; set; } = 3;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(FromStop)
        && !string.IsNullOrWhiteSpace(ToStop)
        && DurationMinutes > 0;
}
