using System.Globalization;
using System.Text.Json;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.VehicleTracking;

public static class LocationChatParser
{
    public static VehicleLiveState? TryParse(
        string fileContent,
        string fileName,
        IReadOnlyList<RegisteredVehicleInfo> roster)
    {
        try
        {
            using var outer = JsonDocument.Parse(fileContent);
            var root = outer.RootElement;

            var phoneFromFile = root.TryGetProperty("phoneNumber", out var phoneProp)
                ? NormalizePhone(phoneProp.GetString())
                : ExtractPhoneFromFileName(fileName);
            var userName = root.TryGetProperty("userName", out var userProp)
                ? userProp.GetString()?.Trim()
                : null;
            var fileTimestamp = root.TryGetProperty("timestamp", out var tsProp)
                ? tsProp.GetInt64()
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using var payload = LocationChatCrypto.DecryptFromLocationChatFileContent(fileContent);
            var loc = payload.RootElement;

            var lat = ReadDouble(loc, "latitude");
            var lon = ReadDouble(loc, "longitude");
            if (!IsValidCoordinate(lat, lon))
            {
                return null;
            }

            var payloadPhone = loc.TryGetProperty("phoneNumber", out var pp)
                ? NormalizePhone(pp.GetString())
                : phoneFromFile;
            var payloadUser = loc.TryGetProperty("userName", out var pu)
                ? pu.GetString()?.Trim()
                : userName;

            var driverName = ReadOptionalString(loc, "driverName");
            var id = payloadPhone ?? phoneFromFile ?? ExtractDeviceIdFromFileName(fileName) ?? fileName;
            var displayName = ResolveDisplayName(
                id,
                payloadPhone ?? phoneFromFile,
                payloadUser ?? userName,
                roster);

            var timestamp = loc.TryGetProperty("timestamp", out var locTs)
                ? locTs.GetInt64()
                : fileTimestamp;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var status = VehicleLiveState.ComputeStatus(timestamp, now);

            var speedMs = ReadDouble(loc, "speed");
            var speedKmh = speedMs >= 0 ? (int)Math.Round(speedMs * 3.6) : 0;

            return new VehicleLiveState
            {
                Id = id,
                DisplayName = displayName,
                PhoneNumber = payloadPhone ?? phoneFromFile,
                Latitude = lat,
                Longitude = lon,
                AccuracyM = ReadDouble(loc, "accuracy"),
                SpeedKmh = Math.Max(0, speedKmh),
                LineCourse = ReadOptionalString(loc, "lineCourse"),
                RouteName = ReadOptionalString(loc, "route"),
                StopName = ReadOptionalString(loc, "stop"),
                Destination = ReadOptionalString(loc, "destination"),
                DriverName = driverName,
                DriverPersonnelNumber = ReadOptionalString(loc, "driverPersonnelNumber"),
                BatteryLevel = loc.TryGetProperty("batteryLevel", out var bat) && bat.TryGetInt32(out var b) && b >= 0 ? b : null,
                DelaySeconds = loc.TryGetProperty("delaySeconds", out var delay) && delay.TryGetInt32(out var d) ? d : null,
                TimestampEpochMs = timestamp,
                FileTimestampEpochMs = fileTimestamp,
                Status = status
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Karten-/Listenlabel: Fahrzeug (z. B. KOM2602), nicht der angemeldete Fahrer.
    /// Fahrer steht in <see cref="VehicleLiveState.DriverName"/> / Detail „Angemeldeter Fahrer“.
    /// </summary>
    private static string ResolveDisplayName(
        string id,
        string? phone,
        string? userName,
        IReadOnlyList<RegisteredVehicleInfo> roster)
    {
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var byPhone = roster.FirstOrDefault(v =>
                NormalizePhone(v.PhoneNumber) == phone);
            if (byPhone is not null)
            {
                var vehicleLabel = ResolveVehicleLabelFromRosterEntry(byPhone);
                if (!string.IsNullOrWhiteSpace(vehicleLabel))
                {
                    return vehicleLabel;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(userName) && !string.Equals(userName, "Unbekannt", StringComparison.OrdinalIgnoreCase))
        {
            var byName = roster.FirstOrDefault(v =>
                string.Equals(v.Name, userName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null && !string.IsNullOrWhiteSpace(byName.Name))
            {
                return byName.Name;
            }

            return ShortLabel(userName);
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return ShortLabel(userName);
        }

        return phone ?? id;
    }

    /// <summary>Fahrzeugname bevorzugen; nur bei reinem Telefon-Platzhalter Hauptnutzer-Name.</summary>
    private static string? ResolveVehicleLabelFromRosterEntry(RegisteredVehicleInfo entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Name))
        {
            var name = entry.Name.Trim();
            var phoneDigits = NormalizePhone(entry.PhoneNumber);
            if (phoneDigits is null || NormalizePhone(name) != phoneDigits)
            {
                return name;
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.MainDeviceEmployeeName))
        {
            return ShortLabel(entry.MainDeviceEmployeeName);
        }

        return string.IsNullOrWhiteSpace(entry.Name) ? null : entry.Name;
    }

    public static string ShortLabel(string name)
    {
        if (name.StartsWith("Smart-ÖPNV", StringComparison.OrdinalIgnoreCase))
        {
            return "Smart-ÖPNV";
        }

        return name.Length > 18 ? name[..17] + "…" : name;
    }

    private static string? ExtractPhoneFromFileName(string fileName)
    {
        const string prefix = "location_chat_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var digits = stem[prefix.Length..];
        return digits.All(char.IsDigit) ? digits : null;
    }

    private static string? ExtractDeviceIdFromFileName(string fileName)
    {
        const string prefix = "location_chat_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFileNameWithoutExtension(fileName)[prefix.Length..];
    }

    private static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : null;
    }

    private static string? ReadOptionalString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var prop))
        {
            return null;
        }

        var s = prop.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static double ReadDouble(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var prop))
        {
            return double.NaN;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDouble(),
            JsonValueKind.String when double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => double.NaN
        };
    }

    private static bool IsValidCoordinate(double lat, double lon) =>
        double.IsFinite(lat) &&
        double.IsFinite(lon) &&
        Math.Abs(lat) <= 90 &&
        Math.Abs(lon) <= 180 &&
        !(Math.Abs(lat) < 0.0001 && Math.Abs(lon) < 0.0001);
}
