using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Realtime.Vdv454;

/// <summary>
/// Betreiber-/Verbund-Konfiguration für einen späteren VDV-454-/SIRI-Ausgang.
/// Persistiert in <see cref="PlanerAppSettings"/> (Planer + Leitstelle).
/// </summary>
public sealed class Vdv454ParticipantConfig
{
    /// <summary>SIRI ParticipantRef / RequestorRef (z. B. „SMOEPNV“).</summary>
    public string ParticipantRef { get; init; } = "SMOEPNV";

    /// <summary>OperatorRef laut Verbund (oft IFOPT-/VDV-Code).</summary>
    public string OperatorRef { get; init; } = "SMOEPNV:Operator:1";

    /// <summary>Codespace-Präfix für lokale Refs (Line/Vehicle/Stop), bis Verbund-IDs stehen.</summary>
    public string LocalCodespace { get; init; } = "SMOEPNV";

    /// <summary>Optionaler Verbund-Codespace für StopPointRef (z. B. VRR), wenn <c>VrrStopId</c> gesetzt ist.</summary>
    public string? ExternalStopCodespace { get; init; } = "de:vrr";

    /// <summary>Produktionsflag: false = nur Vorbereitung / Dry-Run XML.</summary>
    public bool ExportEnabled { get; init; }

    public static Vdv454ParticipantConfig CreateDefault() => new();

    /// <summary>Lädt Verbund-Refs aus App-Einstellungen; leere Strings fallen auf Defaults zurück.</summary>
    public static Vdv454ParticipantConfig FromPlanerSettings(PlanerAppSettings? settings)
    {
        if (settings is null)
        {
            return CreateDefault();
        }

        return new Vdv454ParticipantConfig
        {
            ParticipantRef = OrDefault(settings.Vdv454ParticipantRef, "SMOEPNV"),
            OperatorRef = OrDefault(settings.Vdv454OperatorRef, "SMOEPNV:Operator:1"),
            LocalCodespace = OrDefault(settings.Vdv454LocalCodespace, "SMOEPNV"),
            ExternalStopCodespace = string.IsNullOrWhiteSpace(settings.Vdv454ExternalStopCodespace)
                ? null
                : settings.Vdv454ExternalStopCodespace.Trim(),
            ExportEnabled = false
        };
    }

    private static string OrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
