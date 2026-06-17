using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>Entspricht GPSAnsagen <c>KomRemoteDestination</c>.</summary>
public static class KomRemoteDestinationService
{
    public const string CommandType = "kom_remote_destination";

    public static string BuildFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_remote_destination_{normalized}.json";
    }

    public static string BuildPayloadJson(string destinationName, long commandId) =>
        JsonSerializer.Serialize(new
        {
            type = CommandType,
            destinationName,
            commandId,
            sentAt = commandId
        });

    public static async Task<long> UploadAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        string destinationName,
        CancellationToken ct = default)
    {
        var destination = destinationName.Trim();
        if (string.IsNullOrEmpty(destination))
        {
            return 0;
        }

        var commandId = KomCommandId.New();
        var fileName = BuildFileName(phoneRaw);
        var payload = BuildPayloadJson(destination, commandId);
        await dropbox.UploadNamedFileAsync(fileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }
}
