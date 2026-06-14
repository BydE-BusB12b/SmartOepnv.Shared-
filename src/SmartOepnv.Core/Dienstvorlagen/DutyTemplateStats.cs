namespace SmartOepnv.Core.Dienstvorlagen;

public sealed class DutyTemplateStats
{
    public int ServiceDurationMinutes { get; init; }

    public int PayMinutes { get; init; }

    public int BreakMinutes { get; init; }

    /// <summary>Summe ab/an-Zeiten je Zeile (inkl. Leerfahrten, ohne Leerzeilen).</summary>
    public int DrivingMinutes { get; init; }

    public int UnpaidBreakDeductedMinutes { get; init; }

    public int WorkPreparationMinutes { get; init; }

    public int WorkFollowUpMinutes { get; init; }

    public string ServiceDurationDisplay => DutyTemplateCalculator.FormatMinutes(ServiceDurationMinutes);

    public string PayHoursDisplay => DutyTemplateCalculator.FormatMinutes(PayMinutes);

    public string PureDrivingDisplay => DutyTemplateCalculator.FormatMinutes(DrivingMinutes);

    public string PureBreakDisplay =>
        BreakMinutes > 0 ? DutyTemplateCalculator.FormatMinutes(BreakMinutes) : "–";

    public string BreaksDisplay
    {
        get
        {
            if (BreakMinutes <= 0 && UnpaidBreakDeductedMinutes <= 0)
            {
                return "–";
            }

            var parts = new List<string>();
            if (BreakMinutes > 0)
            {
                parts.Add(DutyTemplateCalculator.FormatMinutes(BreakMinutes));
            }

            if (UnpaidBreakDeductedMinutes > 0)
            {
                parts.Add($"{DutyTemplateCalculator.FormatMinutes(UnpaidBreakDeductedMinutes)} unbez.");
            }

            return string.Join(" · ", parts);
        }
    }
}
