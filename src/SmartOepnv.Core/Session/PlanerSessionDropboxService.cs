using System.Text.Json;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.Session;

public sealed class PlanerSessionDropboxService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Abgelaufene Sperre nach Absturz (ohne Abmeldung) wieder freigeben.</summary>
    private static readonly TimeSpan StaleLockTimeout = TimeSpan.FromHours(12);

    private readonly DropboxApiClient _dropbox;

    public PlanerSessionDropboxService(DropboxApiClient dropbox) => _dropbox = dropbox;

    public async Task<PlanerSessionDocument> LoadAsync(CancellationToken ct = default)
    {
        if (!_dropbox.Settings.IsConnected)
        {
            return ReadLocalCopy() ?? CreateAvailable();
        }

        try
        {
            var json = await _dropbox.DownloadNamedFileAsync(DropboxConstants.PlanerSessionFileName, ct)
                .ConfigureAwait(false);
            return Parse(json) ?? CreateAvailable();
        }
        catch
        {
            return ReadLocalCopy() ?? CreateAvailable();
        }
    }

    public async Task SaveAsync(PlanerSessionDocument document, CancellationToken ct = default)
    {
        document.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var json = Serialize(document);

        if (_dropbox.Settings.IsConnected)
        {
            await _dropbox.UploadNamedFileAsync(DropboxConstants.PlanerSessionFileName, json, ct)
                .ConfigureAwait(false);
        }

        WriteLocalCopy(json);
    }

    public static bool IsLockedByOther(PlanerSessionDocument document, string? ownSessionId = null)
    {
        if (!string.Equals(document.Status, PlanerSessionStatus.InUse, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ownSessionId) &&
            string.Equals(document.SessionId, ownSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsStale(document) || IsSameMachineOrphan(document))
        {
            return false;
        }

        return true;
    }

    /// <summary>Hängende Sperre auf demselben PC (Absturz/fehlgeschlagener Start).</summary>
    public static bool IsSameMachineOrphan(PlanerSessionDocument document)
    {
        if (!string.Equals(document.Status, PlanerSessionStatus.InUse, StringComparison.Ordinal))
        {
            return false;
        }

        var machine = document.MachineName?.Trim();
        if (string.IsNullOrEmpty(machine))
        {
            return false;
        }

        return string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    public static PlanerSessionDocument CreateAvailableDocument() => CreateAvailable();

    public static bool IsStale(PlanerSessionDocument document)
    {
        if (document.UpdatedAtMs <= 0)
        {
            return true;
        }

        var updated = DateTimeOffset.FromUnixTimeMilliseconds(document.UpdatedAtMs);
        return DateTimeOffset.UtcNow - updated > StaleLockTimeout;
    }

    private static PlanerSessionDocument CreateAvailable() => new()
    {
        Status = PlanerSessionStatus.Available,
        UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    private static PlanerSessionDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PlanerSessionDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Serialize(PlanerSessionDocument document) =>
        JsonSerializer.Serialize(document, JsonOptions);

    private static string? LocalCopyPath()
    {
        var folder = DropboxSyncFolderLocator.TryResolveSmartOepnvFolder();
        return folder is null ? null : Path.Combine(folder, DropboxConstants.PlanerSessionFileName);
    }

    private static PlanerSessionDocument? ReadLocalCopy()
    {
        var path = LocalCopyPath();
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static void WriteLocalCopy(string json)
    {
        var path = LocalCopyPath();
        if (path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        catch
        {
            // optional
        }
    }
}
