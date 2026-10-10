using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Abweichendes Routenwechselziel an einer Endhaltestelle.
/// Gültig, wenn Folgefahrt gesetzt und mindestens ein Datum und/oder Verkehrstag gewählt ist.
/// Standardziel ohne Ausnahme bleibt <see cref="RouteStopItem.SelectedLineCourseTrip"/>.
/// </summary>
public sealed class RouteChangeTargetEntry
{
    public string SelectedLineCourseTrip { get; set; } = string.Empty;
    public List<DateOnly> OperatingDates { get; set; } = [];
    /// <summary>Wiederkehrende Verkehrstage (z. B. jeden Dienstag), zusätzlich oder statt konkreter Daten.</summary>
    public List<DutyOperatingDay> OperatingDays { get; set; } = [];

    public bool HasScheduleConstraint =>
        OperatingDates.Count > 0 || OperatingDays.Count > 0;

    public RouteChangeTargetEntry Clone() => new()
    {
        SelectedLineCourseTrip = SelectedLineCourseTrip,
        OperatingDates = OperatingDates.ToList(),
        OperatingDays = OperatingDays.ToList()
    };
}
