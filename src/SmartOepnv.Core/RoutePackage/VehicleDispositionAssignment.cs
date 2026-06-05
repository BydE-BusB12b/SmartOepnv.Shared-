namespace SmartOepnv.Core.RoutePackage;

/// <summary>Planer: Tour/Einsatz auf einem Fahrzeug (Fahrzeugdisposition).</summary>
public sealed class VehicleDispositionAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Normalisierte KOM-Telefonnummer (nur Ziffern).</summary>
    public string VehiclePhone { get; set; } = string.Empty;

    public long StartEpochMs { get; set; }

    public long EndEpochMs { get; set; }

    public string Label { get; set; } = string.Empty;

    public string DriverName { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public VehicleDispositionAssignment Clone() => new()
    {
        Id = Id,
        VehiclePhone = VehiclePhone,
        StartEpochMs = StartEpochMs,
        EndEpochMs = EndEpochMs,
        Label = Label,
        DriverName = DriverName,
        Notes = Notes
    };
}
