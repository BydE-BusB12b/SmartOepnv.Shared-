namespace SmartOepnv.Core.RoutePackage;

public sealed class WorkspaceMeta
{
    public DateTimeOffset LastSavedUtc { get; set; }
    public string Source { get; set; } = "local";
    public long? PackageTimestamp { get; set; }

    /// <summary>Zuletzt übernommenes <c>routes_update.json</c>-Timestamp (Leitstelle-Merge).</summary>
    public long? LastMergedRouteUpdateTimestamp { get; set; }

    /// <summary>Zuletzt übernommenes <c>leitstelle_routes.json</c>-Timestamp.</summary>
    public long? LastMergedLeitstelleRoutesTimestamp { get; set; }
}
