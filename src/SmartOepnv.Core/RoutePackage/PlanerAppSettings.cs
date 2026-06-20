namespace SmartOepnv.Core.RoutePackage;

public sealed class PlanerAppSettings
{
    public const int FileVersion = 3;

    /// <summary>Legacy: einzelnes Logo vor Mehrfach-Verwaltung.</summary>
    public string CompanyLogoFileName { get; set; } = string.Empty;

    public List<CompanyLogoEntry> CompanyLogos { get; set; } = [];

    /// <summary>Für Einweisungs-PDF: Gerätepasswort (betriebsweit, nicht pro Fahrer).</summary>
    public string DevicePassword { get; set; } = string.Empty;

    /// <summary>Für Einweisungs-PDF: Entsperrpasswort (Pause o. Ä., betriebsweit).</summary>
    public string UnlockPassword { get; set; } = string.Empty;
}
