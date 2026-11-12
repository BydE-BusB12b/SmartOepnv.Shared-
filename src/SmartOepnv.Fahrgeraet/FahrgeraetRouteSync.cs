using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Lädt routes_export.json und routes_update.json aus demselben Dropbox-Ordner wie das Tablet
/// und mischt das Lite-Update in das Vollpaket.
/// </summary>
public sealed class FahrgeraetRouteSync
{
    public const string AppFolder = "Fahrgeraet";

    private readonly string _workspaceDir;

    public FahrgeraetRouteSync()
    {
        _workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(AppFolder), "workspace");
        Directory.CreateDirectory(_workspaceDir);
    }

    public string LocalPackagePath => Path.Combine(_workspaceDir, "routes_local.json");

    public EditableRoutePackage? TryLoadLocal()
    {
        if (!File.Exists(LocalPackagePath))
        {
            return null;
        }

        var json = File.ReadAllText(LocalPackagePath);
        return EditableRoutePackage.FromJson(json);
    }

    public async Task<RouteSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var client = OpenConnectedClient();
        if (client is null)
        {
            return RouteSyncResult.Fail("Dropbox ist nicht verbunden. Im Planer anmelden, dann hier erneut abgleichen.");
        }

        var export = await client.TryDownloadNamedFileAsync(DropboxConstants.RouteFileName, ct).ConfigureAwait(false);
        var update = await client.TryDownloadNamedFileAsync(DropboxConstants.RouteUpdateFileName, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(export) && string.IsNullOrWhiteSpace(update))
        {
            return RouteSyncResult.Fail("routes_export.json und routes_update.json fehlen in Dropbox.");
        }

        var merged = Combine(export, update);
        await File.WriteAllTextAsync(LocalPackagePath, merged, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(export))
        {
            await File.WriteAllTextAsync(Path.Combine(_workspaceDir, DropboxConstants.RouteFileName), export, ct)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(update))
        {
            await File.WriteAllTextAsync(Path.Combine(_workspaceDir, DropboxConstants.RouteUpdateFileName), update, ct)
                .ConfigureAwait(false);
        }

        var package = EditableRoutePackage.FromJson(merged);
        var note = string.IsNullOrWhiteSpace(update)
            ? "routes_export.json"
            : "routes_export.json + routes_update.json";
        return RouteSyncResult.Ok(package, $"{package.RouteNames.Count} Routen aus {note}");
    }

    private static string Combine(string? export, string? update)
    {
        if (string.IsNullOrWhiteSpace(export))
        {
            return update ?? throw new InvalidOperationException("Kein Routenpaket.");
        }

        if (string.IsNullOrWhiteSpace(update))
        {
            return export;
        }

        if (!LiteRouteUpdateMerge.IsLiteVehicleUpdate(update))
        {
            return update;
        }

        return LiteRouteUpdateMerge.MergeIntoPackageJson(export, update);
    }

    private static DropboxApiClient? OpenConnectedClient()
    {
        foreach (var folder in new[] { AppFolder, "Planer" })
        {
            var store = new DropboxSettingsStore(folder);
            if (store.Load().IsConnected)
            {
                return new DropboxApiClient(store);
            }
        }

        return null;
    }
}

public sealed class RouteSyncResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public EditableRoutePackage? Package { get; init; }

    public static RouteSyncResult Ok(EditableRoutePackage package, string message) =>
        new() { Success = true, Package = package, Message = message };

    public static RouteSyncResult Fail(string message) =>
        new() { Success = false, Message = message };
}
