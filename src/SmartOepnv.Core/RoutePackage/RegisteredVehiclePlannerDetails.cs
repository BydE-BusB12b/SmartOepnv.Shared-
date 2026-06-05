namespace SmartOepnv.Core.RoutePackage;

/// <summary>Zusatzdaten nur im Planer (JSON: registeredVehiclesPlannerMeta), nicht an die App.</summary>
public sealed class RegisteredVehiclePlannerDetails
{
    public string VehicleType { get; set; } = string.Empty;
    public string Vin { get; set; } = string.Empty;
    /// <summary>Gurte (Bemerkung oder Anzahl).</summary>
    public string SeatBelts { get; set; } = string.Empty;
    /// <summary>Klima (ja/nein/Bemerkung).</summary>
    public string Climate { get; set; } = string.Empty;
    public string PermittedTotalMassKg { get; set; } = string.Empty;
    public string EmptyWeightKg { get; set; } = string.Empty;
    /// <summary>ISO-Datum yyyy-MM-dd (Planer DatePicker).</summary>
    public string NextMainInspection { get; set; } = string.Empty;
    /// <summary>ISO-Datum yyyy-MM-dd (Planer DatePicker).</summary>
    public string NextSpInspection { get; set; } = string.Empty;

    /// <summary>Fahrzeug in der Disposition anzeigen (Planer, default aktiv).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Hintergrundfarbe der Dispo-Zeile (#RRGGBB), leer = Standard.</summary>
    public string DispoRowColor { get; set; } = string.Empty;

    public RegisteredVehiclePlannerDetails Clone() => new()
    {
        VehicleType = VehicleType,
        Vin = Vin,
        SeatBelts = SeatBelts,
        Climate = Climate,
        PermittedTotalMassKg = PermittedTotalMassKg,
        EmptyWeightKg = EmptyWeightKg,
        NextMainInspection = NextMainInspection,
        NextSpInspection = NextSpInspection,
        IsActive = IsActive,
        DispoRowColor = DispoRowColor
    };
}
