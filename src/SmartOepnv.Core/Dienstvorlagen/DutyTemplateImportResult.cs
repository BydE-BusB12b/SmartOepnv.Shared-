namespace SmartOepnv.Core.Dienstvorlagen;

public sealed class DutyTemplateImportHints
{
    public string Line { get; init; } = string.Empty;

    public string Route { get; init; } = string.Empty;

    public string VehicleNumber { get; init; } = string.Empty;

    public string LineCourse { get; init; } = string.Empty;

    public string Validity { get; init; } = string.Empty;
}

public sealed class DutyTemplateImportResult
{
    public IReadOnlyList<DutyTemplateImportRow> Rows { get; init; } = [];

    public DutyTemplateImportHints Hints { get; init; } = new();
}
