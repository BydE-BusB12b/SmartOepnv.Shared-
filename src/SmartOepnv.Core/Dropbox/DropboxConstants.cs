namespace SmartOepnv.Core.Dropbox;

public static class DropboxConstants
{
    public const string DefaultAppKey = "zl4jd0tyuqjwkxp";
    public const string DefaultAppSecret = "lzer62tixqyzpc3";
    public const string DefaultFolderPath = "/smart öpnv";

    /// <summary>Frühere Standardpfade – werden beim Laden auf <see cref="DefaultFolderPath"/> gemappt.</summary>
    public static readonly string[] LegacyDefaultFolderPaths =
    [
        "/App/Smart ÖPNV",
        "/Apps/Smart ÖPNV",
        "/Smart ÖPNV"
    ];

    public static string NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DefaultFolderPath;
        }

        var trimmed = path.Trim();
        if (!trimmed.StartsWith('/'))
        {
            trimmed = $"/{trimmed}";
        }

        foreach (var legacy in LegacyDefaultFolderPaths)
        {
            if (string.Equals(trimmed, legacy, StringComparison.OrdinalIgnoreCase))
            {
                return DefaultFolderPath;
            }
        }

        return trimmed;
    }
    public const string RouteFileName = "routes_export.json";

    /// <summary>
    /// Lokaler Planer-/Leitstelle-Arbeitscache (nicht Dropbox-App-Export).
    /// Audio liegt getrennt in <see cref="LocalRouteCacheHeavyMediaFileName"/>.
    /// </summary>
    public const string LocalRouteCacheFileName = "routes_cache.json";

    /// <summary>Audio-Sidecar zum lokalen <see cref="LocalRouteCacheFileName"/>.</summary>
    public const string LocalRouteCacheHeavyMediaFileName = "routes_cache.heavymedia.json";

    /// <summary>
    /// Leichtes Fahrzeugupdate ohne Audio (Merge auf dem Gerät, routes_export.json bleibt Vollbackup).
    /// </summary>
    public const string RouteUpdateFileName = "routes_update.json";
    public const string LeitstelleStandFileName = "leitstelle_stand.json";
    public const string MaengelkarteFileName = "maengelkarte.json";
    public const string PlanerSessionFileName = "planer_session.json";
    /// <summary>Vollständiger Planer-Arbeitsstand (nicht routes_export.json – die lesen die Apps).</summary>
    public const string PlanerWorkspaceFileName = "planer_workspace.json";

    /// <summary>Routen-Paket für Planer-Sync (getrennt von planer_workspace.json – schneller bei Dienst-/Dispo-Änderungen).</summary>
    public const string PlanerRoutesFileName = "planer_routes.json";

    /// <summary>Gespeicherte Planer-Snapshots (JSON pro Version, nicht in planer_workspace.json).</summary>
    public const string PlanerVersionSnapshotsFolderName = "planer_version_snapshots";

    /// <summary>
    /// Ansagen-Rohdateien für Planer-Sync (Binärdateien, nicht in planer_workspace.json).
    /// Unabhängig von routes_export.json / embeddedSounds für die Android-Apps.
    /// </summary>
    public const string PlanerAnnouncementRawSoundsFolderName = "planer_ansagen_roh";
    /// <summary>Verfügbare Planer-/Leitstelle-Versionen (nur Hinweis beim Start, kein Auto-Install).</summary>
    public const string SoftwareVersionsFileName = "software_versions.json";
    public const string PlanerSetupFileName = "Setup-Smart-OEPNV-Planer-x64.exe";
    public const string LeitstelleSetupFileName = "Setup-Smart-OEPNV-Leitstelle-x64.exe";
    public const string OAuthRedirectUri = "https://www.dropbox.com";

    public const string AuthorizeUrl = "https://www.dropbox.com/oauth2/authorize";
    public const string TokenUrl = "https://api.dropbox.com/oauth2/token";
    public const string UploadUrl = "https://content.dropboxapi.com/2/files/upload";
    public const string UploadSessionStartUrl = "https://content.dropboxapi.com/2/files/upload_session/start";
    public const string UploadSessionAppendUrl = "https://content.dropboxapi.com/2/files/upload_session/append_v2";
    public const string UploadSessionFinishUrl = "https://content.dropboxapi.com/2/files/upload_session/finish";
    public const string DownloadUrl = "https://content.dropboxapi.com/2/files/download";
    public const string ListFolderUrl = "https://api.dropboxapi.com/2/files/list_folder";
    public const string ListFolderContinueUrl = "https://api.dropboxapi.com/2/files/list_folder/continue";
    public const string SearchUrl = "https://api.dropboxapi.com/2/files/search_v2";
    public const string GetMetadataUrl = "https://api.dropboxapi.com/2/files/get_metadata";
    public const string CurrentAccountUrl = "https://api.dropboxapi.com/2/users/get_current_account";

    /// <summary>Dropbox /files/upload: maximal 150 MiB – darüber Upload-Session nutzen.</summary>
    public const int SimpleUploadMaxBytes = 140 * 1024 * 1024;

    /// <summary>Empfohlene Chunk-Größe für Upload-Sessions (4 MiB).</summary>
    public const int UploadSessionChunkBytes = 4 * 1024 * 1024;

    /// <summary>planer_workspace.json kann mehrere MB groß sein – langsames Internet braucht länger.</summary>
    public const int UploadTimeoutMinutes = 15;

    public const int UploadMaxAttempts = 5;

    public const int UploadRetryDelaySeconds = 4;
}
