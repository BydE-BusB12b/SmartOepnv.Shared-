using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>Entspricht GPSAnsagen <c>KomRemoteRoute</c>.</summary>
public static class KomRemoteRouteService
{
    public const string CommandType = "kom_remote_route";

    public static string BuildFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Telefonnummer fehlt oder ist ungültig.", nameof(phoneRaw));
        }

        return $"kom_remote_route_{normalized}.json";
    }

    public static string BuildPayloadJson(string routeDisplayName, bool activatePasInfo, long commandId) =>
        JsonSerializer.Serialize(new
        {
            type = CommandType,
            routeDisplayName,
            activatePasInfo,
            commandId,
            sentAt = commandId
        });

    public static async Task<long> UploadAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        string routeDisplayName,
        bool activatePasInfo = true,
        CancellationToken ct = default)
    {
        var route = routeDisplayName.Trim();
        if (string.IsNullOrEmpty(route))
        {
            return 0;
        }

        var commandId = KomCommandId.New();
        var fileName = BuildFileName(phoneRaw);
        var payload = BuildPayloadJson(route, activatePasInfo, commandId);
        await dropbox.UploadNamedFileAsync(fileName, payload, ct).ConfigureAwait(false);
        return commandId;
    }
}
