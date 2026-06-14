namespace SmartOepnv.Core.RoutePackage;

/// <summary>Fällige 3-Monats-Kontrollen (Führerschein, FQN, Fahrerkarte).</summary>
public static class EmployeeDocumentCheckWarningEvaluator
{
    private static readonly (string Label, Func<EmployeeRosterItem, string> GetExpiry, Func<EmployeeRosterItem, long> GetConfirmedAtUtcMs)[] Checks =
    [
        ("Führerscheinkontrolle", e => e.LicenseExpiry, e => e.LicenseCheckConfirmedAtUtcMs),
        ("FQN-Kontrolle", e => e.FqnExpiry, e => e.FqnCheckConfirmedAtUtcMs),
        ("Fahrerkartenkontrolle", e => e.DriverCardExpiry, e => e.DriverCardCheckConfirmedAtUtcMs)
    ];

    public static IList<EmployeeDocumentCheckWarning> Evaluate(IEnumerable<EmployeeRosterItem> employees)
    {
        var warnings = new List<EmployeeDocumentCheckWarning>();

        foreach (var employee in employees)
        {
            if (employee.IsDeprecatedDefaultCredential())
            {
                continue;
            }

            var label = BuildDriverLabel(employee);
            var personnelKey = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);

            foreach (var (checkLabel, getExpiry, getConfirmedAt) in Checks)
            {
                if (!EmployeeDocumentCheck.IsCheckRequired(getExpiry(employee), getConfirmedAt(employee)))
                {
                    continue;
                }

                warnings.Add(new EmployeeDocumentCheckWarning
                {
                    CheckLabel = checkLabel,
                    PersonnelNumberNormalized = personnelKey,
                    Message = $"Kontrolle fällig: {checkLabel} – {label}"
                });
            }
        }

        return warnings
            .OrderBy(w => w.Message, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int CountDueChecks(IEnumerable<EmployeeRosterItem> employees) =>
        Evaluate(employees).Count;

    private static string BuildDriverLabel(EmployeeRosterItem employee)
    {
        if (!string.IsNullOrWhiteSpace(employee.Name))
        {
            var name = employee.Name.Trim();
            if (!string.IsNullOrWhiteSpace(employee.PersonnelNumber))
            {
                return $"{name} (PN {employee.PersonnelNumber.Trim()})";
            }

            return name;
        }

        if (!string.IsNullOrWhiteSpace(employee.PersonnelNumber))
        {
            return $"PN {employee.PersonnelNumber.Trim()}";
        }

        return "unbenannt";
    }
}
