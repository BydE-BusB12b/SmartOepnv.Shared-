using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Geo;

public sealed class GpsMapPickerView
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

/// <summary>Merkt sich die letzte Ansicht der GPS-Auswahlkarte (Haltestelle/Ansage).</summary>
public sealed class GpsMapPickerViewStore
{
    public const string FileName = "gps_map_picker_view.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public GpsMapPickerViewStore(string appSubfolder)
    {
        _path = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), FileName);
    }

    public GpsMapPickerView? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var view = JsonSerializer.Deserialize<GpsMapPickerView>(File.ReadAllText(_path), JsonOptions);
            return view is { IsValid: true } ? view : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(GpsMapPickerView view)
    {
        if (!view.IsValid)
        {
            return;
        }

        SafeDataFileStore.WriteAllText(_path, JsonSerializer.Serialize(view, JsonOptions));
    }
}
