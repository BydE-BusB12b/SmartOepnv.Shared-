using System.Text.Json;
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

    public bool IsDropboxConnected => OpenConnectedClient() is not null;

    public async Task<RouteSyncResult> SyncAsync(
        CancellationToken ct = default,
        IProgress<RoutenFortschritt>? fortschritt = null,
        bool nurTest = false)
    {
        var client = OpenConnectedClient();
        if (client is null)
        {
            return RouteSyncResult.Fail("Dropbox ist nicht verbunden. Im Planer anmelden, dann hier erneut abgleichen.");
        }

        if (nurTest)
        {
            Melden(fortschritt, 0, "Lade routes_test.json…");
            var (test, testError) = await Laden(client, DropboxConstants.RouteTestFileName, ct, fortschritt, 0, 1, 1)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(test))
            {
                return RouteSyncResult.Fail(testError ?? "routes_test.json fehlt im Dropbox-Ordner.");
            }

            await File.WriteAllTextAsync(LocalPackagePath, test, ct).ConfigureAwait(false);
            var testPaket = EditableRoutePackage.FromJson(test);
            Melden(fortschritt, 100, "routes_test.json installiert");
            return RouteSyncResult.Ok(testPaket, $"{testPaket.RouteNames.Count} Routen. routes_test.json installiert.");
        }

        var exportMeta = await client.TryGetNamedFileMetadataAsync(DropboxConstants.RouteFileName, ct).ConfigureAwait(false);
        var updateMeta = await client.TryGetNamedFileMetadataAsync(DropboxConstants.RouteUpdateFileName, ct).ConfigureAwait(false);
        if (exportMeta is null && updateMeta is null)
        {
            return RouteSyncResult.Fail("routes_export.json und routes_update.json fehlen im Dropbox-Ordner.");
        }

        var stand = StandLesen();
        var exportNeu = IstNeu(exportMeta, stand.Export, LocalCopy(DropboxConstants.RouteFileName));
        var updateNeu = IstNeu(updateMeta, stand.Update, LocalCopy(DropboxConstants.RouteUpdateFileName));
        if (!exportNeu && !updateNeu)
        {
            stand.Export = Kennung(exportMeta) ?? stand.Export;
            stand.Update = Kennung(updateMeta) ?? stand.Update;
            StandSchreiben(stand);
            return RouteSyncResult.Aktuell("Routen sind aktuell.");
        }

        var exportGroesse = exportNeu ? exportMeta?.SizeBytes ?? 0 : 0;
        var updateGroesse = updateNeu ? updateMeta?.SizeBytes ?? 0 : 0;
        var gesamt = Math.Max(1, exportGroesse + updateGroesse);
        string? export = null;
        string? exportError = null;
        string? update = null;
        string? updateError = null;
        if (exportNeu)
        {
            Melden(fortschritt, 0, "Lade routes_export.json…");
            (export, exportError) = await Laden(
                client, DropboxConstants.RouteFileName, ct, fortschritt, 0, exportGroesse, gesamt).ConfigureAwait(false);
        }

        if (updateNeu)
        {
            (update, updateError) = await Laden(
                client, DropboxConstants.RouteUpdateFileName, ct, fortschritt, exportGroesse, updateGroesse, gesamt)
                .ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(export) && string.IsNullOrWhiteSpace(update))
        {
            var why = string.Join(" ", new[] { exportError, updateError }.Where(text => !string.IsNullOrWhiteSpace(text)));
            return RouteSyncResult.Fail(string.IsNullOrWhiteSpace(why)
                ? "routes_export.json und routes_update.json fehlen im Dropbox-Ordner."
                : why);
        }

        var basis = export;
        if (string.IsNullOrWhiteSpace(basis) && File.Exists(LocalPackagePath))
        {
            basis = await File.ReadAllTextAsync(LocalPackagePath, ct).ConfigureAwait(false);
        }

        string merged;
        try
        {
            merged = Combine(basis, update);
        }
        catch (Exception ex)
        {
            return RouteSyncResult.Fail(ex.Message);
        }

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
        stand.Export = Kennung(exportMeta) ?? stand.Export;
        stand.Update = Kennung(updateMeta) ?? stand.Update;
        StandSchreiben(stand);
        var note = export is not null && update is not null
            ? "routes_export.json und routes_update.json installiert"
            : export is not null
                ? "routes_export.json installiert"
                : "routes_update.json installiert";
        if (!string.IsNullOrWhiteSpace(exportError))
        {
            note += ". " + exportError;
        }

        if (!string.IsNullOrWhiteSpace(updateError))
        {
            note += ". " + updateError;
        }

        return RouteSyncResult.Ok(package, $"{package.RouteNames.Count} Routen. {note}.");
    }

    private string LocalCopy(string name) => Path.Combine(_workspaceDir, name);

    private string StandPfad => Path.Combine(_workspaceDir, "route-sync.state.json");

    private SyncStand StandLesen()
    {
        try
        {
            return File.Exists(StandPfad)
                ? JsonSerializer.Deserialize<SyncStand>(File.ReadAllText(StandPfad)) ?? new SyncStand()
                : new SyncStand();
        }
        catch
        {
            return new SyncStand();
        }
    }

    private void StandSchreiben(SyncStand stand)
    {
        File.WriteAllText(StandPfad, JsonSerializer.Serialize(stand));
    }

    private static string? Kennung(DropboxNamedFileMetadata? meta)
    {
        if (meta is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(meta.Value.ContentHash)
            ? meta.Value.SizeBytes + ":" + meta.Value.ServerModifiedUtcMs
            : meta.Value.ContentHash;
    }

    private static bool IstNeu(DropboxNamedFileMetadata? meta, string? gespeichert, string lokaleKopie)
    {
        if (meta is null)
        {
            return false;
        }

        var kennung = Kennung(meta);
        var groesseUndZeit = meta.Value.SizeBytes + ":" + meta.Value.ServerModifiedUtcMs;
        if (!string.IsNullOrWhiteSpace(gespeichert)
            && (gespeichert == kennung
                || gespeichert == groesseUndZeit
                || (!string.IsNullOrWhiteSpace(meta.Value.ContentHash) && gespeichert == meta.Value.ContentHash)))
        {
            return false;
        }

        if (File.Exists(lokaleKopie))
        {
            var info = new FileInfo(lokaleKopie);
            var remoteNeuer = meta.Value.ServerModifiedUtcMs is long ms
                && DateTimeOffset.FromUnixTimeMilliseconds(ms) > info.LastWriteTimeUtc.AddSeconds(5);
            if (!remoteNeuer && info.Length == meta.Value.SizeBytes)
            {
                return false;
            }
        }

        return true;
    }

    private static void Melden(IProgress<RoutenFortschritt>? fortschritt, int prozent, string meldung) =>
        fortschritt?.Report(new RoutenFortschritt(prozent, meldung));

    private static async Task<(string? Text, string? Error)> Laden(
        DropboxApiClient client,
        string name,
        CancellationToken ct,
        IProgress<RoutenFortschritt>? fortschritt = null,
        long schon = 0,
        long dateiGroesse = 0,
        long gesamt = 0)
    {
        var progress = new Progress<DropboxTransferProgress>(info =>
        {
            var bytes = info.BytesTransferred;
            var basis = gesamt > 0 ? gesamt : Math.Max(dateiGroesse, info.TotalBytes);
            var prozent = basis <= 0
                ? (int)info.Percent
                : (int)Math.Clamp((schon + bytes) * 100.0 / basis, 0, 99);
            Melden(fortschritt, prozent, "Lade " + name + "…");
        });
        try
        {
            return (await client.DownloadNamedFileAsync(name, ct, progress).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsMissing(ex))
        {
            return (null, null);
        }
        catch (Exception ex)
        {
            return (null, name + ": " + ex.Message);
        }
    }

    private static bool IsMissing(Exception ex) =>
        ex.Message.Contains("not_found", StringComparison.OrdinalIgnoreCase);

    private static string Combine(string? export, string? update)
    {
        if (string.IsNullOrWhiteSpace(export))
        {
            if (string.IsNullOrWhiteSpace(update))
            {
                throw new InvalidOperationException("Kein Routenpaket.");
            }

            if (LiteRouteUpdateMerge.IsLiteVehicleUpdate(update))
            {
                throw new InvalidOperationException("routes_update.json braucht zuerst routes_export.json.");
            }

            return update;
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
    public bool Unchanged { get; init; }
    public string Message { get; init; } = "";
    public EditableRoutePackage? Package { get; init; }

    public static RouteSyncResult Ok(EditableRoutePackage package, string message) =>
        new() { Success = true, Package = package, Message = message };

    public static RouteSyncResult Aktuell(string message) =>
        new() { Success = true, Unchanged = true, Message = message };

    public static RouteSyncResult Fail(string message) =>
        new() { Success = false, Message = message };
}

public readonly record struct RoutenFortschritt(int Prozent, string Meldung);

internal sealed class SyncStand
{
    public string? Export { get; set; }
    public string? Update { get; set; }
}
