using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartOepnv.Core.VehicleTracking;

public static class LocationChatCrypto
{
    private const string AppKey = "GPSAnsagen2024!X";
    private const string Prefix = "LOCATION_UPDATE:";

    public static string DecryptLocationUpdateMessage(string messageField)
    {
        if (string.IsNullOrWhiteSpace(messageField))
        {
            throw new ArgumentException("messageField ist leer.", nameof(messageField));
        }

        if (!messageField.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ungültiges Format: Prefix LOCATION_UPDATE: fehlt.");
        }

        var base64Cipher = messageField[Prefix.Length..]
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        var cipherBytes = Convert.FromBase64String(base64Cipher);
        var keyBytes = Encoding.UTF8.GetBytes(AppKey);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyBytes;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public static JsonDocument DecryptFromLocationChatFileContent(string fileContent)
    {
        using var outer = JsonDocument.Parse(fileContent);
        if (!outer.RootElement.TryGetProperty("message", out var msgElem))
        {
            throw new InvalidOperationException("Feld 'message' fehlt in location_chat JSON.");
        }

        var messageField = msgElem.GetString();
        if (messageField is null)
        {
            throw new InvalidOperationException("Feld 'message' ist null.");
        }

        var payloadJson = DecryptLocationUpdateMessage(messageField);
        return JsonDocument.Parse(payloadJson);
    }
}
