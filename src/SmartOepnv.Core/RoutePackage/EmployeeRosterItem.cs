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
    public bool LoginAsMainDevice { get; set; }

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
