using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>Entspricht GPSAnsagen <c>KomLeitstelleDurchsage</c>.</summary>
public static class KomLeitstelleDurchsageService
{
    public const string CommandType = "kom_leitstelle_durchsage";

    public static string BuildJsonFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_leitstelle_durchsage_{normalized}.json";
    }

    public static string BuildAudioFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_leitstelle_durchsage_{normalized}.m4a";
    }

    public static string BuildPayloadJson(string phoneRaw, long commandId)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        return JsonSerializer.Serialize(new
        {
            type = CommandType,
            commandId,
            sentAt = commandId,
            audioFileName = BuildAudioFileName(phoneRaw)
        });
    }

    public static async Task<long> UploadAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        byte[] audioBytes,
        CancellationToken ct = default)
    {
        if (audioBytes.Length == 0)
        {
            return 0;
        }

        var commandId = KomCommandId.New();
        var audioFileName = BuildAudioFileName(phoneRaw);
        await dropbox.UploadNamedBinaryFileAsync(audioFileName, audioBytes, ct).ConfigureAwait(false);
        var jsonFileName = BuildJsonFileName(phoneRaw);
        var payload = BuildPayloadJson(phoneRaw, commandId);
        await dropbox.UploadNamedFileAsync(jsonFileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }
}
