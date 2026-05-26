namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Dauerhafter Hinweis im Planer-Paket: Telefonnummer wurde geändert (andere PCs / App kennen das nicht).
/// JSON: <c>registeredVehiclesPlannerPhoneRedirects</c>
/// </summary>
public sealed class RegisteredVehiclePhoneRedirect
{
    public string FromPhoneNumber { get; set; } = string.Empty;
    public string ToPhoneNumber { get; set; } = string.Empty;
    public long RecordedAt { get; set; }
    /// <summary>z. B. „Nr. 0171… ist nun Nr. 0172…“</summary>
    public string Note { get; set; } = string.Empty;
}
