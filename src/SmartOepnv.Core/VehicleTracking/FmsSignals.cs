using System.Globalization;
using System.Text.Json;

namespace SmartOepnv.Core.VehicleTracking;

/// <summary>
/// Vertrag der Fahrzeugdiagnose. Dieselben Schlüssel schickt der ESP32 per BLE
/// und die App legt sie in die Standort-JSON an den Planer.
/// </summary>
public static class FmsSignals
{
    public sealed record Signal(string Key, string Label, string Unit, bool DefaultForward);

    public static readonly IReadOnlyList<Signal> All =
    [
        new("speed", "Geschwindigkeit", "km/h", true),
        new("tank", "Tankstand", "%", true),
        new("fuelRate", "Verbrauch", "L/h", true),
        new("fuelUsed", "Kraftstoff gesamt", "L", false),
        new("km", "Kilometerstand", "km", true),
        new("doorCriterion", "Türkriterium", "", true),
        new("t1_open", "Tür vorn", "", true),
        new("t2_open", "Tür Mitte", "", true),
        new("t3_open", "Tür hinten", "", false),
        new("rpm", "Motordrehzahl", "1/min", false),
        new("coolant", "Kühlmittel", "°C", false),
        new("ambient", "Außentemperatur", "°C", false),
        new("engineHours", "Betriebsstunden", "h", false),
        new("accelerator", "Fahrpedal", "%", false),
        new("brake", "Bremse", "", false),
        new("parkingBrake", "Feststellbremse", "", false),
        new("clutch", "Kupplung", "", false),
        new("cruise", "Tempomat", "", false),
        new("ignition", "Zündung", "", false),
        new("engineOn", "Motor läuft", "", false),
        new("adblue", "AdBlue", "%", false),
        new("batteryVoltage", "Bordspannung", "V", false),
        new("serviceKm", "Wartung", "km", false)
    ];

    public const string DeviceName = "Citaro_FMS_Gateway";
    public const string ServiceUuid = "4fa2c730-1f8a-40a4-9e32-261f8a40a49e";
    public const string CharacteristicUuid = "beb5483e-36e1-4688-b7f5-ea07361b26a8";

    public static string Label(string key) =>
        All.FirstOrDefault(s => s.Key == key)?.Label ?? key;

    public static string Unit(string key) =>
        All.FirstOrDefault(s => s.Key == key)?.Unit ?? "";

    public static string Format(string key, JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.True => "Ja",
            JsonValueKind.False => "Nein",
            JsonValueKind.Number => Trim(value.GetDouble()),
            JsonValueKind.String => FormatString(value.GetString()),
            _ => "—"
        };
        var unit = Unit(key);
        return unit.Length == 0 || text is "Ja" or "Nein" or "—" ? text : $"{text} {unit}";
    }

    public static string FormatRaw(string key, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "—";
        }

        if (bool.TryParse(raw, out var flag))
        {
            return flag ? "Ja" : "Nein";
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var text = Trim(number);
            var unit = Unit(key);
            return unit.Length == 0 ? text : $"{text} {unit}";
        }

        return raw;
    }

    private static string FormatString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "—";
        }

        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return "Ja";
        }

        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return "Nein";
        }

        return raw;
    }

    private static string Trim(double value) =>
        value % 1d == 0d
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);

    public static Dictionary<string, string>? ReadObject(JsonElement parent)
    {
        if (!parent.TryGetProperty("fms", out var fms) || fms.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in fms.EnumerateObject())
        {
            map[property.Name] = Format(property.Name, property.Value);
        }

        return map.Count == 0 ? null : map;
    }
}
