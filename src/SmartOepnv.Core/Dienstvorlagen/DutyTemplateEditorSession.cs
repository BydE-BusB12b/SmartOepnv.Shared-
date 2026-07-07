namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Aktueller Bearbeitungsstand der Dienstvorlagen-Maske (Entwurf).</summary>
public sealed class DutyTemplateEditorSession
{
    public const int FileVersion = 8;

    public long SavedAtUtcMs { get; set; }

    public string? LoadedTemplateId { get; set; }

    public string TemplateName { get; set; } = string.Empty;

    public string CompanyLogoId { get; set; } = string.Empty;

    public string DutyNumber { get; set; } = string.Empty;

    public string DutyNumberPart2 { get; set; } = string.Empty;

    public string DutyNumberPart3 { get; set; } = string.Empty;

    public string Contractor { get; set; } = string.Empty;

    public string OperatingDay { get; set; } = string.Empty;

    public string VehicleNumber { get; set; } = string.Empty;

    public string DefaultLineCourse { get; set; } = string.Empty;

    public string ImportedLine { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string ImportFileName { get; set; } = string.Empty;

    public bool SubtractUnpaidBreak30Minutes { get; set; }

    public bool SubtractUnpaidBreak30MinutesPart2 { get; set; }

    public bool SubtractUnpaidBreak30MinutesPart3 { get; set; }

    public int CustomUnpaidBreakDeductionMinutes { get; set; }

    public int WorkPreparationMinutes { get; set; } = DutyTemplateCalculator.DefaultWorkPreparationMinutes;

    public int WorkFollowUpMinutes { get; set; } = DutyTemplateCalculator.DefaultWorkFollowUpMinutes;

    public List<DutyTemplateRow> Rows { get; set; } = [];

    public List<DutyTemplateRow> Part2Rows { get; set; } = [];

    public List<DutyTemplateRow> Part3Rows { get; set; } = [];

    public bool IsSplitShift { get; set; }

    public bool HasContent() =>
        SubtractUnpaidBreak30Minutes ||
        SubtractUnpaidBreak30MinutesPart2 ||
        SubtractUnpaidBreak30MinutesPart3 ||
        CustomUnpaidBreakDeductionMinutes > 0 ||
        !string.IsNullOrWhiteSpace(TemplateName) ||
        !string.IsNullOrWhiteSpace(CompanyLogoId) ||
        !string.IsNullOrWhiteSpace(DutyNumber) ||
        !string.IsNullOrWhiteSpace(DutyNumberPart2) ||
        !string.IsNullOrWhiteSpace(DutyNumberPart3) ||
        !string.IsNullOrWhiteSpace(Contractor) ||
        !string.IsNullOrWhiteSpace(OperatingDay) ||
        !string.IsNullOrWhiteSpace(VehicleNumber) ||
        !string.IsNullOrWhiteSpace(DefaultLineCourse) ||
        !string.IsNullOrWhiteSpace(ImportedLine) ||
        !string.IsNullOrWhiteSpace(Notes) ||
        !string.IsNullOrWhiteSpace(ImportFileName) ||
        Rows.Count > 0 ||
        Part2Rows.Count > 0 ||
        Part3Rows.Count > 0;
}
