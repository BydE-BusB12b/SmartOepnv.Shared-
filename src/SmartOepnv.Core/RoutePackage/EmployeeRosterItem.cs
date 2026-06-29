namespace SmartOepnv.Core.RoutePackage;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>Mitarbeiter-Stammdaten (employeeRoster) – kompatibel zur Android-App.</summary>
public sealed class EmployeeRosterItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            var next = value ?? string.Empty;
            if (_name == next)
            {
                return;
            }

            _name = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(PlannerUsername));
            OnPropertyChanged(nameof(CanLoginToPlanner));
        }
    }

    public string PhoneNumber { get; set; } = string.Empty;

    private string _personnelNumber = string.Empty;
    public string PersonnelNumber
    {
        get => _personnelNumber;
        set
        {
            var next = value ?? string.Empty;
            if (_personnelNumber == next)
            {
                return;
            }

            _personnelNumber = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }
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

    /// <summary>Planer: Akte zuletzt gespeichert/bearbeitet (UTC ms).</summary>
    private long _lastEditedAtUtcMs;
    public long LastEditedAtUtcMs
    {
        get => _lastEditedAtUtcMs;
        set
        {
            if (_lastEditedAtUtcMs == value)
            {
                return;
            }

            _lastEditedAtUtcMs = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastEditedDisplayText));
        }
    }

    public string LastEditedDisplayText =>
        LastEditedAtUtcMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(LastEditedAtUtcMs)
                .ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm")
            : "–";

    public bool LicenseCheckConfirmationDue =>
        EmployeeDocumentCheck.IsCheckRequired(LicenseExpiry, LicenseCheckConfirmedAtUtcMs);

    public bool FqnCheckConfirmationDue =>
        EmployeeDocumentCheck.IsCheckRequired(FqnExpiry, FqnCheckConfirmedAtUtcMs);

    public bool DriverCardCheckConfirmationDue =>
        EmployeeDocumentCheck.IsCheckRequired(DriverCardExpiry, DriverCardCheckConfirmedAtUtcMs);

    public string LicenseCheckStatusText =>
        EmployeeDocumentCheck.FormatCheckStatus(LicenseExpiry, LicenseCheckConfirmedAtUtcMs);

    public string FqnCheckStatusText =>
        EmployeeDocumentCheck.FormatCheckStatus(FqnExpiry, FqnCheckConfirmedAtUtcMs);

    public string DriverCardCheckStatusText =>
        EmployeeDocumentCheck.FormatCheckStatus(DriverCardExpiry, DriverCardCheckConfirmedAtUtcMs);

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
            NotifyDocumentCheckChanged(nameof(LicenseCheckConfirmed));
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
            NotifyDocumentCheckChanged(nameof(FqnCheckConfirmed));
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
            NotifyDocumentCheckChanged(nameof(DriverCardCheckConfirmed));
        }
    }

    private void NotifyDocumentCheckChanged(string confirmedPropertyName)
    {
        OnPropertyChanged(confirmedPropertyName);
        switch (confirmedPropertyName)
        {
            case nameof(LicenseCheckConfirmed):
                OnPropertyChanged(nameof(LicenseCheckConfirmationDue));
                OnPropertyChanged(nameof(LicenseCheckStatusText));
                break;
            case nameof(FqnCheckConfirmed):
                OnPropertyChanged(nameof(FqnCheckConfirmationDue));
                OnPropertyChanged(nameof(FqnCheckStatusText));
                break;
            case nameof(DriverCardCheckConfirmed):
                OnPropertyChanged(nameof(DriverCardCheckConfirmationDue));
                OnPropertyChanged(nameof(DriverCardCheckStatusText));
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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

    public bool IsBuiltinAdmin => BuiltinAdminEmployee.IsBuiltinAdmin(this);

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
