namespace SmartOepnv.Core.RoutePackage;

/// <summary>Auslösearten für haltestellengesteuerte Hinweise.</summary>
public static class RouteStopHintTrigger
{
    public const string WithAnnouncement = "withAnnouncement";
    public const string OwnGps = "ownGps";

    public static string Normalize(string? mode) =>
        string.Equals(mode, OwnGps, StringComparison.OrdinalIgnoreCase)
            ? OwnGps
            : WithAnnouncement;
}
