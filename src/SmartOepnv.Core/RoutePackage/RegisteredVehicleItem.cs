namespace SmartOepnv.Core.RoutePackage;

/// <summary>KOM-/Fahrzeug-Gerät (registeredVehicles) – kompatibel zur Android-App.</summary>
public sealed class RegisteredVehicleItem
{
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PersonnelNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string LicenseExpiry { get; set; } = string.Empty;
    public string FqnExpiry { get; set; } = string.Empty;
    public string DriverCardExpiry { get; set; } = string.Empty;
    public bool LoginAsMainDevice { get; set; }

    /// <summary>Nur Planer – wird nicht in registeredVehicles an die App übertragen.</summary>
    public RegisteredVehiclePlannerDetails PlannerDetails { get; set; } = new();

    /// <summary>Telefonnummer beim letzten Laden/Speichern (Planer: Erkennung von Nummernwechsel).</summary>
    public string LoadedPhoneNumber { get; set; } = string.Empty;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name)
            ? PhoneNumber
            : $"{Name} – {PhoneNumber}";
}
