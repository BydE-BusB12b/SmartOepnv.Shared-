namespace SmartOepnv.Core.RoutePackage;

public sealed class WorkspaceMeta
{
    public DateTimeOffset LastSavedUtc { get; set; }
    public string Source { get; set; } = "local";
    public long? PackageTimestamp { get; set; }
}
