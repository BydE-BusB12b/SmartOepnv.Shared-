namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Treffer: gleiche Haltestellenfolge in einer anderen Route mit vorhandenen Nav-Verbindungen.
/// </summary>
public sealed class RoutePathNavReuseCandidate
{
    public required string SourceRouteKey { get; init; }
    public required int TargetFirstListIndex { get; init; }
    public required int TargetLastListIndex { get; init; }
    public required int SourceFirstListIndex { get; init; }
    public required int SourceLastListIndex { get; init; }
    public required IReadOnlyList<string> StopLabels { get; init; }
    public int SnappedEdgeCount { get; init; }

    /// <summary>Beste Quelle für diesen Ziel-Abschnitt (Standard-Häkchen im Dialog).</summary>
    public bool IsPreferredDefault { get; init; }

    public string FromLabel => StopLabels.Count > 0 ? StopLabels[0] : "?";
    public string ToLabel => StopLabels.Count > 0 ? StopLabels[^1] : "?";
}
