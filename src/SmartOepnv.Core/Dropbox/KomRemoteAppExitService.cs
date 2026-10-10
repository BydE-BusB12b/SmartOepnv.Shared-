using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>Entspricht GPSAnsagen <c>KomRemoteAppExit</c> – Fahrer abmelden und App beenden.</summary>
public static class KomRemoteAppExitService
{
    public const string CommandType = "kom_remote_app_exit";

    public static string BuildFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_remote_app_exit_{normalized}.json";
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
