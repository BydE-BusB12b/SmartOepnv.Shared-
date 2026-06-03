using System.IO;
using System.Reflection;

namespace SmartOepnv.AppShared.Sev;

public static class SevAssetPaths
{
    private static string? _root;

    public static string RootDirectory
    {
        get
        {
            if (_root is not null)
            {
                return _root;
            }

            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, "Assets", "sev");
            if (Directory.Exists(candidate))
            {
                _root = candidate;
                return _root;
            }

            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                candidate = Path.Combine(assemblyDir, "Assets", "sev");
                if (Directory.Exists(candidate))
                {
                    _root = candidate;
                    return _root;
                }
            }

            _root = candidate;
            return _root;
        }
    }

    public static string Resolve(string fileName) => Path.Combine(RootDirectory, fileName);

    public static bool Exists(string fileName) => File.Exists(Resolve(fileName));
}
