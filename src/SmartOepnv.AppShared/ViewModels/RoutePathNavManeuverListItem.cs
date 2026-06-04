namespace SmartOepnv.AppShared.ViewModels;

public sealed class RoutePathNavManeuverListItem
{
    public RoutePathNavManeuverListItem(
        int displayNumber,
        string fromNodeId,
        string toNodeId,
        int maneuverIndex,
        string symbolTypeId,
        string instruction,
        int distanceM,
        string segmentLabel,
        Uri? iconUri,
        string mapMarkerKey)
    {
        DisplayNumber = displayNumber;
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
        ManeuverIndex = maneuverIndex;
        SymbolTypeId = symbolTypeId;
        Instruction = instruction;
        DistanceM = distanceM;
        SegmentLabel = segmentLabel;
        IconUri = iconUri;
        MapMarkerKey = mapMarkerKey;
        Title = $"{displayNumber}. {instruction}";
        Subtitle = $"{segmentLabel} · {distanceM} m";
    }

    public int DisplayNumber { get; }
    public string FromNodeId { get; }
    public string ToNodeId { get; }
    public int ManeuverIndex { get; }
    public string SymbolTypeId { get; }
    public string Instruction { get; }
    public int DistanceM { get; }
    public string SegmentLabel { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public Uri? IconUri { get; }
    public string MapMarkerKey { get; }
}

public sealed record NavSymbolPickerOption(string Id, string Label, Uri? IconUri);
