using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.AppShared.ViewModels;

public partial class TripInspectionViewModel : ObservableObject
{
    private readonly GpsTripTraceService _traces = AppServices.GpsTripTraces;
    private IReadOnlyList<GpsTripTraceFile> _files = [];
    private GpsTripTraceFile? _selectedFile;

    public event Action<string>? PushTraceToMapRequested;

    public ObservableCollection<TripInspectionVehicleItem> Vehicles { get; } = [];
    public ObservableCollection<TripInspectionSegmentItem> Segments { get; } = [];

    [ObservableProperty] private string statusMessage = "GPS-Spuren aus Dropbox laden (gps_trace_*.json).";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private TripInspectionVehicleItem? selectedVehicle;
    [ObservableProperty] private TripInspectionSegmentItem? selectedSegment;

    public void OnViewActivated() => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var json = AppServices.Routes.CurrentJson;
            var files = await _traces.LoadAllAsync(json);
            await RunOnUiAsync(() =>
            {
                _files = files;
                RebuildVehicles();
                StatusMessage = files.Count == 0
                    ? "Keine GPS-Spuren in Dropbox (gps_trace_*.json). Aufzeichnung wird beim Abmelden am Fahrzeug hochgeladen."
                    : $"{files.Count} Fahrzeug(e) mit GPS-Spur (7-Tage-Loop).";
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => StatusMessage = $"Laden fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedVehicleChanged(TripInspectionVehicleItem? value)
    {
        SelectedSegment = null;
        Segments.Clear();
        _selectedFile = value is null
            ? null
            : _files.FirstOrDefault(f => f.Phone == value.Phone);
        if (_selectedFile is null)
        {
            PushEmptyMap();
            return;
        }

        foreach (var segment in GpsTripTraceParser.BuildSegments(_selectedFile))
        {
            Segments.Add(TripInspectionSegmentItem.From(segment));
        }

        StatusMessage = Segments.Count == 0
            ? $"Keine Fahrten in der Spur von {value?.DisplayName}."
            : $"{Segments.Count} Zeitabschnitt(e) für {value?.DisplayName}.";
        PushEmptyMap();
    }

    partial void OnSelectedSegmentChanged(TripInspectionSegmentItem? value)
    {
        if (value is null)
        {
            PushEmptyMap();
            return;
        }

        PushTraceToMapRequested?.Invoke(BuildMapPayload(value).ToJsonString());
    }

    /// <summary>
    /// Nach erneutem Öffnen der (gecachten) View die aktuelle Spur erneut an die Karte schicken.
    /// </summary>
    public void RefreshMapForCurrentSelection()
    {
        if (SelectedSegment is null)
        {
            PushEmptyMap();
            return;
        }

        PushTraceToMapRequested?.Invoke(BuildMapPayload(SelectedSegment).ToJsonString());
    }

    private void RebuildVehicles()
    {
        var selectedPhone = SelectedVehicle?.Phone;
        Vehicles.Clear();
        foreach (var file in _files)
        {
            var points = file.Days.Sum(d => d.Points.Count);
            Vehicles.Add(new TripInspectionVehicleItem
            {
                Phone = file.Phone,
                DisplayName = file.VehicleName,
                PointCount = points,
                DayCount = file.Days.Count,
                UpdatedLabel = FormatUpdated(file.UpdatedAtEpochMs)
            });
        }

        SelectedVehicle = !string.IsNullOrWhiteSpace(selectedPhone)
            ? Vehicles.FirstOrDefault(v => v.Phone == selectedPhone)
            : null;
    }

    private static JsonObject BuildMapPayload(TripInspectionSegmentItem segment)
    {
        var coords = new JsonArray();
        foreach (var point in segment.Points)
        {
            coords.Add(new JsonArray { point.Latitude, point.Longitude });
        }

        return new JsonObject
        {
            ["label"] = segment.Title,
            ["detail"] = segment.Detail,
            ["points"] = coords
        };
    }

    private void PushEmptyMap() =>
        PushTraceToMapRequested?.Invoke(new JsonObject { ["points"] = new JsonArray() }.ToJsonString());

    private static string FormatUpdated(long epochMs)
    {
        if (epochMs <= 0)
        {
            return "–";
        }

        var local = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToLocalTime();
        return local.ToString("dd.MM. HH:mm", CultureInfo.GetCultureInfo("de-DE"));
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }
}

public sealed class TripInspectionVehicleItem
{
    public required string Phone { get; init; }
    public required string DisplayName { get; init; }
    public int PointCount { get; init; }
    public int DayCount { get; init; }
    public required string UpdatedLabel { get; init; }
    public string DetailLine =>
        $"{DayCount} Tag(e) · {PointCount} Punkte · Stand {UpdatedLabel}";
}

public sealed class TripInspectionSegmentItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public IReadOnlyList<GpsTripTracePoint> Points { get; init; } = [];

    public static TripInspectionSegmentItem From(GpsTripSegment segment)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        var start = DateTimeOffset.FromUnixTimeMilliseconds(segment.StartEpochMs).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeMilliseconds(segment.EndEpochMs).ToLocalTime();
        var line = string.IsNullOrWhiteSpace(segment.LineCourse) ? "ohne Linie" : segment.LineCourse;
        var trip = string.IsNullOrWhiteSpace(segment.TripNumber) ? null : $"Fahrt {segment.TripNumber}";
        var title = $"{start.ToString("ddd dd.MM.", de)}  {start:HH:mm}–{end:HH:mm}";
        var detailParts = new List<string> { line! };
        if (trip is not null)
        {
            detailParts.Add(trip);
        }

        detailParts.Add($"{segment.PointCount} GPS-Punkte");
        return new TripInspectionSegmentItem
        {
            Id = segment.Id,
            Title = title,
            Detail = string.Join(" · ", detailParts),
            Points = segment.Points
        };
    }
}
