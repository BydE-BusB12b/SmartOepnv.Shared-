using System.IO;
using System.Text.Json;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.Betrieb;

/// <summary>
/// Mehrere Planer-Betriebe: je eigener Dropbox-Ordner + isolierter lokaler Datenordner unter
/// <c>%AppData%\Smart-OEPNV\Planer\betriebe\{id}\</c>.
/// </summary>
public static class BetriebProfileStore
{
    private const string RegistryFileName = "betriebe.json";
    private const string BetriebeFolderName = "betriebe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] RootFilesToMigrate =
    [
        "dropbox.settings.dat",
        "planer_app_settings.json",
        "voip_settings.json",
        "vehicle_tracking_map_view.json",
        "remote_device_settings_templates.json"
    ];

    private static readonly string[] RootDirsToMigrate =
    [
        "workspace"
    ];

    private static readonly string[] LocalFilesToMigrate =
    [
        "planer_active_session.json",
        "kom_command_ack_processed.json",
        "software_update_ack.json"
    ];

    private static readonly string[] LocalDirsToMigrate =
    [
        "WebView2"
    ];

    public static string GetPlanerRootDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smart-OEPNV",
            "Planer");

    public static string GetPlanerLocalRootDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Smart-OEPNV",
            "Planer");

    public static string GetRegistryPath() =>
        Path.Combine(GetPlanerRootDirectory(), RegistryFileName);

    public static string GetBetriebDataDirectory(string betriebId) =>
        Path.Combine(GetPlanerRootDirectory(), BetriebeFolderName, SanitizeId(betriebId));

    public static string GetBetriebLocalDataDirectory(string betriebId) =>
        Path.Combine(GetPlanerLocalRootDirectory(), BetriebeFolderName, SanitizeId(betriebId));

    /// <summary>
    /// Vor <see cref="AppServices.Initialize"/>: Registry anlegen/migrieren und aktiven Betrieb in
    /// <see cref="AppPaths"/> setzen.
    /// </summary>
    public static BetriebRegistry EnsureMigratedAndActivate()
    {
        Directory.CreateDirectory(GetPlanerRootDirectory());
        Directory.CreateDirectory(GetPlanerLocalRootDirectory());

        var registry = LoadRegistry() ?? CreateRegistryFromLegacyOrDefault();
        if (string.IsNullOrWhiteSpace(registry.ActiveId) ||
            registry.Profiles.All(p => p.Id != registry.ActiveId))
        {
            registry.ActiveId = registry.Profiles.FirstOrDefault()?.Id ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(registry.ActiveId))
        {
            var created = CreateProfile(
                "smart öpnv",
                DropboxConstants.DefaultFolderPath,
                copyTokensFrom: null);
            registry.Profiles.Add(created);
            registry.ActiveId = created.Id;
        }

        SaveRegistry(registry);
        AppPaths.SetActiveBetrieb(registry.ActiveId);
        Directory.CreateDirectory(GetBetriebDataDirectory(registry.ActiveId));
        Directory.CreateDirectory(GetBetriebLocalDataDirectory(registry.ActiveId));
        return registry;
    }

    public static BetriebRegistry LoadOrEmpty() =>
        LoadRegistry() ?? new BetriebRegistry();

    public static BetriebProfile? GetActiveProfile()
    {
        var registry = LoadOrEmpty();
        return registry.Profiles.FirstOrDefault(p => p.Id == registry.ActiveId);
    }

    public static IReadOnlyList<BetriebProfile> ListProfiles()
    {
        var registry = LoadOrEmpty();
        return registry.Profiles
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Aktiven Betrieb wechseln (nach Flush/Export des bisherigen). Danach App neu starten.</summary>
    public static void SwitchTo(string betriebId)
    {
        var registry = LoadOrEmpty();
        var profile = registry.Profiles.FirstOrDefault(p => p.Id == betriebId)
            ?? throw new InvalidOperationException("Betrieb nicht gefunden.");
        registry.ActiveId = profile.Id;
        SaveRegistry(registry);
        AppPaths.SetActiveBetrieb(profile.Id);
    }

    /// <summary>Neuen leeren Betrieb anlegen und aktiv setzen. Danach App neu starten.</summary>
    public static BetriebProfile CreateAndActivate(
        string displayName,
        string dropboxFolderPath,
        DropboxSettings? copyTokensFrom)
    {
        var registry = LoadOrEmpty();
        var profile = CreateProfile(displayName, dropboxFolderPath, copyTokensFrom);
        registry.Profiles.Add(profile);
        registry.ActiveId = profile.Id;
        SaveRegistry(registry);
        AppPaths.SetActiveBetrieb(profile.Id);
        return profile;
    }

    public static void UpdateProfileMeta(string betriebId, string displayName, string dropboxFolderPath)
    {
        var registry = LoadOrEmpty();
        var profile = registry.Profiles.FirstOrDefault(p => p.Id == betriebId)
            ?? throw new InvalidOperationException("Betrieb nicht gefunden.");
        profile.DisplayName = displayName.Trim();
        profile.DropboxFolderPath = DropboxConstants.NormalizeFolderPath(dropboxFolderPath);
        SaveRegistry(registry);
    }

    private static BetriebProfile CreateProfile(
        string displayName,
        string dropboxFolderPath,
        DropboxSettings? copyTokensFrom)
    {
        var name = string.IsNullOrWhiteSpace(displayName)
            ? "Neuer Betrieb"
            : displayName.Trim();
        var folder = DropboxConstants.NormalizeFolderPath(dropboxFolderPath);
        var id = Guid.NewGuid().ToString("N");
        var dataDir = GetBetriebDataDirectory(id);
        var localDir = GetBetriebLocalDataDirectory(id);
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(localDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "workspace"));

        var settings = copyTokensFrom is null
            ? new DropboxSettings { FolderPath = folder }
            : new DropboxSettings
            {
                FolderPath = folder,
                AccessToken = copyTokensFrom.AccessToken,
                RefreshToken = copyTokensFrom.RefreshToken,
                AppKey = copyTokensFrom.AppKey,
                AppSecret = copyTokensFrom.AppSecret,
                ConnectedAccountName = copyTokensFrom.ConnectedAccountName,
                ConnectedAccountEmail = copyTokensFrom.ConnectedAccountEmail
            };

        // Temporär Pfad setzen, damit DropboxSettingsStore in den neuen Ordner schreibt.
        var previous = AppPaths.ActiveBetriebId;
        try
        {
            AppPaths.SetActiveBetrieb(id);
            new DropboxSettingsStore("Planer").Save(settings);
        }
        finally
        {
            AppPaths.SetActiveBetrieb(previous);
        }

        return new BetriebProfile
        {
            Id = id,
            DisplayName = name,
            DropboxFolderPath = folder,
            CreatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static BetriebRegistry CreateRegistryFromLegacyOrDefault()
    {
        var root = GetPlanerRootDirectory();
        var localRoot = GetPlanerLocalRootDirectory();
        var hasLegacy =
            Directory.Exists(Path.Combine(root, "workspace")) ||
            File.Exists(Path.Combine(root, "dropbox.settings.dat"));

        string displayName;
        string folderPath;
        DropboxSettings? legacySettings = null;

        if (hasLegacy)
        {
            // Kurz ohne Betrieb-Scope lesen (Legacy liegt noch im Planer-Root).
            AppPaths.SetActiveBetrieb(null);
            legacySettings = new DropboxSettingsStore("Planer").Load();
            folderPath = DropboxConstants.NormalizeFolderPath(legacySettings.FolderPath);
            displayName = DeriveDisplayName(folderPath);
        }
        else
        {
            folderPath = DropboxConstants.DefaultFolderPath;
            displayName = DeriveDisplayName(folderPath);
        }

        var id = Guid.NewGuid().ToString("N");
        var target = GetBetriebDataDirectory(id);
        var localTarget = GetBetriebLocalDataDirectory(id);
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(localTarget);

        if (hasLegacy)
        {
            MigrateLegacyInto(root, target, localRoot, localTarget);
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(target, "workspace"));
            AppPaths.SetActiveBetrieb(id);
            new DropboxSettingsStore("Planer").Save(new DropboxSettings { FolderPath = folderPath });
            AppPaths.SetActiveBetrieb(null);
        }

        return new BetriebRegistry
        {
            ActiveId = id,
            Profiles =
            [
                new BetriebProfile
                {
                    Id = id,
                    DisplayName = displayName,
                    DropboxFolderPath = folderPath,
                    CreatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            ]
        };
    }

    private static void MigrateLegacyInto(
        string root,
        string target,
        string localRoot,
        string localTarget)
    {
        foreach (var file in RootFilesToMigrate)
        {
            MoveIfExists(Path.Combine(root, file), Path.Combine(target, file));
        }

        foreach (var dir in RootDirsToMigrate)
        {
            MoveDirIfExists(Path.Combine(root, dir), Path.Combine(target, dir));
        }

        foreach (var file in LocalFilesToMigrate)
        {
            MoveIfExists(Path.Combine(localRoot, file), Path.Combine(localTarget, file));
        }

        foreach (var dir in LocalDirsToMigrate)
        {
            MoveDirIfExists(Path.Combine(localRoot, dir), Path.Combine(localTarget, dir));
        }

        Directory.CreateDirectory(Path.Combine(target, "workspace"));
    }

    private static void MoveIfExists(string source, string dest)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (File.Exists(dest))
        {
            File.Delete(dest);
        }

        File.Move(source, dest);
    }

    private static void MoveDirIfExists(string source, string dest)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        if (Directory.Exists(dest))
        {
            Directory.Delete(dest, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        Directory.Move(source, dest);
    }

    private static BetriebRegistry? LoadRegistry()
    {
        var path = GetRegistryPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BetriebRegistry>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveRegistry(BetriebRegistry registry)
    {
        Directory.CreateDirectory(GetPlanerRootDirectory());
        File.WriteAllText(GetRegistryPath(), JsonSerializer.Serialize(registry, JsonOptions));
    }

    public static string DeriveDisplayName(string folderPath)
    {
        var normalized = DropboxConstants.NormalizeFolderPath(folderPath).Trim('/');
        return string.IsNullOrWhiteSpace(normalized) ? "Betrieb" : normalized;
    }

    public static string SanitizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "default";
        }

        var chars = id.Trim().Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var s = new string(chars);
        return string.IsNullOrWhiteSpace(s) ? "default" : s;
    }

    public static string SuggestFolderPath(string displayName)
    {
        var raw = string.IsNullOrWhiteSpace(displayName) ? "neuer-betrieb" : displayName.Trim();
        var slug = new string(raw
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : (c is ' ' or '-' or '_' ? '-' : '\0'))
            .Where(c => c != '\0')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "neuer-betrieb";
        }

        return DropboxConstants.NormalizeFolderPath("/" + slug);
    }
}
