using System.Text.Json;
using System.Text.Json.Nodes;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.Updates;

public static class DesktopInstallerDropboxPublisher
{
    public sealed record PublishResult(string SetupDropboxPath, string VersionFilePath, string Version);

    public static async Task<PublishResult> PublishAsync(
        string setupExePath,
        bool isLeitstelle,
        string version,
        CancellationToken ct = default)
    {
        if (!AppServices.IsInitialized)
        {
            throw new InvalidOperationException("AppServices.Initialize wurde nicht aufgerufen.");
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            throw new InvalidOperationException("Dropbox nicht verbunden – bitte zuerst im Planer anmelden.");
        }

        if (!File.Exists(setupExePath))
        {
            throw new FileNotFoundException("Setup-Datei nicht gefunden.", setupExePath);
        }

        var setupFileName = isLeitstelle
            ? DropboxConstants.LeitstelleSetupFileName
            : DropboxConstants.PlanerSetupFileName;
        var productKey = isLeitstelle ? "leitstelle" : "planer";
        var message = isLeitstelle
            ? "Nach dem Download Setup ausführen, um die Leitstelle zu aktualisieren."
            : "Nach dem Download Setup ausführen, um den Planer zu aktualisieren.";

        var bytes = await File.ReadAllBytesAsync(setupExePath, ct).ConfigureAwait(false);
        await AppServices.Dropbox.UploadNamedBinaryFileAsync(setupFileName, bytes, ct).ConfigureAwait(false);

        var folder = AppServices.Dropbox.Settings.FolderPath.TrimEnd('/');
        var setupDropboxPath = $"{folder}/{setupFileName}";
        var versionsDropboxPath = $"{folder}/{DropboxConstants.SoftwareVersionsFileName}";

        var root = new JsonObject();
        var existing = await AppServices.Dropbox
            .TryDownloadNamedFileAsync(DropboxConstants.SoftwareVersionsFileName, ct)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                var parsed = JsonNode.Parse(existing)?.AsObject();
                if (parsed is not null)
                {
                    if (parsed["planer"] is not null)
                    {
                        root["planer"] = parsed["planer"]!.DeepClone();
                    }

                    if (parsed["leitstelle"] is not null)
                    {
                        root["leitstelle"] = parsed["leitstelle"]!.DeepClone();
                    }
                }
            }
            catch
            {
                // neue Datei anlegen
            }
        }

        root[productKey] = new JsonObject
        {
            ["version"] = version.Trim(),
            ["setupFile"] = setupFileName,
            ["message"] = message
        };

        var versionsJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await AppServices.Dropbox
            .UploadNamedFileAsync(DropboxConstants.SoftwareVersionsFileName, versionsJson, ct)
            .ConfigureAwait(false);

        return new PublishResult(setupDropboxPath, versionsDropboxPath, version.Trim());
    }
}
