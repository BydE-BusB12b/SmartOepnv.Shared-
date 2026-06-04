namespace SmartOepnv.Core.RoutePath;

public enum RoutePathNodeType
{
    STOP,
    ANNOUNCEMENT,
    AUTO_WAYPOINT,
    MANUAL_WAYPOINT
}

public sealed class RoutePathLatLng
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}

public sealed class RoutePathNode
{
    public required string Id { get; set; }
    public RoutePathNodeType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SourceStopName { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
}

public sealed class RoutePathSegment
{
    public int Order { get; set; }
    public required string FromNodeId { get; set; }
    public required string ToNodeId { get; set; }
}

public sealed class RoutePathSnapManeuver
{
    public double DistanceM { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public string? CurrentStreet { get; set; }
    public string? NextStreet { get; set; }
    public string? NavSymbolType { get; set; }
}

public sealed class RoutePathDraft
{
    public const string DefaultNotes =
        "TODO spaeter: Fahrweg fuer genaue Verspaetungsberechnung nutzen; " +
        "bei Verlassen des Fahrwegs Hinweis anzeigen.";

    public string RouteName { get; set; } = string.Empty;
    public long CreatedAtEpochMs { get; set; }
    public long UpdatedAtEpochMs { get; set; }
    public string Notes { get; set; } = DefaultNotes;
    /// <summary>Hex-Farbe für gesnappte Routenlinien auf der Karte, z. B. #2196f3.</summary>
    public string RouteLineColor { get; set; } = "#2196f3";
    /// <summary>Zuletzt genutzter Kartenmittelpunkt (Breite).</summary>
    public double? MapViewLat { get; set; }
    /// <summary>Zuletzt genutzter Kartenmittelpunkt (Länge).</summary>
    public double? MapViewLon { get; set; }
    /// <summary>Zuletzt genutzter Leaflet-Zoom (ca. 3–19).</summary>
    public double? MapViewZoom { get; set; }
    public List<RoutePathNode> Nodes { get; set; } = [];
    public List<RoutePathSegment> Segments { get; set; } = [];
    public List<RoutePathLatLng> SnappedShape { get; set; } = [];
    public List<RoutePathSnapManeuver> SnappedManeuvers { get; set; } = [];
    public HashSet<string> RoadSnappedEdgeKeys { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> RoadBusStraightEdgeKeys { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<RoutePathLatLng>> RoadSegmentPolylines { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<RoutePathSnapManeuver>> RoadSegmentManeuvers { get; set; } = new(StringComparer.Ordinal);

    public static string SegmentEdgeKey(string fromNodeId, string toNodeId) =>
        $"{fromNodeId}\u0001{toNodeId}";
}
