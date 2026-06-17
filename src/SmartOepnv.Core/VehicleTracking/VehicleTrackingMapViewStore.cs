using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.VehicleTracking;

public sealed class VehicleTrackingMapView
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Zoom { get; set; }

    public bool IsValid =>
        Zoom is > 0 and <= 22 &&
        double.IsFinite(Lat) &&
        double.IsFinite(Lon) &&
        Math.Abs(Lat) <= 90 &&
        Math.Abs(Lon) <= 180;
}

public sealed class VehicleTrackingMapViewStore
{
    public const string FileName = "vehicle_tracking_map_view.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public VehicleTrackingMapViewStore(string appSubfolder)
    {
        _path = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), FileName);
    }

    public VehicleTrackingMapView? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var view = JsonSerializer.Deserialize<VehicleTrackingMapView>(File.ReadAllText(_path), JsonOptions);
            return view is { IsValid: true } ? view : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(VehicleTrackingMapView view)
    {
        if (!view.IsValid)
        {
            return;
        }

        SafeDataFileStore.WriteAllText(_path, JsonSerializer.Serialize(view, JsonOptions));
    }
}
