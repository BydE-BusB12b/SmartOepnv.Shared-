namespace SmartOepnv.Core.RoutePackage;

public sealed class PlanerAppSettings
{
    public const int FileVersion = 13;

    /// <summary>Legacy: einzelnes Logo vor Mehrfach-Verwaltung.</summary>
    public string CompanyLogoFileName { get; set; } = string.Empty;

    public List<CompanyLogoEntry> CompanyLogos { get; set; } = [];

    /// <summary>Für Einweisungs-PDF: Gerätepasswort (betriebsweit, nicht pro Fahrer).</summary>
    public string DevicePassword { get; set; } = string.Empty;

    /// <summary>Für Einweisungs-PDF: Entsperrpasswort (Pause o. Ä., betriebsweit).</summary>
    public string UnlockPassword { get; set; } = string.Empty;

    /// <summary>
    /// Dateiname der Sondergong-Tondatei (unter Einstellungen/ansagen_sounds).
    /// Leer = noch nicht konfiguriert.
    /// </summary>
    public string SondergongFileName { get; set; } = string.Empty;

    /// <summary>
    /// Zusätzliche Linien-Bausteine für die Spezialbausteine-Bibliothek
    /// (z. B. S28, RE10) – unabhängig von Routen und Haltestellenansagen.
    /// </summary>
    public List<string> SpecialBuildingBlockLines { get; set; } = [];

    /// <summary>
    /// Aus der Spezialbausteine-Liste ausgeblendete Linien (auch wenn sie aus Routen kommen),
    /// z. B. Kursnummern wie 001/01.
    /// </summary>
    public List<string> SpecialBuildingBlockLinesHidden { get; set; } = [];

    /// <summary>
    /// Tondatei-/Namens-Schlüssel: nicht wieder automatisch aus Haltestellenvorlagen in die Kartei übernehmen.
    /// </summary>
    public List<string> SuppressedStopAnnouncementSoundKeys { get; set; } = [];

    /// <summary>Zuletzt vergebene packageVersion für routes_export.json.</summary>
    public long LastRoutesExportPackageVersion { get; set; }

    /// <summary>Zuletzt vergebene packageVersion für routes_update.json.</summary>
    public long LastRoutesUpdatePackageVersion { get; set; }

    /// <summary>UTC-Zeitpunkt des letzten erfolgreichen Versands ans Fahrzeug (0 = noch nie).</summary>
    public long LastVehicleSendUtcMs { get; set; }

    /// <summary>Smart-ÖPNV-Logo in PDF-Kopfzeilen anzeigen (Planer-PDFs).</summary>
    public bool ShowSmartOepnvLogoInPdfs { get; set; } = true;

    /// <summary>
    /// Heller UI-Modus (Planer/Leitstelle): helle Flächen, dunkelblaue Schrift.
    /// Standard bleibt Dark Mode.
    /// </summary>
    public bool UseLightTheme { get; set; }

    /// <summary>
    /// Max. Zeichen für DS009-Eingaben im Planer (Haltestellenanzeige / Zieltext): 16 oder 20.
    /// Entspricht der Fahrzeug-Einstellung; steuert nur die Planer-Eingabelänge.
    /// </summary>
    public int Ds009TextLength { get; set; } = 20;

    /// <summary>SIRI ParticipantRef / RequestorRef für VDV-454-Vorbereitung.</summary>
    public string Vdv454ParticipantRef { get; set; } = "SMOEPNV";

    /// <summary>OperatorRef laut Verbund (IFOPT-/VDV-Code).</summary>
    public string Vdv454OperatorRef { get; set; } = "SMOEPNV:Operator:1";

    /// <summary>Lokaler Codespace für Line/Vehicle/Stop/Journey-Refs.</summary>
    public string Vdv454LocalCodespace { get; set; } = "SMOEPNV";

    /// <summary>Verbund-Codespace für StopPointRef bei VrrStopId (z. B. de:vrr).</summary>
    public string Vdv454ExternalStopCodespace { get; set; } = "de:vrr";

    /// <summary>Raspberry-Pi-Adresse der SEV-TFT-Webanzeige. Leer = noch nicht eingerichtet.</summary>
    public string SevTftHost { get; set; } = string.Empty;

    /// <summary>HTTP-Port der SEV-TFT-API. Der eingerichtete Dienst hört auf 8080.</summary>
    public int SevTftPort { get; set; } = 8080;

    /// <summary>Übertragung an die SEV-TFT ist eingeschaltet.</summary>
    public bool SevTftEnabled { get; set; }

    /// <summary>Beim Öffnen einer Route Linie, Ziel und Haltestellen senden.</summary>
    public bool SevTftAutoSend { get; set; } = true;
}
