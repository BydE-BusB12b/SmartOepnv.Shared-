using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>Entspricht GPSAnsagen <c>KomRemoteLock</c> – Gerät remote sperren/entsperren.</summary>
public static class KomRemoteLockService
{
    public const string CommandType = "kom_remote_lock";

    public static string BuildFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_remote_lock_{normalized}.json";
    }

    public static string BuildPayloadJson(bool locked, long commandId) =>
        JsonSerializer.Serialize(new
        {
            type = CommandType,
            locked,
            commandId,
            sentAt = commandId
        });

    public static async Task<long> UploadAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        bool locked,
        CancellationToken ct = default)
    {
        var commandId = KomCommandId.New();
        var fileName = BuildFileName(phoneRaw);
        var payload = BuildPayloadJson(locked, commandId);
        await dropbox.UploadNamedFileAsync(fileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }
}
