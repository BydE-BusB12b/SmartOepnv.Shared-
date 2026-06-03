using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// ZBL-Nachrichten vom Hauptgerät/Leitstelle an Fahrzeuge (Dropbox <c>zbl_message(…).json</c>),
/// kompatibel zu GPSAnsagen <c>DropboxManager.uploadZblMessage</c>.
/// </summary>
public static class ZblMessageService
{
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

    public static string BuildPayloadJson(string phoneRaw, string message)
    {
        var normalized = NormalizePhone(phoneRaw);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return JsonSerializer.Serialize(new
        {
            phoneNumber = normalized,
            message,
            timestamp
        });
    }
}
