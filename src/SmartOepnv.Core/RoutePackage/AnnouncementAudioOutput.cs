namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Stereo-Zone für Ansagen (Tablet: L=Innen, R=Außen). JSON: innen | aussen | beide.
/// </summary>
public static class AnnouncementAudioOutput
{
    public const string Inside = "innen";
    public const string Outside = "aussen";
    public const string Both = "beide";

    public static string Normalize(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "outside" or "aussen" or "außen" or "out" => Outside,
            "both" or "beide" or "all" => Both,
            "inside" or "innen" or "in" => Inside,
            _ => Inside
        };

    public static string ToLabel(string? raw) =>
        Normalize(raw) switch
        {
            Outside => "Außen",
            Both => "Beide",
            _ => "Innen"
        };

    public static string Next(string? raw) =>
        Normalize(raw) switch
        {
            Inside => Outside,
            Outside => Both,
            _ => Inside
        };
}
