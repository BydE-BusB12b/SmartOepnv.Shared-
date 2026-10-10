using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.VehicleTracking;

public sealed class VehicleTrackingService
{
    private const int MaxParallelDownloads = 8;
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
        if (files.Count == 0)
        {
            return [];
        }

        using var gate = new SemaphoreSlim(MaxParallelDownloads);
        var tasks = files.Select(async fileName =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var content = await _dropbox.DownloadNamedFileAsync(fileName, ct).ConfigureAwait(false);
                return LocationChatParser.TryParse(content, fileName, roster);
            }
            catch
            {
                return null;
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var byId = new Dictionary<string, VehicleLiveState>(StringComparer.Ordinal);
        foreach (var state in results)
        {
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

        return byId.Values
            .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
