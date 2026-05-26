namespace SmartOepnv.Core.RoutePackage;

public sealed class VehicleInspectionWarning
{
    public VehicleInspectionWarningLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public int SortKey { get; init; }

    /// <summary>Normalisierte Telefonnummer (nur Ziffern), damit zur Fahrzeugliste navigiert werden kann.</summary>
    public string PhoneNormalized { get; init; } = string.Empty;
}
