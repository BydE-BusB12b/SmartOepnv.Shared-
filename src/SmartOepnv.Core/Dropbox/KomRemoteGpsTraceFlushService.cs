using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Entspricht GPSAnsagen <c>KomRemoteGpsTraceFlush</c> –
/// Zwischenstand der Prüfdaten (GPS + Zeitstrahl) ohne Logout-Event.
/// </summary>
public static class KomRemoteGpsTraceFlushService
{
    public const string CommandType = "kom_remote_gps_trace_flush";

    public static string BuildFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_remote_gps_trace_flush_{normalized}.json";
    }

    public static string BuildPayloadJson(long commandId) =>
        JsonSerializer.Serialize(new
        {
            type = CommandType,
            commandId,
            sentAt = commandId
        });

    public static async Task<long> UploadAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        CancellationToken ct = default)
    {
        var commandId = KomCommandId.New();
        var fileName = BuildFileName(phoneRaw);
        var payload = BuildPayloadJson(commandId);
        await dropbox.UploadNamedFileAsync(fileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }
}
