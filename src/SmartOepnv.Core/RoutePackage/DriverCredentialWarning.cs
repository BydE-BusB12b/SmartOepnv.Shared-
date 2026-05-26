namespace SmartOepnv.Core.RoutePackage;

public sealed class DriverCredentialWarning
{
    public DriverCredentialWarningLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public int SortKey { get; init; }
    /// <summary>4-stellige Personalnummer zur Auswahl in der Fahrerliste.</summary>
    public string PersonnelNumberNormalized { get; init; } = string.Empty;
    public int DaysUntilExpiry { get; init; }
}
