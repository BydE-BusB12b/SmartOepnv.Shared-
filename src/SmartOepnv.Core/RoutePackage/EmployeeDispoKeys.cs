namespace SmartOepnv.Core.RoutePackage;

/// <summary>Eindeutiger Schlüssel für Fahrerdisposition.</summary>
public static class EmployeeDispoKeys
{
    private const string NamePrefix = "name:";
    private const string PersonnelPrefix = "pn:";

    public static string FromEmployee(EmployeeRosterItem employee)
    {
        var personnel = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
        if (personnel.Length > 0)
        {
            return PersonnelPrefix + personnel;
        }

        var phone = RegisteredVehiclesEditor.NormalizePhoneKey(employee.PhoneNumber);
        if (phone.Length > 0)
        {
            return phone;
        }

        var name = employee.Name.Trim();
        return name.Length > 0 ? NamePrefix + name : string.Empty;
    }

    public static bool KeysMatch(string storedKey, EmployeeRosterItem employee) =>
        string.Equals(storedKey, FromEmployee(employee), StringComparison.Ordinal);

    public static string? TryGetPersonnelDigits(string storedKey)
    {
        if (!storedKey.StartsWith(PersonnelPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var digits = storedKey[PersonnelPrefix.Length..];
        return digits.Length > 0 ? digits : null;
    }
}
