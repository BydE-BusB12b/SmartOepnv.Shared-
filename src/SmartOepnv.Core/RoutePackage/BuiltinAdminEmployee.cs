namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Fester Admin-Zugang (nicht in Personalverwaltung sichtbar; App: PN 1995 / Passwort 2709 / Hauptnutzer;
/// Planer-Login: Name „Admin“. Passwort standardmäßig 4711, je Betrieb in den
/// Einstellungen änderbar (Prüfcode nötig).
/// </summary>
public static class BuiltinAdminEmployee
{
    public const string Name = "Admin";
    public const string PersonnelNumber = "1995";
    public const string EntryDate = "01.01.2026";
    public const string BusPassword = "2709";
    public const string PlannerPassword = "4711";

    /// <summary>Zusätzlicher Code, ohne den das Planer-Admin-Passwort nicht geändert werden darf.</summary>
    public const string PlannerPasswordChangeCode = "Kiara2026";

    public static bool IsAdminPersonnel(string? personnel) =>
        EmployeeRosterItem.NormalizePersonnelDigits(personnel) == PersonnelNumber;

    public static bool IsBuiltinAdmin(EmployeeRosterItem employee) =>
        IsAdminPersonnel(employee.PersonnelNumber) ||
        string.Equals(employee.Name?.Trim(), Name, StringComparison.Ordinal);

    public static EmployeeRosterItem Create() => new()
    {
        Name = Name,
        PersonnelNumber = PersonnelNumber,
        Password = BusPassword,
        LoginAsMainDevice = true,
        PlannerLoginEnabled = true,
        PlannerPassword = PlannerPassword,
        EntryDate = EntryDate
    };

    public static void ApplyFixedFields(EmployeeRosterItem employee)
    {
        if (!IsBuiltinAdmin(employee))
        {
            return;
        }

        employee.Name = Name;
        employee.PersonnelNumber = PersonnelNumber;
        employee.Password = BusPassword;
        employee.LoginAsMainDevice = true;
        employee.PlannerLoginEnabled = true;
        employee.PlannerPassword = PlannerPassword;
    }
}
