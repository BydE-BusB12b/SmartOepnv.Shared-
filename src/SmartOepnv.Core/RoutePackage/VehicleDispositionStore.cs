using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Nur Fahrzeugdisposition – getrennt vom großen Planer-Overlay für schnelles Speichern.</summary>
public sealed class VehicleDispositionStore
{
    public const string FileName = "vehicle_disposition.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;

    public VehicleDispositionStore(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _filePath = Path.Combine(workspaceDir, FileName);
    }

    public string FilePath => _filePath;

    public bool Exists => File.Exists(_filePath);

    public IReadOnlyList<VehicleDispositionAssignment> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<VehicleDispositionFile>(json, JsonOptions);
            return data?.Assignments ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<VehicleDispositionAssignment> assignments)
    {
        var list = assignments.Select(a => a.Clone()).ToList();
        var payload = new VehicleDispositionFile
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

    private sealed class VehicleDispositionFile
    {
        public int Version { get; set; } = 1;

        public long SavedAtUtcMs { get; set; }

        public List<VehicleDispositionAssignment> Assignments { get; set; } = [];
    }
}
