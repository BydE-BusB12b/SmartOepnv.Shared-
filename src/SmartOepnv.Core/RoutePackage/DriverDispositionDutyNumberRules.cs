using System.Globalization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Jede Dienstnummer darf pro Kalendertag nur einmal in der Fahrerdisposition vorkommen.</summary>
public static class DriverDispositionDutyNumberRules
{
    /// <summary>Flexible Bereitschaft – keine FPersV-Zeitregeln, Darstellung lila.</summary>
    public const string StandbyDutyNumber = "Bereitschaft";

    public static bool IsStandbyDuty(string? dutyNumber) =>
        string.Equals(NormalizeDutyNumber(dutyNumber), StandbyDutyNumber, StringComparison.OrdinalIgnoreCase);

    public static bool IsStandbyAssignment(DriverDispositionAssignment assignment) =>
        IsStandbyDuty(ResolveDutyNumber(assignment));

    public static string ResolveDutyNumber(DriverDispositionAssignment assignment)
    {
        if (!string.IsNullOrWhiteSpace(assignment.DutyNumber))
        {
            return assignment.DutyNumber.Trim();
        }

        return assignment.Label.Trim();
    }

    public static string NormalizeDutyNumber(string? dutyNumber) =>
        (dutyNumber ?? string.Empty).Trim();

    public static DateTime GetAssignmentDutyDate(DriverDispositionAssignment assignment) =>
        DateTimeOffset.FromUnixTimeMilliseconds(assignment.StartEpochMs).LocalDateTime.Date;

    public static DateTime GetDutyDateFromStartEpochMs(long startEpochMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(startEpochMs).LocalDateTime.Date;

    public static bool TryFindConflictingDutyNumber(
        IEnumerable<DriverDispositionAssignment> assignments,
        string dutyNumber,
        DateTime dutyDate,
        string? excludeAssignmentId,
        out DriverDispositionAssignment? conflict)
    {
        conflict = null;
        var normalized = NormalizeDutyNumber(dutyNumber);
        if (normalized.Length == 0 || IsStandbyDuty(normalized))
        {
            // Bereitschaft darf mehrfach am selben Tag stehen (flexible Einsätze).
            return false;
        }

        var day = dutyDate.Date;
        foreach (var assignment in assignments)
        {
            if (excludeAssignmentId is not null &&
                string.Equals(assignment.Id, excludeAssignmentId, StringComparison.Ordinal))
            {
                continue;
            }

            if (GetAssignmentDutyDate(assignment) != day)
            {
                continue;
            }

            if (!string.Equals(
                    NormalizeDutyNumber(ResolveDutyNumber(assignment)),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            conflict = assignment;
            return true;
        }

        return false;
    }

    public static string BuildDuplicateMessage(string dutyNumber, DateTime dutyDate, string? existingDriverLabel = null)
    {
        var dateText = dutyDate.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"));
        var number = NormalizeDutyNumber(dutyNumber);
        if (string.IsNullOrWhiteSpace(existingDriverLabel))
        {
            return $"Dienstnummer {number} ist am {dateText} bereits vergeben.";
        }

        return $"Dienstnummer {number} ist am {dateText} bereits vergeben ({existingDriverLabel}).";
    }
}
