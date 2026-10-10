namespace SmartOepnv.Core.RoutePackage;

public sealed class PlanerAppSettings
{
    public const int FileVersion = 10;

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

    /// <summary>Smart-ÖPNV-Logo in PDF-Kopfzeilen anzeigen (Planer-PDFs).</summary>
    public bool ShowSmartOepnvLogoInPdfs { get; set; } = true;

    /// <summary>
    /// Max. Zeichen für DS009-Eingaben im Planer (Haltestellenanzeige / Zieltext): 16 oder 20.
    /// Entspricht der Fahrzeug-Einstellung; steuert nur die Planer-Eingabelänge.
    /// </summary>
    public int Ds009TextLength { get; set; } = 20;
}
