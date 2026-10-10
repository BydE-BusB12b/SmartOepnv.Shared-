using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
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
    public ObservableCollection<TripInspectionScheduleRow> ScheduleRows { get; } = [];
    public ObservableCollection<TripInspectionTimelineSessionItem> TimelineSessions { get; } = [];

    [ObservableProperty] private string statusMessage = "GPS-Spuren aus Dropbox laden (gps_trace_*.json).";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private TripInspectionVehicleItem? selectedVehicle;
    [ObservableProperty] private TripInspectionSegmentItem? selectedSegment;
    [ObservableProperty] private string scheduleStatusMessage = "Zeitabschnitt wählen – bei vorhandenem Fahrplan erscheint die Soll/Ist-Tabelle.";
    [ObservableProperty] private bool hasScheduleTable;
    [ObservableProperty] private bool hasSelectedVehicle;
    [ObservableProperty] private bool isTimelineOpen;
    [ObservableProperty] private DateTime? timelineDate = DateTime.Today;
    [ObservableProperty] private string timelineStatusMessage = "Datum wählen und Zeitleiste öffnen.";

    /// <summary>
    /// Seite sofort anzeigen: vorhandener Cache bleibt, Dropbox nur auf „Jetzt laden“ oder erstes Öffnen (Hintergrund).
    /// </summary>
    public void OnViewActivated()
    {
        if (_files.Count > 0)
        {
            StatusMessage = $"{_files.Count} Fahrzeug(e) mit GPS-Spur (Cache) – „Jetzt laden“ aktualisiert.";
            RefreshMapForCurrentSelection();
            return;
        }

        // UI zuerst zeichnen, dann Dropbox laden.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _ = RefreshAsync();
            return;
        }

        dispatcher.BeginInvoke(
            () => _ = RefreshAsync(),
            DispatcherPriority.Background);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "GPS-Spuren werden geladen …";
        try
        {
            var json = AppServices.Routes.CurrentJson;
            var files = await Task.Run(async () => await _traces.LoadAllAsync(json));
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
        HasSelectedVehicle = value is not null;
        IsTimelineOpen = false;
        TimelineSessions.Clear();
        SelectedSegment = null;
        Segments.Clear();
        ClearScheduleTable("Zeitabschnitt wählen – bei vorhandenem Fahrplan erscheint die Soll/Ist-Tabelle.");
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
        RebuildTimelineIfOpen();
    }

    partial void OnTimelineDateChanged(DateTime? value) => RebuildTimelineIfOpen();

    [RelayCommand]
    private void OpenTimeline()
    {
        if (_selectedFile is null || SelectedVehicle is null)
        {
            return;
        }

        // Gewählte Fahrt rechts hat Vorrang – sonst Tag mit Events/GPS-Spur.
        var preferred = TryParseSegmentServiceDate(SelectedSegment)
                        ?? TimelineDate
                        ?? DateTime.Today;
        var suggested = GpsTripTraceParser.SuggestTimelineDate(_selectedFile, preferred);
        if (suggested is not null)
        {
            TimelineDate = suggested;
        }
        else
        {
            TimelineDate = preferred.Date;
        }

        IsTimelineOpen = true;
        RebuildTimeline();
    }

    [RelayCommand]
    private void CloseTimeline()
    {
        IsTimelineOpen = false;
    }

    private void RebuildTimelineIfOpen()
    {
        if (IsTimelineOpen)
        {
            RebuildTimeline();
        }
    }

    private void RebuildTimeline()
    {
        TimelineSessions.Clear();
        if (_selectedFile is null)
        {
            TimelineStatusMessage = "Kein Fahrzeug gewählt.";
            return;
        }

        var date = (TimelineDate ?? DateTime.Today).Date;
        var key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sessions = GpsTripTraceParser.BuildTimelineSessions(_selectedFile, key);
        foreach (var session in sessions)
        {
            TimelineSessions.Add(TripInspectionTimelineSessionItem.From(session));
        }

        TimelineStatusMessage = sessions.Count == 0
            ? $"Keine Sitzung am {date:dd.MM.yyyy} (weder Events noch GPS-Spur mit Fahrer)."
            : $"{sessions.Count} Sitzung(en) am {date:dd.MM.yyyy} – Events und GPS-Spur.";
    }

    partial void OnSelectedSegmentChanged(TripInspectionSegmentItem? value)
    {
        if (value is null)
        {
            ClearScheduleTable("Zeitabschnitt wählen – bei vorhandenem Fahrplan erscheint die Soll/Ist-Tabelle.");
            PushEmptyMap();
            return;
        }

        var segmentDate = TryParseSegmentServiceDate(value);
        if (segmentDate is not null &&
            segmentDate.Value.Date != (TimelineDate ?? DateTime.MinValue).Date)
        {
            TimelineDate = segmentDate;
        }

        PushTraceToMapRequested?.Invoke(BuildMapPayload(value).ToJsonString());
        RebuildScheduleTable(value);
    }

    private static DateTime? TryParseSegmentServiceDate(TripInspectionSegmentItem? segment)
    {
        if (segment is null || string.IsNullOrWhiteSpace(segment.Source.Date))
        {
            return null;
        }

        return DateTime.TryParseExact(
            segment.Source.Date.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed.Date
            : null;
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
        RebuildScheduleTable(SelectedSegment);
    }

    private void RebuildScheduleTable(TripInspectionSegmentItem item)
    {
        ScheduleRows.Clear();
        var compare = TripInspectionScheduleComparer.Build(
            item.Source,
            AppServices.Routes.Editor);
        foreach (var row in compare.Rows)
        {
            ScheduleRows.Add(row);
        }

        HasScheduleTable = compare.HasRows;
        ScheduleStatusMessage = compare.Message;
        if (compare.Matched && compare.HasRows)
        {
            StatusMessage = compare.Message;
        }
    }

    private void ClearScheduleTable(string message)
    {
        ScheduleRows.Clear();
        HasScheduleTable = false;
        ScheduleStatusMessage = message;
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
        var de = CultureInfo.GetCultureInfo("de-DE");
        var coords = new JsonArray();
        foreach (var point in segment.Points)
        {
            var local = DateTimeOffset.FromUnixTimeMilliseconds(point.TimestampEpochMs).ToLocalTime();
            var pointJson = new JsonObject
            {
                ["lat"] = point.Latitude,
                ["lon"] = point.Longitude,
                ["t"] = local.ToString("HH:mm:ss", de),
                ["ts"] = point.TimestampEpochMs,
                ["label"] = local.ToString("dd.MM. HH:mm:ss", de),
                ["speed"] = point.SpeedKmh
            };
            // bt/pi nur setzen wenn bekannt – fehlt → Altdaten-Darstellung
            if (point.BluetoothConnected is { } bt)
            {
                pointJson["bt"] = bt;
            }

            if (point.PasInfoActive is { } pi)
            {
                pointJson["pi"] = pi;
            }

            coords.Add(pointJson);
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
    public required GpsTripSegment Source { get; init; }
    public IReadOnlyList<GpsTripTracePoint> Points => Source.Points;

    public static TripInspectionSegmentItem From(GpsTripSegment segment)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        var start = DateTimeOffset.FromUnixTimeMilliseconds(segment.StartEpochMs).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeMilliseconds(segment.EndEpochMs).ToLocalTime();
        var line = string.IsNullOrWhiteSpace(segment.LineCourse)
            ? GpsTripTraceParser.UncodedLineCourse
            : segment.LineCourse.Trim();
        var trip = string.IsNullOrWhiteSpace(segment.TripNumber) ? null : segment.TripNumber.Trim();
        var coding = trip is null ? line : $"{line}, {trip}";
        var driver = string.IsNullOrWhiteSpace(segment.DriverName) ? null : segment.DriverName.Trim();
        var title = driver is null
            ? $"{start.ToString("ddd dd.MM.", de)}  {start:HH:mm}–{end:HH:mm}"
            : $"{start.ToString("ddd dd.MM.", de)}  {start:HH:mm}–{end:HH:mm}  ·  {driver}";
        var detailParts = new List<string> { coding };
        if (driver is not null)
        {
            detailParts.Add(driver);
        }

        detailParts.Add($"{segment.PointCount} GPS-Punkte");
        return new TripInspectionSegmentItem
        {
            Id = segment.Id,
            Title = title,
            Detail = string.Join(" · ", detailParts),
            Source = segment
        };
    }
}

public sealed class TripInspectionTimelineSessionItem
{
    public required string Header { get; init; }
    public required string SubHeader { get; init; }
    public ObservableCollection<TripInspectionTimelineEventItem> Events { get; init; } = [];

    public static TripInspectionTimelineSessionItem From(GpsTripTimelineSession session)
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(session.StartEpochMs).ToLocalTime();
        var end = DateTimeOffset.FromUnixTimeMilliseconds(session.EndEpochMs).ToLocalTime();
        var user = string.IsNullOrWhiteSpace(session.UserName) ? "Unbekannt" : session.UserName.Trim();
        var hasCrash = session.Events.Any(e => GpsTripTraceParser.IsCrashKind(e.Kind));
        var endLabel = session.ClosedByLogout
            ? $"Abmeldung {end:HH:mm:ss}"
            : hasCrash
                ? $"App-Absturz {end:HH:mm:ss}"
                : $"bis {end:HH:mm:ss} (offen)";
        var item = new TripInspectionTimelineSessionItem
        {
            Header = user,
            SubHeader = $"Anmeldung {start:HH:mm:ss} – {endLabel}"
        };
        foreach (var ev in session.Events)
        {
            item.Events.Add(TripInspectionTimelineEventItem.From(ev));
        }

        return item;
    }
}

public sealed class TripInspectionTimelineEventItem
{
    private static readonly Brush LoginBrush = FreezeBrush(0xA5, 0xD6, 0xA7);
    private static readonly Brush AlertBrush = FreezeBrush(0xEF, 0x53, 0x50);
    private static readonly Brush RouteBrush = FreezeBrush(0xFF, 0xEE, 0x58);
    private static readonly Brush CrashBrush = FreezeBrush(0xCE, 0x93, 0xD8);
    private static readonly Brush NeutralBrush = Brushes.White;

    public required string TimeLabel { get; init; }
    public required string KindLabel { get; init; }
    public required string DetailLabel { get; init; }
    public required Brush AccentBrush { get; init; }

    public static TripInspectionTimelineEventItem From(GpsTripTraceEvent ev)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        var local = DateTimeOffset.FromUnixTimeMilliseconds(ev.TimestampEpochMs).ToLocalTime();
        var (kind, detail, brush) = Describe(ev);
        return new TripInspectionTimelineEventItem
        {
            TimeLabel = local.ToString("HH:mm:ss", de),
            KindLabel = kind,
            DetailLabel = detail,
            AccentBrush = brush
        };
    }

    private static (string Kind, string Detail, Brush Accent) Describe(GpsTripTraceEvent ev)
    {
        var user = string.IsNullOrWhiteSpace(ev.UserName) ? null : ev.UserName.Trim();
        var d = string.IsNullOrWhiteSpace(ev.Detail) ? null : ev.Detail.Trim();
        if (GpsTripTraceParser.IsCrashKind(ev.Kind))
        {
            return ("App-Absturz", JoinParts(d, user), CrashBrush);
        }

        return ev.Kind.ToLowerInvariant() switch
        {
            "login" => ("Anmeldung", JoinParts(user, d is null ? null : $"PN {d}"), LoginBrush),
            "logout" => ("Abmeldung", JoinParts(user, d is null ? null : $"PN {d}"), NeutralBrush),
            "line" or "route" or "kurs" =>
                ("Linie/Kurs", JoinParts(d ?? GpsTripTraceParser.UncodedLineCourse, user), RouteBrush),
            "dest" or "zielwechsel" => ("Zielwechsel", JoinParts(d, user), RouteBrush),
            "pas_on" => ("Pas.Info an", user ?? "–", NeutralBrush),
            "pas_off" => ("Pas.Info deaktiviert", user ?? "–", AlertBrush),
            "gps_off" or "gps_disable" or "gps_disabled" or "gps_deactivated" =>
                ("GPS deaktiviert", JoinParts(d, user), AlertBrush),
            "bt_enable" => ("Bluetooth einschalten", JoinParts("BT war aus", d, user), NeutralBrush),
            _ => (ev.Kind, JoinParts(d, user), NeutralBrush)
        };
    }

    private static string JoinParts(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p))!);

    private static Brush FreezeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
