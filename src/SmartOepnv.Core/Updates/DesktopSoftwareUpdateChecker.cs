using System.Reflection;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.Updates;

public sealed record SoftwareUpdateNotice(
    string InstalledVersion,
    string AvailableVersion,
    string SetupFileName,
    string? Message);

public static class DesktopSoftwareUpdateChecker
{
    private const string PlanerAppKey = "planer";
    private const string LeitstelleAppKey = "leitstelle";

    public static async Task<SoftwareUpdateNotice?> CheckAsync(CancellationToken ct = default)
    {
        if (!AppServices.IsInitialized || !AppServices.Dropbox.Settings.IsConnected)
        {
            return null;
        }

        string? json;
        try
        {
            json = await AppServices.Dropbox
                .TryDownloadNamedFileAsync(DropboxConstants.SoftwareVersionsFileName, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var manifest = SoftwareVersionsManifest.TryParse(json);
        var entry = manifest?.ResolveForApp(!AppServices.IsPlannerApp);
        if (entry is null || !TryParseVersion(entry.Version, out var available))
        {
            return null;
        }

        var installedText = ResolveInstalledVersionText();
        if (!TryParseVersion(installedText, out var installed))
        {
            return null;
        }

        if (available <= installed)
        {
            return null;
        }

        var appKey = AppServices.IsPlannerApp ? PlanerAppKey : LeitstelleAppKey;
        if (SoftwareUpdateAckStore.WasAcknowledged(appKey, entry.Version))
        {
            return null;
        }

        return new SoftwareUpdateNotice(
            installedText,
            entry.Version,
            ResolveSetupFileName(entry),
            entry.Message);
    }

    private static string ResolveSetupFileName(SoftwareVersionEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SetupFileName))
        {
            return Path.GetFileName(entry.SetupFileName.Trim());
        }

        return AppServices.IsPlannerApp
            ? DropboxConstants.PlanerSetupFileName
            : DropboxConstants.LeitstelleSetupFileName;
    }

    public static void MarkNoticeAcknowledged(SoftwareUpdateNotice notice)
    {
        var appKey = AppServices.IsPlannerApp ? PlanerAppKey : LeitstelleAppKey;
        SoftwareUpdateAckStore.MarkAcknowledged(appKey, notice.AvailableVersion);
    }

    public static string ResolveInstalledVersionText()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var cut = informational.IndexOf('+', StringComparison.Ordinal);
            return cut > 0 ? informational[..cut] : informational.Trim();
        }

        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }

    private static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var cleaned = raw.Trim();
        if (Version.TryParse(cleaned, out var parsed))
        {
            version = parsed;
            return true;
        }

        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }
}
