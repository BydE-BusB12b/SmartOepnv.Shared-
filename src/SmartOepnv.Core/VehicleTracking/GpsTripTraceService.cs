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

        var archiveDir = GpsTripTraceArchive.DirectoryFor(AppServices.SettingsSubfolder);
        var files = await _dropbox.ListGpsTraceFilesAsync(ct);
        var byPhone = new Dictionary<string, GpsTripTraceFile>(StringComparer.Ordinal);

        if (files.Count > 0)
        {
            using var gate = new SemaphoreSlim(MaxParallelDownloads);
            var tasks = files.Select(async fileName =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var content = await _dropbox.DownloadNamedFileAsync(fileName, ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return null;
                    }

                    var incoming = GpsTripTraceParser.TryParse(content, fileName);
                    if (incoming is null || string.IsNullOrWhiteSpace(incoming.Phone))
                    {
                        return null;
                    }

                    var archived = GpsTripTraceArchive.Absorb(archiveDir, incoming.Phone, content);
                    var parsed = GpsTripTraceParser.TryParse(archived, fileName) ?? incoming;
                    return ApplyRosterName(parsed, roster);
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

            foreach (var named in await Task.WhenAll(tasks).ConfigureAwait(false))
            {
                RememberNewer(byPhone, named);
            }
        }

        foreach (var stored in GpsTripTraceArchive.ReadAll(archiveDir))
        {
            if (byPhone.ContainsKey(stored.Phone))
            {
                continue;
            }

            var parsed = GpsTripTraceParser.TryParse(stored.Json, stored.FileName);
            RememberNewer(byPhone, parsed is null ? null : ApplyRosterName(parsed, roster));
        }

        return byPhone.Values
            .OrderBy(v => v.VehicleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void RememberNewer(Dictionary<string, GpsTripTraceFile> byPhone, GpsTripTraceFile? named)
    {
        if (named is null || string.IsNullOrWhiteSpace(named.Phone))
        {
            return;
        }

        if (!byPhone.TryGetValue(named.Phone, out var existing) ||
            named.UpdatedAtEpochMs >= existing.UpdatedAtEpochMs)
        {
            byPhone[named.Phone] = named;
        }
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
