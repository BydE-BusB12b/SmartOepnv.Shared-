using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.VehicleTracking;

public sealed class GpsTripTraceService
{
    private const int MaxParallelDownloads = 6;
    private readonly DropboxApiClient _dropbox;

    public GpsTripTraceService(DropboxApiClient dropbox)
    {
        _dropbox = dropbox;
    }

    public async Task<IReadOnlyList<GpsTripTraceFile>> LoadAllAsync(
        string? routePackageJson,
        CancellationToken ct = default)
    {
        var roster = string.IsNullOrWhiteSpace(routePackageJson)
            ? Array.Empty<RegisteredVehicleInfo>()
            : RegisteredVehicleInfo.ParseFromJson(routePackageJson);

        var files = await _dropbox.ListGpsTraceFilesAsync(ct);
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
                var parsed = GpsTripTraceParser.TryParse(content, fileName);
                return parsed is null ? null : ApplyRosterName(parsed, roster);
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
        var byPhone = new Dictionary<string, GpsTripTraceFile>(StringComparer.Ordinal);
        foreach (var named in results)
        {
            if (named is null)
            {
                continue;
            }

            if (!byPhone.TryGetValue(named.Phone, out var existing) ||
                named.UpdatedAtEpochMs >= existing.UpdatedAtEpochMs)
            {
                byPhone[named.Phone] = named;
            }
        }

        return byPhone.Values
            .OrderBy(v => v.VehicleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GpsTripTraceFile ApplyRosterName(
        GpsTripTraceFile file,
        IReadOnlyList<RegisteredVehicleInfo> roster)
    {
        if (roster.Count == 0 || string.IsNullOrWhiteSpace(file.Phone))
        {
            return file;
        }

        var match = roster.FirstOrDefault(v =>
            string.Equals(
                new string((v.PhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray()),
                file.Phone,
                StringComparison.Ordinal));
        if (match is null || string.IsNullOrWhiteSpace(match.Name))
        {
            return file;
        }

        return new GpsTripTraceFile
        {
            FileName = file.FileName,
            Phone = file.Phone,
            VehicleName = match.Name.Trim(),
            UpdatedAtEpochMs = file.UpdatedAtEpochMs,
            Days = file.Days,
            Events = file.Events
        };
    }
}
