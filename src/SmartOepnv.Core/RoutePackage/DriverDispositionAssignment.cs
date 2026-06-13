namespace SmartOepnv.Core.RoutePackage;

/// <summary>Planer: Dienst/Einsatz für einen Fahrer (Fahrerdisposition).</summary>
public sealed class DriverDispositionAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Schlüssel aus Personalnummer, Telefon oder Name (EmployeeDispoKeys).</summary>
    public string DriverKey { get; set; } = string.Empty;

    /// <summary>Gesamtbeginn (Teil 1 bei geteiltem Dienst).</summary>
    public long StartEpochMs { get; set; }

    /// <summary>Gesamtende (Teil 2 bei geteiltem Dienst).</summary>
    public long EndEpochMs { get; set; }

    /// <summary>Ende Teil 1 – nur bei geteiltem Dienst (&gt; 0).</summary>
    public long Part1EndEpochMs { get; set; }

    /// <summary>Beginn Teil 2 – nur bei geteiltem Dienst (&gt; 0).</summary>
    public long Part2StartEpochMs { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>Ruhezeit vor diesem Dienst wurde auf 9 h verkürzt (max. 2× pro Kalenderwoche).</summary>
    public bool ReducedRestBefore { get; set; }

    /// <summary>Lenkzeit an diesem Tag bis 10 h (max. 2× pro Kalenderwoche).</summary>
    public bool ExtendedDrivingDay { get; set; }

    /// <summary>Wochenruhe vor diesem Dienst auf 24 h verkürzt (max. 3× bis zur nächsten 45-h-Ruhe).</summary>
    public bool ReducedWeeklyRestBefore { get; set; }

    public bool IsSplitShift => Part1EndEpochMs > 0 && Part2StartEpochMs > Part1EndEpochMs;

    public IEnumerable<(long StartMs, long EndMs)> EnumerateWorkSegments()
    {
        if (IsSplitShift)
        {
            yield return (StartEpochMs, Part1EndEpochMs);
            yield return (Part2StartEpochMs, EndEpochMs);
        }
        else
        {
            yield return (StartEpochMs, EndEpochMs);
        }
    }

    public void ClearSplitParts()
    {
        Part1EndEpochMs = 0;
        Part2StartEpochMs = 0;
    }

    public void ApplySplitParts(long part1EndEpochMs, long part2StartEpochMs)
    {
        Part1EndEpochMs = part1EndEpochMs;
        Part2StartEpochMs = part2StartEpochMs;
    }

    public DriverDispositionAssignment Clone() => new()
    {
        Id = Id,
        DriverKey = DriverKey,
        StartEpochMs = StartEpochMs,
        EndEpochMs = EndEpochMs,
        Part1EndEpochMs = Part1EndEpochMs,
        Part2StartEpochMs = Part2StartEpochMs,
        Label = Label,
        Notes = Notes,
        ReducedRestBefore = ReducedRestBefore,
        ExtendedDrivingDay = ExtendedDrivingDay,
        ReducedWeeklyRestBefore = ReducedWeeklyRestBefore
    };
}
