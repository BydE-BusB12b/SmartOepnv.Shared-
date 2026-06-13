using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Fahrerdisposition – getrennte Datei für schnelles Speichern.</summary>
public sealed class DriverDispositionStore
{
    public const string FileName = "driver_disposition.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;

    public DriverDispositionStore(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _filePath = Path.Combine(workspaceDir, FileName);
    }

    public bool Exists => File.Exists(_filePath);

    public IReadOnlyList<DriverDispositionAssignment> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<DriverDispositionFile>(json, JsonOptions);
            return data?.Assignments ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<DriverDispositionAssignment> assignments)
    {
        var list = assignments.Select(a => a.Clone()).ToList();
        var payload = new DriverDispositionFile
        {
            Version = 1,
            SavedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Assignments = list
        };

        SafeDataFileStore.WriteAllText(
            _filePath,
            JsonSerializer.Serialize(payload, JsonOptions),
            archivePrevious: false);
    }

    private sealed class DriverDispositionFile
    {
        public int Version { get; set; } = 1;

        public long SavedAtUtcMs { get; set; }

        public List<DriverDispositionAssignment> Assignments { get; set; } = [];
    }
}
