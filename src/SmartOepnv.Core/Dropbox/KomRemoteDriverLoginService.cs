using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>Entspricht GPSAnsagen <c>KomRemoteDriverLogin</c> – Fahrer remote an-/abmelden.</summary>
public static class KomRemoteDriverLoginService
{
    public const string CommandType = "kom_remote_driver_login";
    public const string ActionLogin = "login";
    public const string ActionLogout = "logout";

    public static string BuildFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_remote_driver_login_{normalized}.json";
    }

    public static string BuildPayloadJson(
        string action,
        string? personnelPin,
        string? personnelNumber,
        string? driverName,
        long commandId) =>
        JsonSerializer.Serialize(new
        {
            type = CommandType,
            action,
            personnelPin = personnelPin ?? string.Empty,
            personnelNumber = personnelNumber ?? string.Empty,
            driverName = driverName ?? string.Empty,
            commandId,
            sentAt = commandId
        });

    public static async Task<long> UploadLoginAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        string personnelPin,
        string? personnelNumber,
        string? driverName,
        CancellationToken ct = default)
    {
        var pin = EmployeeRosterItemPin(personnelPin);
        if (pin.Length != 4)
        {
            throw new ArgumentException("Personalnummer/PIN muss 4 Ziffern ergeben.", nameof(personnelPin));
        }

        var commandId = KomCommandId.New();
        var fileName = BuildFileName(phoneRaw);
        var payload = BuildPayloadJson(ActionLogin, pin, personnelNumber, driverName, commandId);
        await dropbox.UploadNamedFileAsync(fileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }

    public static async Task<long> UploadLogoutAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        CancellationToken ct = default)
    {
        var commandId = KomCommandId.New();
        var fileName = BuildFileName(phoneRaw);
        var payload = BuildPayloadJson(ActionLogout, null, null, null, commandId);
        await dropbox.UploadNamedFileAsync(fileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }

    /// <summary>Wie App <c>EmployeeRosterItem.NormalizePersonnelDigits</c> / Login-PIN.</summary>
    public static string EmployeeRosterItemPin(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return string.Empty;
        return digits.Length <= 4 ? digits.PadLeft(4, '0') : digits[^4..];
    }
}
