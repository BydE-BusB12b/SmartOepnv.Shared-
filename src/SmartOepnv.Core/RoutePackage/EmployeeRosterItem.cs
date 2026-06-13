namespace SmartOepnv.Core.RoutePackage;

/// <summary>Mitarbeiter-Stammdaten (employeeRoster) – kompatibel zur Android-App.</summary>
public sealed class EmployeeRosterItem
{
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PersonnelNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string LicenseExpiry { get; set; } = string.Empty;
    public string FqnExpiry { get; set; } = string.Empty;
    public string DriverCardExpiry { get; set; } = string.Empty;

    /// <summary>Planer: letzte Führerscheinkontrolle bestätigt (UTC ms).</summary>
    public long LicenseCheckConfirmedAtUtcMs { get; set; }

    /// <summary>Planer: letzte FQN-Kontrolle bestätigt (UTC ms).</summary>
    public long FqnCheckConfirmedAtUtcMs { get; set; }

    /// <summary>Planer: letzte Fahrerkartenkontrolle bestätigt (UTC ms).</summary>
    public long DriverCardCheckConfirmedAtUtcMs { get; set; }

    public bool LicenseCheckConfirmationDue => EmployeeDocumentCheck.IsDue(LicenseCheckConfirmedAtUtcMs);

    public bool FqnCheckConfirmationDue => EmployeeDocumentCheck.IsDue(FqnCheckConfirmedAtUtcMs);

    public bool DriverCardCheckConfirmationDue => EmployeeDocumentCheck.IsDue(DriverCardCheckConfirmedAtUtcMs);

    public string LicenseCheckStatusText => EmployeeDocumentCheck.FormatStatus(LicenseCheckConfirmedAtUtcMs);

    public string FqnCheckStatusText => EmployeeDocumentCheck.FormatStatus(FqnCheckConfirmedAtUtcMs);

    public string DriverCardCheckStatusText => EmployeeDocumentCheck.FormatStatus(DriverCardCheckConfirmedAtUtcMs);

    public bool LicenseCheckConfirmed
    {
        get => EmployeeDocumentCheck.IsValid(LicenseCheckConfirmedAtUtcMs);
        set
        {
            if (!value || !LicenseCheckConfirmationDue)
            {
                return;
            }

            LicenseCheckConfirmedAtUtcMs = EmployeeDocumentCheck.ConfirmNowUtcMs();
        }
    }

    public bool FqnCheckConfirmed
    {
        get => EmployeeDocumentCheck.IsValid(FqnCheckConfirmedAtUtcMs);
        set
        {
            if (!value || !FqnCheckConfirmationDue)
            {
                return;
            }

            FqnCheckConfirmedAtUtcMs = EmployeeDocumentCheck.ConfirmNowUtcMs();
        }
    }

    public bool DriverCardCheckConfirmed
    {
        get => EmployeeDocumentCheck.IsValid(DriverCardCheckConfirmedAtUtcMs);
        set
        {
            if (!value || !DriverCardCheckConfirmationDue)
            {
                return;
            }

            DriverCardCheckConfirmedAtUtcMs = EmployeeDocumentCheck.ConfirmNowUtcMs();
        }
    }

    /// <summary>Hauptnutzer am Bus-Gerät (App auf Handy/Tablet).</summary>
    public bool LoginAsMainDevice { get; set; }

    /// <summary>Planer-Anmeldung erlaubt – nur Planer, nicht in routes_export.json für Apps.</summary>
    public bool PlannerLoginEnabled { get; set; }

    public string PlannerPassword { get; set; } = string.Empty;

    public string PlannerUsername => Name.Trim();

    public bool CanLoginToPlanner =>
        PlannerLoginEnabled &&
        !string.IsNullOrWhiteSpace(PlannerUsername) &&
        !string.IsNullOrWhiteSpace(PlannerPassword);

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(PersonnelNumber)
            ? Name
            : $"{Name} (PN {PersonnelNumber})";

    public bool IsDeprecatedDefaultCredential()
    {
        var personnel = NormalizePersonnelDigits(PersonnelNumber);
        return (personnel == "2503" && Password == "2601") ||
               (personnel == "4711" && Password == "4711");
    }

    public static string NormalizePersonnelDigits(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return string.Empty;
        return digits.Length <= 4 ? digits.PadLeft(4, '0') : digits[^4..];
    }
}
