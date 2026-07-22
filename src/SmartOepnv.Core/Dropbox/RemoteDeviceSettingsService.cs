using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Entspricht GPSAnsagen <c>RemoteDeviceSettings</c>: Tablet-Einstellungen per Dropbox.
/// Dateiname: <c>remote_settings_(telefon).json</c>.
/// </summary>
public static class RemoteDeviceSettingsService
{
    public const string CommandType = "remote_settings";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string NormalizePhone(string phoneRaw) =>
        new string(phoneRaw.Where(char.IsDigit).ToArray());

    public static string BuildFileName(string phoneRaw)
    {
        var normalized = NormalizePhone(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"remote_settings_({normalized}).json";
    }

    public static string BuildPayloadJson(long commandId, RemoteDeviceSettingsPayload settings) =>
        JsonSerializer.Serialize(new RemoteSettingsEnvelope
        {
            Type = CommandType,
            CommandId = commandId,
            SentAt = commandId,
            Settings = settings
        }, JsonOptions);

    public static async Task<long> UploadAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        RemoteDeviceSettingsPayload settings,
        CancellationToken ct = default)
    {
        var commandId = KomCommandId.New();
        var fileName = BuildFileName(phoneRaw);
        var payload = BuildPayloadJson(commandId, settings);
        await dropbox.UploadNamedFileAsync(fileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }

    private sealed class RemoteSettingsEnvelope
    {
        public string Type { get; set; } = "";
        public long CommandId { get; set; }
        public long SentAt { get; set; }
        public RemoteDeviceSettingsPayload Settings { get; set; } = new();
    }
}

/// <summary>Felder entsprechen den SharedPreferences-Keys der GPSAnsagen-App.</summary>
public sealed class RemoteDeviceSettingsPayload
{
    public bool? ButtonSoundsEnabled { get; set; }
    public bool? AutoOpenNextRoute { get; set; }
    public bool? NightModeEnabled { get; set; }
    /// <summary>−60 … +120 in 20er-Schritten; 0 = Standard.</summary>
    public int? FahrgastraumGainPercent { get; set; }
    public bool? TftTimeEnabled { get; set; }
    public bool? InteriorAltProtocolEnabled { get; set; }
    public bool? ProtranStopsEnabled { get; set; }
    public bool? TftLawoEnabled { get; set; }
    public bool? HideNavigationBarEnabled { get; set; }
    public bool? AutoHideNavigationOnGps { get; set; }
    /// <summary><c>call</c> oder <c>sprechwunsch</c>.</summary>
    public string? ZblContactMode { get; set; }

    public bool? TftTcpEnabled { get; set; }
    public int? TftTcpPort { get; set; }
    public bool? TcpSocketClientEnabled { get; set; }
    /// <summary><c>TCP</c> oder <c>UDP</c>.</summary>
    public string? TcpSocketProtocol { get; set; }
    public string? TcpSocketHost { get; set; }
    public int? TcpSocketPort { get; set; }

    public string? SelectedProtocol { get; set; }
    /// <summary><c>SICMA_ZD_TARGET_NUMBER</c> oder <c>DIRECT_CLEARTEXT</c>.</summary>
    public string? IbisDisplayControlMode { get; set; }
}
