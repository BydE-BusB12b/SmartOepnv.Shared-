namespace SmartOepnv.Core.RoutePackage;

public sealed class PlanerAppSettings
{
    public const int FileVersion = 2;

    /// <summary>Legacy: einzelnes Logo vor Mehrfach-Verwaltung.</summary>
    public string CompanyLogoFileName { get; set; } = string.Empty;

    public List<CompanyLogoEntry> CompanyLogos { get; set; } = [];
}
