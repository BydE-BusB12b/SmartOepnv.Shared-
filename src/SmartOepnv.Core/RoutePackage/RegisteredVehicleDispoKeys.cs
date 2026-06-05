namespace SmartOepnv.Core.RoutePackage;

/// <summary>Eindeutiger Schlüssel für Fahrzeugdisposition (Telefon oder Planer-Name).</summary>
public static class RegisteredVehicleDispoKeys
{
    private const string NamePrefix = "name:";

    public static string FromVehicle(RegisteredVehicleItem vehicle)
    {
        var phone = RegisteredVehiclesEditor.NormalizePhoneKey(vehicle.PhoneNumber);
        if (phone.Length > 0)
        {
            return phone;
        }

        var loaded = RegisteredVehiclesEditor.NormalizePhoneKey(vehicle.LoadedPhoneNumber);
        if (loaded.Length > 0)
        {
            return loaded;
        }

        var name = vehicle.Name.Trim();
        return name.Length > 0 ? NamePrefix + name : string.Empty;
    }

    public static bool KeysMatch(string storedKey, RegisteredVehicleItem vehicle) =>
        string.Equals(storedKey, FromVehicle(vehicle), StringComparison.Ordinal);

    public static bool KeysMatch(string storedKey, string? phoneNumber, string? loadedPhoneNumber, string? name)
    {
        var phone = RegisteredVehiclesEditor.NormalizePhoneKey(phoneNumber);
        if (phone.Length > 0 && string.Equals(storedKey, phone, StringComparison.Ordinal))
        {
            return true;
        }

        var loaded = RegisteredVehiclesEditor.NormalizePhoneKey(loadedPhoneNumber);
        if (loaded.Length > 0 && string.Equals(storedKey, loaded, StringComparison.Ordinal))
        {
            return true;
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        return trimmedName.Length > 0 &&
               string.Equals(storedKey, NamePrefix + trimmedName, StringComparison.Ordinal);
    }
}
