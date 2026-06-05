using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Zeitwirtschaft;

/// <summary>Telefonnummer aus zeitwirtschaft_*.json → Anzeigename aus Fahrzeugverwaltung.</summary>
public static class ZeitwirtschaftVehicleLabelResolver
{
    public static Dictionary<string, string> BuildLabelMap(
        IEnumerable<RegisteredVehicleItem> vehicles,
        IEnumerable<RegisteredVehiclePhoneRedirect>? phoneRedirects = null)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var vehicle in vehicles)
        {
            var label = FormatLabel(vehicle);
            foreach (var key in CollectPhoneKeys(vehicle))
            {
                map[key] = label;
            }
        }

        if (phoneRedirects is not null)
        {
            foreach (var redirect in phoneRedirects)
            {
                var from = RegisteredVehiclesEditor.NormalizePhoneKey(redirect.FromPhoneNumber);
                var to = RegisteredVehiclesEditor.NormalizePhoneKey(redirect.ToPhoneNumber);
                if (from.Length == 0 || to.Length == 0 || !map.TryGetValue(to, out var label))
                {
                    continue;
                }

                map[from] = label;
            }
        }

        return map;
    }

    public static string Resolve(string phone, IReadOnlyDictionary<string, string>? labelMap)
    {
        var key = RegisteredVehiclesEditor.NormalizePhoneKey(phone);
        if (key.Length > 0 &&
            labelMap is not null &&
            labelMap.TryGetValue(key, out var label) &&
            !string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return phone;
    }

    private static IEnumerable<string> CollectPhoneKeys(RegisteredVehicleItem vehicle)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var current = RegisteredVehiclesEditor.NormalizePhoneKey(vehicle.PhoneNumber);
        if (current.Length > 0)
        {
            keys.Add(current);
        }

        var loaded = RegisteredVehiclesEditor.NormalizePhoneKey(vehicle.LoadedPhoneNumber);
        if (loaded.Length > 0)
        {
            keys.Add(loaded);
        }

        return keys;
    }

    private static string FormatLabel(RegisteredVehicleItem vehicle)
    {
        var name = vehicle.Name.Trim();
        var type = vehicle.PlannerDetails.VehicleType.Trim();

        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(type))
        {
            return $"{name} – {type}";
        }

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (!string.IsNullOrEmpty(type))
        {
            return type;
        }

        return vehicle.PhoneNumber;
    }
}
