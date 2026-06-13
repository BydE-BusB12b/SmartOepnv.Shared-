using System.Text.Json.Serialization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Nur Planer: Fahrer, Fahrzeuge und Planer-Metadaten getrennt vom Routen-Paket.
/// Gelöschte Einträge bleiben als Tombstone erhalten, damit sie beim Import alter JSONs nicht zurückkommen.
/// </summary>
public sealed class PlannerLocalOverlayData
{
    public const string FileVersion = "1.0";

    public string Version { get; set; } = FileVersion;

    public long SavedAtUtc { get; set; }

    public List<EmployeeRosterItem> Employees { get; set; } = [];

    public List<RegisteredVehicleItem> Vehicles { get; set; } = [];

    public List<RegisteredVehiclePhoneRedirect> PhoneRedirects { get; set; } = [];

    /// <summary>4-stellig normalisierte Personalnummern, die im Planer gelöscht wurden.</summary>
    public List<string> DeletedEmployeePersonnel { get; set; } = [];

    /// <summary>Normalisierte Telefonnummern (nur Ziffern), wenn kein Personalnummer-Schlüssel vorhanden war.</summary>
    public List<string> DeletedEmployeePhones { get; set; } = [];

    /// <summary>Normalisierte KOM-Telefonnummern gelöschter Fahrzeuge.</summary>
    public List<string> DeletedVehiclePhoneKeys { get; set; } = [];

    /// <summary>Fahrzeugdisposition: Touren/Einsätze (nur Planer).</summary>
    public List<VehicleDispositionAssignment> VehicleDispositionAssignments { get; set; } = [];

    /// <summary>Fahrerdisposition: Dienste/Einsätze (nur Planer).</summary>
    public List<DriverDispositionAssignment> DriverDispositionAssignments { get; set; } = [];

    [JsonIgnore]
    public bool HasContent =>
        Employees.Count > 0 ||
        Vehicles.Count > 0 ||
        PhoneRedirects.Count > 0 ||
        DeletedEmployeePersonnel.Count > 0 ||
        DeletedEmployeePhones.Count > 0 ||
        DeletedVehiclePhoneKeys.Count > 0 ||
        VehicleDispositionAssignments.Count > 0 ||
        DriverDispositionAssignments.Count > 0;
}
