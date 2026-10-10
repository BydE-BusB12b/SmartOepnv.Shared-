using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// ZBL-Nachrichten vom Hauptgerät/Leitstelle an Fahrzeuge (Dropbox <c>zbl_message(…).json</c>),
/// kompatibel zu GPSAnsagen <c>DropboxManager.uploadZblMessage</c>.
/// </summary>
public static class ZblMessageService
{
    public const string CommandType = "kom_zbl_message";

    public static string NormalizePhone(string? raw) =>
        new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string BuildFileName(string phoneRaw)
    {
        var normalized = NormalizePhone(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"zbl_message({normalized}).json";
    }

    public static string BuildPayloadJson(string phoneRaw, string message, long commandId)
    {
        var normalized = NormalizePhone(phoneRaw);
        return JsonSerializer.Serialize(new
        {
            type = CommandType,
            phoneNumber = normalized,
            message,
            commandId,
            timestamp = commandId
        });
    }

    public static async Task<long> UploadAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        string message,
        CancellationToken ct = default)
    {
        var text = message.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var commandId = KomCommandId.New();
        var fileName = BuildFileName(phoneRaw);
        var payload = BuildPayloadJson(phoneRaw, text, commandId);
        await dropbox.UploadNamedFileAsync(fileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }
}
