using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Dienstvorlagen;

/// <summary>Wiederverwendbare Dienstvorlage für den Planer.</summary>
public sealed class DutyTemplate
{
    public const int FileVersion = 8;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Id des Firmenlogos aus den Einstellungen (PDF-Kopfzeile).</summary>
    public string CompanyLogoId { get; set; } = string.Empty;

    /// <summary>Dienstnummer Teil 1 (oder einziger Dienst).</summary>
    public string DutyNumber { get; set; } = string.Empty;

    /// <summary>Dienstnummer Teil 2 bei geteiltem Dienst.</summary>
    public string DutyNumberPart2 { get; set; } = string.Empty;

    /// <summary>Dienstnummer Teil 3 bei dreigeteiltem Dienst.</summary>
    public string DutyNumberPart3 { get; set; } = string.Empty;

    public string Contractor { get; set; } = string.Empty;

    public string OperatingDay { get; set; } = string.Empty;

    public string VehicleNumber { get; set; } = string.Empty;

    /// <summary>Linie/Kurs für alle Fahrten (Betrieb, z. B. 128/03).</summary>
    public string DefaultLineCourse { get; set; } = string.Empty;

    /// <summary>Im Fahrplan erkannte Linie (z. B. S28).</summary>
    public string ImportedLine { get; set; } = string.Empty;

    public long UpdatedAtUtcMs { get; set; }

    public string Notes { get; set; } = string.Empty;

    /// <summary>30 Minuten unbezahlte Pause von den Lohnstunden abziehen (Teil 1 / Einzeldienst).</summary>
    public bool SubtractUnpaidBreak30Minutes { get; set; }

    /// <summary>30 Minuten unbezahlte Pause für Teil 2 bei geteiltem Dienst.</summary>
    public bool SubtractUnpaidBreak30MinutesPart2 { get; set; }

    /// <summary>30 Minuten unbezahlte Pause für Teil 3 bei dreigeteiltem Dienst.</summary>
    public bool SubtractUnpaidBreak30MinutesPart3 { get; set; }

    /// <summary>Zusätzlicher flexibler Pausenabzug in Minuten (zusätzlich zur 30-Min.-Checkbox).</summary>
    public int CustomUnpaidBreakDeductionMinutes { get; set; }

    /// <summary>Arbeitsvorbereitung in Minuten (Standard 10).</summary>
    public int WorkPreparationMinutes { get; set; } = DutyTemplateCalculator.DefaultWorkPreparationMinutes;

    /// <summary>Arbeitsnachbereitung in Minuten (Standard 5).</summary>
    public int WorkFollowUpMinutes { get; set; } = DutyTemplateCalculator.DefaultWorkFollowUpMinutes;

    public List<DutyTemplateRow> Rows { get; set; } = [];

    /// <summary>Teil 2 bei geteiltem Dienst (Teil 1 = <see cref="Rows"/>).</summary>
    public List<DutyTemplateRow> Part2Rows { get; set; } = [];

    /// <summary>Teil 3 bei dreigeteiltem Dienst.</summary>
    public List<DutyTemplateRow> Part3Rows { get; set; } = [];

    [JsonIgnore]
    public bool IsSplitDuty => Part2Rows.Count > 0 || Part3Rows.Count > 0;

    [JsonIgnore]
    public bool IsThreePartDuty => Part3Rows.Count > 0;

    [JsonIgnore]
    public string Summary => BuildSummary();

    public string BuildSummary()
    {
        var when = UpdatedAtUtcMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs).ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : "–";
        var stats = DutyTemplateCalculator.ComputeSummary(this);
        var partLabel = IsSplitDuty && !string.IsNullOrWhiteSpace(DutyNumberPart2)
            ? $"{DutyNumber.Trim()} + {DutyNumberPart2.Trim()} · "
            : IsSplitDuty ? "2 Teile · " : string.Empty;
        return $"{partLabel}{stats.ServiceDurationDisplay} · {Rows.Count + Part2Rows.Count + Part3Rows.Count} Abschnitt(e) · {when}";
    }

    public DutyTemplate Clone() => new()
    {
        Id = Id,
        Name = Name,
        CompanyLogoId = CompanyLogoId,
        DutyNumber = DutyNumber,
        DutyNumberPart2 = DutyNumberPart2,
        DutyNumberPart3 = DutyNumberPart3,
        Contractor = Contractor,
        OperatingDay = OperatingDay,
        VehicleNumber = VehicleNumber,
        DefaultLineCourse = DefaultLineCourse,
        ImportedLine = ImportedLine,
        UpdatedAtUtcMs = UpdatedAtUtcMs,
        Notes = Notes,
        SubtractUnpaidBreak30Minutes = SubtractUnpaidBreak30Minutes,
        SubtractUnpaidBreak30MinutesPart2 = SubtractUnpaidBreak30MinutesPart2,
        SubtractUnpaidBreak30MinutesPart3 = SubtractUnpaidBreak30MinutesPart3,
        CustomUnpaidBreakDeductionMinutes = CustomUnpaidBreakDeductionMinutes,
        WorkPreparationMinutes = WorkPreparationMinutes,
        WorkFollowUpMinutes = WorkFollowUpMinutes,
        Rows = Rows.Select(r => r.Clone()).ToList(),
        Part2Rows = Part2Rows.Select(r => r.Clone()).ToList(),
        Part3Rows = Part3Rows.Select(r => r.Clone()).ToList()
    };
}
