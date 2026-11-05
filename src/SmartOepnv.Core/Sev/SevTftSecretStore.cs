using System.Security.Cryptography;
using System.Text;

namespace SmartOepnv.Core.Sev;

/// <summary>API-Token der SEV-TFT, DPAPI, nicht in planer_app_settings.json.</summary>
public sealed class SevTftSecretStore
{
    public const string FileName = "sev_tft.secret.dat";

    private readonly string _path;

    public SevTftSecretStore(string appSubfolder)
    {
        _path = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), FileName);
    }

    internal static SevTftSecretStore CreateForFile(string path) => new(path, explicitFile: true);

    private SevTftSecretStore(string path, bool explicitFile)
    {
        _ = explicitFile;
        _path = path;
    }

    public string? LoadToken()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(jsonBytes).Trim();
            return token.Length == 0 ? null : token;
        }
        catch
        {
            return null;
        }
    }

    public void SaveToken(string? token)
    {
        var trimmed = (token ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            return;
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(trimmed),
            null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }
}
