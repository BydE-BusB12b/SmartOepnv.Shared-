namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Ein Fahrplanabschnitt innerhalb einer Dienstvorlage.</summary>
public sealed class DutyTemplateRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string TripNumber { get; set; } = string.Empty;

    public string LineCourse { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    public string FromTime { get; set; } = string.Empty;

    public string FromStop { get; set; } = string.Empty;

    public string ToTime { get; set; } = string.Empty;

    public string ToStop { get; set; } = string.Empty;

    public DutyTemplateRow Clone() => new()
    {
        Id = Id,
        TripNumber = TripNumber,
        LineCourse = LineCourse,
        Remark = Remark,
        Destination = Destination,
        FromTime = FromTime,
        FromStop = FromStop,
        ToTime = ToTime,
        ToStop = ToStop
    };
}
