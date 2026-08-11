namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Datumsabhängiges Routenwechselziel an einer Endhaltestelle.
/// Leere <see cref="OperatingDates"/> = ungültig (nur Einträge mit Tagen zählen).
/// Standardziel ohne Datum bleibt <see cref="RouteStopItem.SelectedLineCourseTrip"/>.
/// </summary>
public sealed class RouteChangeTargetEntry
{
    public string SelectedLineCourseTrip { get; set; } = string.Empty;
    public List<DateOnly> OperatingDates { get; set; } = [];

    public RouteChangeTargetEntry Clone() => new()
    {
        SelectedLineCourseTrip = SelectedLineCourseTrip,
        OperatingDates = OperatingDates.ToList()
    };
}
