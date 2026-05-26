using System.Text.Json;

namespace SmartOepnv.Core.RoutePackage;

public sealed class RegisteredVehicleInfo
{
    public required string Name { get; init; }
    public required string PhoneNumber { get; init; }

    public static IReadOnlyList<RegisteredVehicleInfo> ParseFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var vehicles = new List<RegisteredVehicleInfo>();
        var seenPhones = new HashSet<string>(StringComparer.Ordinal);

        if (root.TryGetProperty("registeredVehicles", out var registered) &&
            registered.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in registered.EnumerateArray())
            {
                AddVehicle(vehicles, seenPhones, item);
            }
        }

        if (vehicles.Count == 0 &&
            root.TryGetProperty("employeeRoster", out var roster) &&
            roster.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in roster.EnumerateArray())
            {
                AddVehicle(vehicles, seenPhones, item);
            }
        }

        return vehicles;
    }

    private static void AddVehicle(
        List<RegisteredVehicleInfo> vehicles,
        HashSet<string> seenPhones,
        JsonElement item)
    {
        var phone = item.TryGetProperty("phoneNumber", out var phoneProp)
            ? phoneProp.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || !seenPhones.Add(digits))
        {
            return;
        }

        var name = item.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString()?.Trim()
            : null;
        vehicles.Add(new RegisteredVehicleInfo
        {
            Name = string.IsNullOrWhiteSpace(name) ? digits : name!,
            PhoneNumber = phone
        });
    }
}
