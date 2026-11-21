using System.Text.Json;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Merkt sich Dropbox-Metadaten von <c>planer_workspace.json</c> nach erfolgreichem Upload/Import,
/// damit der nächste Start nur Metadaten prüft und bei Gleichheit den lokalen Stand nutzt (kein Download).
/// </summary>
internal static class PlanerWorkspaceDropboxSyncStamp
{
    private const string FileSuffix = ".dropbox.stamp.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public sealed class Data
    {
        /// <summary>Fortlaufende Stand-Nummer (gleich wie in planer_workspace.json).</summary>
        public long SyncGeneration { get; set; }

        public long? ServerModifiedUtcMs { get; set; }
        public long SizeBytes { get; set; }
        public string? ContentHash { get; set; }
        public long LocalSavedAtUtcMs { get; set; }
    }

    public static string GetStampPath(string workspaceLocalPath) =>
        workspaceLocalPath + FileSuffix;

    public static Data? TryLoad(string workspaceLocalPath)
    {
        var path = GetStampPath(workspaceLocalPath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Nächste fortlaufende Nummer: max(lokal, Stempel) + 1.</summary>
    public static long NextSyncGeneration(string workspaceLocalPath, long documentGeneration)
    {
        var stampGeneration = TryLoad(workspaceLocalPath)?.SyncGeneration ?? 0;
        var baseline = Math.Max(documentGeneration, stampGeneration);
        return baseline <= 0 ? 1 : baseline + 1;
    }

    public static bool MatchesRemote(Data? stamp, DropboxNamedFileMetadata remote, long? localSyncGeneration = null)
    {
        if (stamp is null || stamp.SizeBytes <= 0 || remote.SizeBytes <= 0)
        {
            return false;
        }

        if (stamp.SyncGeneration > 0 &&
            localSyncGeneration is > 0 &&
            stamp.SyncGeneration != localSyncGeneration.Value)
        {
            return false;
        }

        if (stamp.SizeBytes != remote.SizeBytes)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(stamp.ContentHash) &&
            !string.IsNullOrWhiteSpace(remote.ContentHash))
        {
            return string.Equals(stamp.ContentHash, remote.ContentHash, StringComparison.Ordinal);
        }

        return stamp.ServerModifiedUtcMs is > 0 &&
               remote.ServerModifiedUtcMs is > 0 &&
               stamp.ServerModifiedUtcMs == remote.ServerModifiedUtcMs;
    }

    public static void Save(
        string workspaceLocalPath,
        DropboxNamedFileMetadata remote,
        long localSavedAtUtcMs,
        long syncGeneration)
    {
        var path = GetStampPath(workspaceLocalPath);
        try
        {
            var data = new Data
            {
                SyncGeneration = syncGeneration,
                ServerModifiedUtcMs = remote.ServerModifiedUtcMs,
                SizeBytes = remote.SizeBytes,
                ContentHash = remote.ContentHash,
                LocalSavedAtUtcMs = localSavedAtUtcMs
            };
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch
        {
            // Stempel ist nur Optimierung – Fehler ignorieren.
        }
    }
}
