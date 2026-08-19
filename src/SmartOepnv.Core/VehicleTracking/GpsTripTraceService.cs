using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.VehicleTracking;

public sealed class GpsTripTraceService
{
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
        var byPhone = new Dictionary<string, GpsTripTraceFile>(StringComparer.Ordinal);

        foreach (var fileName in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var content = await _dropbox.DownloadNamedFileAsync(fileName, ct);
                var parsed = GpsTripTraceParser.TryParse(content, fileName);
                if (parsed is null)
                {
                    continue;
                }

                var named = ApplyRosterName(parsed, roster);
                if (!byPhone.TryGetValue(named.Phone, out var existing) ||
                    named.UpdatedAtEpochMs >= existing.UpdatedAtEpochMs)
                {
                    byPhone[named.Phone] = named;
                }
            }
            catch
            {
                // Einzelne defekte Datei überspringen
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
            Days = file.Days
        };
    }
}
