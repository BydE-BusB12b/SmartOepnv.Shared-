using System.Text.Json;

namespace SmartOepnv.Core.RoutePackage;

public sealed class RegisteredVehicleInfo
{
    public required string Name { get; init; }
    public required string PhoneNumber { get; init; }

    /// <summary>Name des Hauptnutzers aus employeeRoster (gleiche Telefonnummer).</summary>
    public string? MainDeviceEmployeeName { get; init; }

    public static IReadOnlyList<RegisteredVehicleInfo> ParseFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var byPhone = new Dictionary<string, RegisteredVehicleInfo>(StringComparer.Ordinal);

        if (root.TryGetProperty("registeredVehicles", out var registered) &&
            registered.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in registered.EnumerateArray())
            {
                MergeRegisteredVehicle(byPhone, item);
            }
        }

        if (root.TryGetProperty("employeeRoster", out var roster) &&
            roster.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in roster.EnumerateArray())
            {
                MergeEmployeeRoster(byPhone, item);
            }
        }

        return byPhone.Values.ToList();
    }

    private static void MergeRegisteredVehicle(
        Dictionary<string, RegisteredVehicleInfo> byPhone,
        JsonElement item)
    {
        var phoneDigits = NormalizePhoneDigits(ReadPhone(item));
        if (phoneDigits is null)
        {
            return;
        }

        var name = ReadName(item) ?? phoneDigits;
        if (byPhone.TryGetValue(phoneDigits, out var existing))
        {
            byPhone[phoneDigits] = new RegisteredVehicleInfo
            {
                Name = name,
                PhoneNumber = existing.PhoneNumber,
                MainDeviceEmployeeName = existing.MainDeviceEmployeeName
            };
            return;
        }

        byPhone[phoneDigits] = new RegisteredVehicleInfo
        {
            Name = name,
            PhoneNumber = ReadPhone(item) ?? phoneDigits
        };
    }

    private static void MergeEmployeeRoster(
        Dictionary<string, RegisteredVehicleInfo> byPhone,
        JsonElement item)
    {
        var loginAsMainDevice = item.TryGetProperty("loginAsMainDevice", out var flag) &&
                                flag.ValueKind == JsonValueKind.True;
        if (!loginAsMainDevice)
        {
            return;
        }

        var phoneDigits = NormalizePhoneDigits(ReadPhone(item));
        if (phoneDigits is null)
        {
            return;
        }

        var employeeName = ReadName(item);
        if (string.IsNullOrWhiteSpace(employeeName))
        {
            return;
        }

        if (byPhone.TryGetValue(phoneDigits, out var existing))
        {
            byPhone[phoneDigits] = new RegisteredVehicleInfo
            {
                Name = existing.Name,
                PhoneNumber = existing.PhoneNumber,
                MainDeviceEmployeeName = employeeName
            };
            return;
        }

        byPhone[phoneDigits] = new RegisteredVehicleInfo
        {
            Name = employeeName,
            PhoneNumber = ReadPhone(item) ?? phoneDigits,
            MainDeviceEmployeeName = employeeName
        };
    }

    private static string? ReadPhone(JsonElement item) =>
        item.TryGetProperty("phoneNumber", out var phoneProp)
            ? phoneProp.GetString()?.Trim()
            : null;

    private static string? ReadName(JsonElement item) =>
        item.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString()?.Trim()
            : null;

    private static string? NormalizePhoneDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : null;
    }
}
