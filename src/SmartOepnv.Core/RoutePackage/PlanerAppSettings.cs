namespace SmartOepnv.Core.RoutePackage;

public sealed class PlanerAppSettings
{
    public const int FileVersion = 5;

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

    /// <summary>Zuletzt vergebene packageVersion für routes_export.json.</summary>
    public long LastRoutesExportPackageVersion { get; set; }

    /// <summary>Zuletzt vergebene packageVersion für routes_update.json.</summary>
    public long LastRoutesUpdatePackageVersion { get; set; }
}
