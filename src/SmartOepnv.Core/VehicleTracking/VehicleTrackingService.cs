using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.VehicleTracking;

public sealed class VehicleTrackingService
{
    private readonly DropboxApiClient _dropbox;

    public VehicleTrackingService(DropboxApiClient dropbox)
    {
        _dropbox = dropbox;
    }

    public async Task<IReadOnlyList<VehicleLiveState>> SyncAsync(
        string? routePackageJson,
        CancellationToken ct = default)
    {
        var roster = string.IsNullOrWhiteSpace(routePackageJson)
            ? Array.Empty<RegisteredVehicleInfo>()
            : RegisteredVehicleInfo.ParseFromJson(routePackageJson);

        var files = await _dropbox.ListLocationChatFilesAsync(ct);
        var byId = new Dictionary<string, VehicleLiveState>(StringComparer.Ordinal);

        foreach (var fileName in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var content = await _dropbox.DownloadNamedFileAsync(fileName, ct);
                var state = LocationChatParser.TryParse(content, fileName, roster);
                if (state is null || state.Status == VehicleOnlineStatus.Hidden)
                {
                    continue;
                }

                if (!byId.TryGetValue(state.Id, out var existing) ||
                    state.TimestampEpochMs >= existing.TimestampEpochMs)
                {
                    byId[state.Id] = state;
                }
            }
            catch
            {
                // Einzelne defekte Datei überspringen
            }
        }

        return byId.Values
            .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
