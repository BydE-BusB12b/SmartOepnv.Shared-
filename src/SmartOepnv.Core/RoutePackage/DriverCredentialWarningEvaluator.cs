using System.Globalization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Führerschein / FQN / Fahrerkarte – je Dokument eine Meldung (Gelb 90–1 Tage, Rot ab Fälligkeit).
/// </summary>
public static class DriverCredentialWarningEvaluator
{
    private const int YellowMaxDays = 90;

    private static readonly (string Label, Func<EmployeeRosterItem, string> GetExpiry)[] Credentials =
    [
        ("Führerschein", e => e.LicenseExpiry),
        ("FQN", e => e.FqnExpiry),
        ("Fahrerkarte", e => e.DriverCardExpiry)
    ];

    public static IList<DriverCredentialWarning> Evaluate(
        IEnumerable<EmployeeRosterItem> employees,
        DateOnly? today = null)
    {
        var reference = today ?? DateOnly.FromDateTime(DateTime.Today);
        var warnings = new List<DriverCredentialWarning>();

        foreach (var employee in employees)
        {
            if (employee.IsDeprecatedDefaultCredential())
            {
                continue;
            }

            AddWarningsForEmployee(warnings, employee, reference);
        }

        return warnings
            .OrderByDescending(w => w.SortKey)
            .ThenBy(w => w.Message, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddWarningsForEmployee(
        List<DriverCredentialWarning> warnings,
        EmployeeRosterItem employee,
        DateOnly today)
    {
        var label = BuildDriverLabel(employee);
        var personnelKey = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
        var kindOrder = 0;

        foreach (var (kindLabel, getExpiry) in Credentials)
        {
            kindOrder++;
            if (!TryParseExpiryDate(getExpiry(employee), out var dueDate))
            {
                continue;
            }

            var daysUntil = dueDate.DayNumber - today.DayNumber;
            if (daysUntil is >= 1 and <= YellowMaxDays)
            {
                warnings.Add(new DriverCredentialWarning
                {
                    Level = DriverCredentialWarningLevel.ExpiringSoon,
                    Message = $"!Hinweis: {kindLabel} von Fahrer {label} läuft in {daysUntil} Tagen ab!",
                    SortKey = 1000 + daysUntil * 10 - kindOrder,
                    PersonnelNumberNormalized = personnelKey,
                    DaysUntilExpiry = daysUntil
                });
                continue;
            }

            if (daysUntil <= 0)
            {
                warnings.Add(new DriverCredentialWarning
                {
                    Level = DriverCredentialWarningLevel.Expired,
                    Message =
                        $"!Achtung: Fahrer {label} nicht einsetzbar – {kindLabel} abgelaufen!",
                    SortKey = 2000 + Math.Abs(daysUntil) * 10 - kindOrder,
                    PersonnelNumberNormalized = personnelKey,
                    DaysUntilExpiry = daysUntil
                });
            }
        }
    }

    private static string BuildDriverLabel(EmployeeRosterItem employee)
    {
        if (!string.IsNullOrWhiteSpace(employee.Name))
        {
            return employee.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(employee.PersonnelNumber))
        {
            return $"PN {employee.PersonnelNumber.Trim()}";
        }

        return "unbenannt";
    }

    internal static bool TryParseExpiryDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (DateOnly.TryParseExact(
                trimmed,
                "dd.MM.yyyy",
                CultureInfo.GetCultureInfo("de-DE"),
                DateTimeStyles.None,
                out date))
        {
            return true;
        }

        return VehicleInspectionWarningEvaluator.TryParseInspectionDate(trimmed, out date);
    }
}
