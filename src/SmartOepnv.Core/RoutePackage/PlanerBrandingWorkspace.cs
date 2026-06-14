namespace SmartOepnv.Core.RoutePackage;



/// <summary>Firmenlogos für PDFs und Druckausgaben (workspace/branding).</summary>

public static class PlanerBrandingWorkspace

{

    public const string BrandingFolderName = "branding";

    public const string DefaultLogoBaseName = "company_logo";



    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)

    {

        ".png", ".jpg", ".jpeg", ".webp"

    };



    public static string GetBrandingDirectory(string appSubfolder)

    {

        var dir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace", BrandingFolderName);

        Directory.CreateDirectory(dir);

        return dir;

    }



    public static IReadOnlyList<CompanyLogoEntry> GetLogos(string appSubfolder)

    {

        var settings = LoadAndMigrateSettings(appSubfolder);

        return settings.CompanyLogos

            .Where(logo => !string.IsNullOrWhiteSpace(logo.Id) && !string.IsNullOrWhiteSpace(logo.FileName))

            .OrderBy(logo => logo.Name, StringComparer.CurrentCultureIgnoreCase)

            .ToList();

    }



    public static string? TryGetLogoPath(string appSubfolder, string? logoId = null)

    {

        var settings = LoadAndMigrateSettings(appSubfolder);

        if (string.IsNullOrWhiteSpace(logoId))

        {

            return null;

        }



        var entry = settings.CompanyLogos.FirstOrDefault(logo =>

            string.Equals(logo.Id, logoId.Trim(), StringComparison.Ordinal));

        if (entry is null || string.IsNullOrWhiteSpace(entry.FileName))

        {

            return null;

        }



        var path = Path.Combine(GetBrandingDirectory(appSubfolder), entry.FileName.Trim());

        return File.Exists(path) ? path : null;

    }



    public static CompanyLogoEntry AddLogoFromFile(string appSubfolder, string sourcePath, string? displayName = null)

    {

        if (!File.Exists(sourcePath))

        {

            throw new FileNotFoundException("Logo-Datei nicht gefunden.", sourcePath);

        }



        var extension = Path.GetExtension(sourcePath);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))

        {

            extension = ".png";

        }



        var id = Guid.NewGuid().ToString("N");

        var fileName = $"logo_{id}{extension.ToLowerInvariant()}";

        var targetPath = Path.Combine(GetBrandingDirectory(appSubfolder), fileName);

        File.Copy(sourcePath, targetPath, overwrite: true);



        var entry = new CompanyLogoEntry

        {

            Id = id,

            Name = string.IsNullOrWhiteSpace(displayName)

                ? Path.GetFileNameWithoutExtension(sourcePath)

                : displayName.Trim(),

            FileName = fileName

        };



        var store = new PlanerAppSettingsStore(appSubfolder);

        var settings = LoadAndMigrateSettings(appSubfolder);

        settings.CompanyLogos.Add(entry);

        settings.CompanyLogoFileName = string.Empty;

        store.Save(settings);



        return entry;

    }



    public static bool UpdateLogoName(string appSubfolder, string logoId, string name)

    {

        if (string.IsNullOrWhiteSpace(logoId))

        {

            return false;

        }



        var store = new PlanerAppSettingsStore(appSubfolder);

        var settings = LoadAndMigrateSettings(appSubfolder);

        var entry = settings.CompanyLogos.FirstOrDefault(logo =>

            string.Equals(logo.Id, logoId.Trim(), StringComparison.Ordinal));

        if (entry is null)

        {

            return false;

        }



        entry.Name = name.Trim();

        store.Save(settings);

        return true;

    }



    public static bool RemoveLogo(string appSubfolder, string logoId)

    {

        if (string.IsNullOrWhiteSpace(logoId))

        {

            return false;

        }



        var store = new PlanerAppSettingsStore(appSubfolder);

        var settings = LoadAndMigrateSettings(appSubfolder);

        var entry = settings.CompanyLogos.FirstOrDefault(logo =>

            string.Equals(logo.Id, logoId.Trim(), StringComparison.Ordinal));

        if (entry is null)

        {

            return false;

        }



        if (!string.IsNullOrWhiteSpace(entry.FileName))

        {

            var path = Path.Combine(GetBrandingDirectory(appSubfolder), entry.FileName.Trim());

            if (File.Exists(path))

            {

                File.Delete(path);

            }

        }



        settings.CompanyLogos.RemoveAll(logo => string.Equals(logo.Id, entry.Id, StringComparison.Ordinal));

        if (string.Equals(settings.CompanyLogoFileName, entry.FileName, StringComparison.OrdinalIgnoreCase))

        {

            settings.CompanyLogoFileName = string.Empty;

        }



        store.Save(settings);

        return true;

    }



    private static PlanerAppSettings LoadAndMigrateSettings(string appSubfolder)

    {

        var store = new PlanerAppSettingsStore(appSubfolder);

        var settings = store.Load();

        MigrateLegacyLogo(settings, appSubfolder);

        return settings;

    }



    private static void MigrateLegacyLogo(PlanerAppSettings settings, string appSubfolder)

    {

        if (settings.CompanyLogos.Count > 0 || string.IsNullOrWhiteSpace(settings.CompanyLogoFileName))

        {

            return;

        }



        var legacyFileName = settings.CompanyLogoFileName.Trim();

        var legacyPath = Path.Combine(GetBrandingDirectory(appSubfolder), legacyFileName);

        if (!File.Exists(legacyPath))

        {

            settings.CompanyLogoFileName = string.Empty;

            return;

        }



        settings.CompanyLogos.Add(new CompanyLogoEntry

        {

            Id = Guid.NewGuid().ToString("N"),

            Name = "Firmenlogo",

            FileName = legacyFileName

        });

        settings.CompanyLogoFileName = string.Empty;



        var store = new PlanerAppSettingsStore(appSubfolder);

        store.Save(settings);

    }

}

