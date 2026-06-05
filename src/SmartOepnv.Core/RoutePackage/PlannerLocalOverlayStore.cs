using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

public sealed class PlannerLocalOverlayStore
{
    public const string OverlayFileName = "planner_local_roster.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _overlayPath;

    public PlannerLocalOverlayStore(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _overlayPath = Path.Combine(workspaceDir, OverlayFileName);
    }

    public string OverlayFilePath => _overlayPath;

    public bool Exists => File.Exists(_overlayPath);

    public PlannerLocalOverlayData LoadOrEmpty()
    {
        if (!File.Exists(_overlayPath))
        {
            return new PlannerLocalOverlayData();
        }

        try
        {
            var json = File.ReadAllText(_overlayPath);
            return JsonSerializer.Deserialize<PlannerLocalOverlayData>(json, JsonOptions) ?? new PlannerLocalOverlayData();
        }
        catch
        {
            return new PlannerLocalOverlayData();
        }
    }

    public void Save(PlannerLocalOverlayData data)
    {
        data.Version = PlannerLocalOverlayData.FileVersion;
        data.SavedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dir = Path.GetDirectoryName(_overlayPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        SafeDataFileStore.WriteAllText(_overlayPath, JsonSerializer.Serialize(data, JsonOptions));
    }
}
