using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Entspricht GPSAnsagen <c>KomRemoteManualUpdate</c>: löst auf dem Mitarbeitergerät
/// „Manuelles Update laden“ aus (Dropbox routes_export.json).
/// </summary>
public static class RemoteManualUpdateService
{
    public const string CommandType = "kom_remote_manual_update";

    public static string NormalizePhone(string phoneRaw) =>
        new string(phoneRaw.Where(char.IsDigit).ToArray());

    public static string BuildCommandFileName(string phoneRaw)
    {
        var normalized = NormalizePhone(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_remote_manual_update_{normalized}.json";
    }

    public static string BuildPayloadJson()
    {
        var commandId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return JsonSerializer.Serialize(new
        {
            type = CommandType,
            commandId,
            sentAt = commandId
        });
    }
}
