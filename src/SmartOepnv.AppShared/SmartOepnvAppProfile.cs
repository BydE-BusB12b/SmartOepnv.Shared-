namespace SmartOepnv.AppShared;

public sealed class SmartOepnvAppProfile
{
    public required string ProductName { get; init; }
    public required string ProductSubtitle { get; init; }
    public required string DashboardHint { get; init; }
    public string PrimaryColorHex { get; init; } = "#0D47A1";
    public string PrimaryDarkColorHex { get; init; } = "#002171";
    public string AccentColorHex { get; init; } = "#42A5F5";
    public bool IsLeitstelle { get; init; }
    public bool AutoLoadDropboxOnStartup { get; init; }

    public static SmartOepnvAppProfile Planer { get; } = new()
    {
        ProductName = "Smart-ÖPNV Planer",
        ProductSubtitle = "Verwaltung · Routen · Ansagen · Navidaten",
        DashboardHint = "Route-Paket laden, bearbeiten und an GPSAnsagen-Fahrzeuge über Dropbox verteilen.",
        IsLeitstelle = false,
        AutoLoadDropboxOnStartup = false
    };

    public static SmartOepnvAppProfile Leitstelle { get; } = new()
    {
        ProductName = "Smart-ÖPNV Leitstelle",
        ProductSubtitle = "Überwachung · Routen · Versand · Fahrzeuge",
        DashboardHint = "Gleiche Datenbasis wie der Planer – Live-Überwachung folgt in einem späteren Schritt.",
        PrimaryColorHex = "#1B5E20",
        PrimaryDarkColorHex = "#003300",
        AccentColorHex = "#66BB6A",
        IsLeitstelle = true,
        AutoLoadDropboxOnStartup = false
    };
}
