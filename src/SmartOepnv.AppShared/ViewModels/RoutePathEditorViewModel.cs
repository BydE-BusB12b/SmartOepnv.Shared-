using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.AppShared.ViewModels;

public sealed record RouteLineColorOption(string Label, string Hex);

public partial class RoutePathEditorViewModel : ObservableObject
{
    private const int MaxUndoSteps = 30;

    private readonly OsrmSnapService _osrm = new();
    private readonly List<RoutePathDraft> _undoStack = [];
    private RoutePathDraft? _draft;
    private string? _selectedSegmentFrom;
    private string? _selectedSegmentTo;
    private int _selectedManeuverIndex;
    private CancellationTokenSource? _previewSnapCts;

    public event Action<string, string?, bool>? PushDraftToMapRequested;

    [ObservableProperty] private string statusMessage = "Route wählen und Fahrweg planen.";
    [ObservableProperty] private string? selectedRoute;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? selectedManeuverText;
    [ObservableProperty] private string selectedNavSymbol = "straight";

    public IReadOnlyList<string> NavSymbols { get; } = NavSymbolCatalog.All.Select(x => x.Id).ToList();

    public IReadOnlyList<RouteLineColorOption> RouteLineColorOptions { get; } =
    [
        new("Blau", "#2196f3"),
        new("Grün", "#4caf50"),
        new("Orange", "#ff9800"),
        new("Rot", "#f44336"),
        new("Lila", "#9c27b0"),
        new("Türkis", "#00bcd4")
    ];

    [ObservableProperty] private string selectedRouteLineColor = "#2196f3";
    [ObservableProperty] private bool canUndo;

    public IReadOnlyList<string> AvailableRoutes
    {
        get
        {
            var editor = AppServices.Routes.Editor;
            return editor?.RouteNames.ToList() ?? [];
        }
    }

    partial void OnSelectedRouteChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        LoadDraftForRoute(value);
    }

    public void ReloadMapFromDraft()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        PushDraftToMap(CollectStopsForSelectedRoute(), resetMapView: false);
    }

    public void RefreshRoutes()
    {
        OnPropertyChanged(nameof(AvailableRoutes));
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            SelectedRoute = AvailableRoutes.FirstOrDefault();
        }
        else if (!AvailableRoutes.Contains(SelectedRoute))
        {
            SelectedRoute = AvailableRoutes.FirstOrDefault();
        }
        else
        {
            LoadDraftForRoute(SelectedRoute);
        }
    }

    partial void OnSelectedRouteLineColorChanged(string value)
    {
        if (_draft is null || string.IsNullOrWhiteSpace(value)) return;
        if (string.Equals(_draft.RouteLineColor, value, StringComparison.OrdinalIgnoreCase)) return;
        _draft.RouteLineColor = value;
        PushDraftToMap(resetMapView: false);
    }

    private void LoadDraftForRoute(string routeName)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte zuerst importieren.";
            _draft = null;
            return;
        }

        var stops = editor.GetStops(routeName).Where(s => !s.IsWaypoint).ToList();
        _undoStack.Clear();
        CanUndo = false;
        UndoLastChangeCommand.NotifyCanExecuteChanged();

        _draft = RoutePathDraftRepository.LoadOrCreate(routeName, stops, editor.PackageRoot);
        EnsureStopsOnDraft(stops);
        SelectedRouteLineColor = string.IsNullOrWhiteSpace(_draft.RouteLineColor) ? "#2196f3" : _draft.RouteLineColor;
        ReportDraftStatus();
        PushDraftToMap(stops, resetMapView: true);
    }

    [RelayCommand]
    private void LoadStopsOnMap()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var stops = editor.GetStops(SelectedRoute).Where(s => !s.IsWaypoint).ToList();
        if (_draft is null)
        {
            _draft = RoutePathDraftBuilder.CreateSeed(SelectedRoute, stops);
        }
        else
        {
            EnsureStopsOnDraft(stops);
        }

        ReportDraftStatus();
        PushDraftToMap(stops, resetMapView: true);
    }

    private void EnsureStopsOnDraft(IList<RouteStopItem> stops)
    {
        if (_draft is null) return;

        var seeded = RoutePathDraftBuilder.BuildSeedNodes(stops);
        var preserved = _draft.Nodes
            .Where(n => n.Type is RoutePathNodeType.AUTO_WAYPOINT or RoutePathNodeType.MANUAL_WAYPOINT)
            .ToList();
        _draft.Nodes = seeded.Concat(preserved).ToList();
    }

    private void ReportDraftStatus()
    {
        if (_draft is null)
        {
            StatusMessage = "Kein Entwurf geladen.";
            return;
        }

        var stopCount = _draft.Nodes.Count(n => n.Type == RoutePathNodeType.STOP);
        var annCount = _draft.Nodes.Count(n => n.Type == RoutePathNodeType.ANNOUNCEMENT);
        if (stopCount == 0 && annCount == 0)
        {
            StatusMessage = "Keine GPS-Koordinaten in den Haltestellen – bitte unter „Routen“ gpsCoordinates/stopCoordinates pflegen, dann „Haltestellen laden“.";
            return;
        }

        StatusMessage = $"„{SelectedRoute}“ – {stopCount} Haltestellen, {annCount} Ansagepunkte, {_draft.Segments.Count} Verbindungen.";
    }

    public void ApplyDraftJsonFromMap(string json, bool recordUndo = false)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var previousSegmentCount = _draft?.Segments.Count ?? 0;
            if (recordUndo && _draft is not null)
            {
                PushUndoSnapshot();
            }

            var parsed = RoutePathDraftSerializer.FromJson(json);
            if (!string.IsNullOrWhiteSpace(SelectedRoute))
            {
                parsed.RouteName = SelectedRoute;
            }

            if (_draft is not null)
            {
                parsed.CreatedAtEpochMs = _draft.CreatedAtEpochMs;
                MergePreservedSnapData(_draft, parsed);
            }

            _draft = parsed;
            if (_draft.Segments.Count > previousSegmentCount)
            {
                var newest = _draft.Segments.MaxBy(s => s.Order);
                if (newest is not null)
                {
                    SetSelectedSegment(newest.FromNodeId, newest.ToNodeId);
                    StatusMessage =
                        $"Verbindung #{newest.Order} erstellt – „Straße snappen (Auswahl)“ für dieses Teilstück.";
                    return;
                }
            }

            StatusMessage = $"Entwurf aktualisiert – {_draft.Segments.Count} Verbindungen.";
        }
        catch
        {
            // ignore malformed interim messages
        }
    }

    public void OnSegmentAddedFromMap(string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        SetSelectedSegment(from, to);
    }

    public void DeleteSegmentFromMap(string? from, string? to)
    {
        if (_draft is null || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            return;
        }

        PushUndoSnapshot();
        if (!RoutePathDraftMutator.DeleteSegment(_draft, from, to))
        {
            DiscardLastUndoSnapshot();
            StatusMessage = "Verbindung konnte nicht gelöscht werden.";
            return;
        }

        _selectedSegmentFrom = null;
        _selectedSegmentTo = null;
        SelectedManeuverText = null;
        StatusMessage = $"Verbindung gelöscht – {_draft.Segments.Count} Verbindungen übrig.";
        PushDraftToMap();
    }

    private void PushUndoSnapshot()
    {
        if (_draft is null) return;
        _undoStack.Add(RoutePathDraftCloner.Clone(_draft));
        if (_undoStack.Count > MaxUndoSteps)
        {
            _undoStack.RemoveAt(0);
        }

        CanUndo = _undoStack.Count > 0;
        UndoLastChangeCommand.NotifyCanExecuteChanged();
    }

    private void DiscardLastUndoSnapshot()
    {
        if (_undoStack.Count == 0)
        {
            CanUndo = false;
            UndoLastChangeCommand.NotifyCanExecuteChanged();
            return;
        }

        _undoStack.RemoveAt(_undoStack.Count - 1);
        CanUndo = _undoStack.Count > 0;
        UndoLastChangeCommand.NotifyCanExecuteChanged();
    }

    private RoutePathDraft? PopUndoSnapshot()
    {
        if (_undoStack.Count == 0)
        {
            CanUndo = false;
            return null;
        }

        var snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        CanUndo = _undoStack.Count > 0;
        UndoLastChangeCommand.NotifyCanExecuteChanged();
        return snapshot;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoLastChange()
    {
        var previous = PopUndoSnapshot();
        if (previous is null)
        {
            StatusMessage = "Nichts zum Rückgängigmachen.";
            return;
        }

        _draft = previous;
        if (!string.IsNullOrWhiteSpace(SelectedRoute))
        {
            _draft.RouteName = SelectedRoute;
        }

        _selectedSegmentFrom = null;
        _selectedSegmentTo = null;
        SelectedManeuverText = null;
        SelectedRouteLineColor = string.IsNullOrWhiteSpace(_draft.RouteLineColor) ? "#2196f3" : _draft.RouteLineColor;
        StatusMessage = $"Rückgängig – {_draft.Segments.Count} Verbindungen.";
        PushDraftToMap();
    }

    [RelayCommand]
    private void ResetRouteConnections()
    {
        if (_draft is null)
        {
            StatusMessage = "Kein Entwurf geladen.";
            return;
        }

        if (_draft.Segments.Count == 0 && _draft.RoadSnappedEdgeKeys.Count == 0)
        {
            StatusMessage = "Kein Fahrweg vorhanden.";
            return;
        }

        PushUndoSnapshot();
        RoutePathDraftMutator.ClearSegmentsAndSnap(_draft);
        _selectedSegmentFrom = null;
        _selectedSegmentTo = null;
        SelectedManeuverText = null;
        StatusMessage = "Fahrweg und Snap-Daten zurückgesetzt – Haltestellen bleiben.";
        PushDraftToMap();
    }

    [RelayCommand]
    private void ClearSnapOnly()
    {
        if (_draft is null)
        {
            StatusMessage = "Kein Entwurf geladen.";
            return;
        }

        if (_draft.RoadSnappedEdgeKeys.Count == 0 && _draft.SnappedShape.Count == 0)
        {
            StatusMessage = "Kein Straßen-Snap vorhanden.";
            return;
        }

        PushUndoSnapshot();
        RoutePathDraftMutator.ClearSnapData(_draft);
        StatusMessage = "Straßen-Snap entfernt – Verbindungen (gelbe Linien) bleiben.";
        PushDraftToMap();
    }

    [RelayCommand]
    private void DeleteSelectedSegment()
    {
        if (_draft is null || _draft.Segments.Count == 0)
        {
            StatusMessage = "Keine Verbindungen vorhanden.";
            return;
        }

        string from;
        string to;
        if (!string.IsNullOrEmpty(_selectedSegmentFrom) && !string.IsNullOrEmpty(_selectedSegmentTo))
        {
            from = _selectedSegmentFrom;
            to = _selectedSegmentTo;
        }
        else
        {
            var last = _draft.Segments.MaxBy(s => s.Order)!;
            from = last.FromNodeId;
            to = last.ToNodeId;
        }

        PushUndoSnapshot();
        if (!RoutePathDraftMutator.DeleteSegment(_draft, from, to))
        {
            DiscardLastUndoSnapshot();
            StatusMessage = "Verbindung konnte nicht gelöscht werden.";
            return;
        }

        _selectedSegmentFrom = null;
        _selectedSegmentTo = null;
        SelectedManeuverText = null;
        StatusMessage = $"Verbindung gelöscht – {_draft.Segments.Count} übrig.";
        PushDraftToMap();
    }

    private static void MergePreservedSnapData(RoutePathDraft previous, RoutePathDraft current)
    {
        // Nur Snap ergänzen, wenn die Karte die Kante weiterhin als gesnappt führt.
        // Früher wurden Keys aus previous immer wiederhergestellt – gesnappte Routen wirkten „eingefroren“.
        foreach (var seg in current.Segments)
        {
            var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
            if (!current.RoadSnappedEdgeKeys.Contains(key))
            {
                continue;
            }

            if (!current.RoadSegmentPolylines.ContainsKey(key) &&
                previous.RoadSegmentPolylines.TryGetValue(key, out var pts))
            {
                current.RoadSegmentPolylines[key] = pts;
            }

            if (!current.RoadSegmentManeuvers.ContainsKey(key) &&
                previous.RoadSegmentManeuvers.TryGetValue(key, out var mans))
            {
                current.RoadSegmentManeuvers[key] = mans;
            }

            if (previous.RoadBusStraightEdgeKeys.Contains(key))
            {
                current.RoadBusStraightEdgeKeys.Add(key);
            }
        }
    }

    [RelayCommand]
    private void ApplyBusStraightLane()
    {
        if (_draft is null || _draft.Segments.Count == 0)
        {
            StatusMessage = "Keine Verbindung vorhanden.";
            return;
        }

        RoutePathSegment? target = null;
        if (!string.IsNullOrEmpty(_selectedSegmentFrom) && !string.IsNullOrEmpty(_selectedSegmentTo))
        {
            target = _draft.Segments.FirstOrDefault(s =>
                s.FromNodeId == _selectedSegmentFrom && s.ToNodeId == _selectedSegmentTo);
        }

        target ??= _draft.Segments.MaxBy(s => s.Order);
        if (target is null)
        {
            return;
        }

        PushUndoSnapshot();
        RoutePathBusLaneHelper.ApplyBusStraightToSegment(_draft, target);
        RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(_draft);
        SelectedManeuverText = "Geradeaus (Busspur / direkt)";
        SelectedNavSymbol = "straight";
        StatusMessage =
            $"Segment #{target.Order}: Busspur/direkt gesetzt (ohne PKW-Umweg über OSRM).";
        PushDraftToMap();
    }

    public void SchedulePreviewSnapForSegment(string? from, string? to)
    {
        if (_draft is null || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        _ = PreviewSnapSegmentDebouncedAsync(from, to);
    }

    public void SchedulePreviewSnapForNode(string? nodeId)
    {
        if (_draft is null || string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        foreach (var seg in _draft.Segments)
        {
            if (seg.FromNodeId == nodeId || seg.ToNodeId == nodeId)
            {
                _ = PreviewSnapSegmentDebouncedAsync(seg.FromNodeId, seg.ToNodeId);
            }
        }
    }

    private async Task PreviewSnapSegmentDebouncedAsync(string from, string to)
    {
        _previewSnapCts?.Cancel();
        _previewSnapCts?.Dispose();
        _previewSnapCts = new CancellationTokenSource();
        var token = _previewSnapCts.Token;

        try
        {
            await Task.Delay(450, token);
            if (_draft is null) return;

            StatusMessage = "Straßenvorschau wird berechnet…";
            await RoutePathSnapOrchestrator.SnapSegmentAsync(_draft, from, to, _osrm, token);
            StatusMessage = "Straßenvorschau aktualisiert.";
            PushDraftToMap();
        }
        catch (OperationCanceledException)
        {
            // newer preview scheduled
        }
        catch (Exception ex)
        {
            StatusMessage = $"Vorschau-Snap fehlgeschlagen: {ex.Message}";
        }
    }

    public void SetSelectedSegment(string? from, string? to, int? maneuverIndex = null)
    {
        _selectedSegmentFrom = from;
        _selectedSegmentTo = to;
        _selectedManeuverIndex = maneuverIndex ?? 0;
        if (_draft is null || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            SelectedManeuverText = null;
            PushDraftToMap();
            return;
        }

        var key = RoutePathDraft.SegmentEdgeKey(from, to);
        if (_draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) && mans.Count > 0)
        {
            var idx = Math.Clamp(_selectedManeuverIndex, 0, mans.Count - 1);
            _selectedManeuverIndex = idx;
            var m = mans[idx];
            SelectedManeuverText = m.Instruction;
            SelectedNavSymbol = string.IsNullOrWhiteSpace(m.NavSymbolType) ? "straight" : m.NavSymbolType;
            StatusMessage = $"Manöver {idx + 1}/{mans.Count} gewählt.";
        }
        else
        {
            SelectedManeuverText = "Noch nicht gesnappt";
            StatusMessage = "Segment gewählt – „Straße snappen (Auswahl)“ für nur dieses Teilstück.";
        }

        PushDraftToMap();
    }

    [RelayCommand]
    private void AutoBuildRoute()
    {
        if (_draft is null) return;
        PushUndoSnapshot();
        _draft.Segments = RoutePathDraftBuilder.BuildAutoSegments(_draft.Nodes);
        RoutePathDraftMutator.ClearSnapData(_draft);
        _selectedSegmentFrom = null;
        _selectedSegmentTo = null;
        StatusMessage =
            $"{_draft.Segments.Count} Verbindungen erzeugt (gelbe Linien) – je Teilstück „Straße snappen (Auswahl)“ wie am Handy.";
        PushDraftToMap();
    }

    [RelayCommand]
    private async Task SnapSelectedSegmentAsync()
    {
        if (_draft is null)
        {
            StatusMessage = "Kein Entwurf geladen.";
            return;
        }

        if (_draft.Segments.Count == 0)
        {
            StatusMessage = "Zuerst zwei Punkte auf der Karte verbinden: Knoten A tippen, dann Knoten B.";
            return;
        }

        string from;
        string to;
        if (!string.IsNullOrEmpty(_selectedSegmentFrom) && !string.IsNullOrEmpty(_selectedSegmentTo) &&
            _draft.Segments.Any(s => s.FromNodeId == _selectedSegmentFrom && s.ToNodeId == _selectedSegmentTo))
        {
            from = _selectedSegmentFrom;
            to = _selectedSegmentTo;
        }
        else
        {
            var last = _draft.Segments.MaxBy(s => s.Order)!;
            from = last.FromNodeId;
            to = last.ToNodeId;
            SetSelectedSegment(from, to);
        }

        var segment = _draft.Segments.First(s => s.FromNodeId == from && s.ToNodeId == to);
        PushUndoSnapshot();
        IsBusy = true;
        StatusMessage = $"Straßensnap für Segment #{segment.Order}…";
        try
        {
            await RoutePathSnapOrchestrator.SnapSegmentAsync(_draft, from, to, _osrm);
            StatusMessage = $"Segment #{segment.Order} auf Straße gesnappt (nur A→B).";
            PushDraftToMap();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Einzel-Snap fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SnapAllAsync()
    {
        if (_draft is null || _draft.Segments.Count == 0)
        {
            StatusMessage = "Keine Verbindungen – zuerst Route erzeugen oder Knoten verbinden.";
            return;
        }

        PushUndoSnapshot();
        IsBusy = true;
        StatusMessage = "OSRM-Snap läuft…";
        try
        {
            await RoutePathSnapOrchestrator.SnapAllSegmentsAsync(_draft, _osrm);
            StatusMessage = $"Straßenzug fertig – {_draft.RoadSnappedEdgeKeys.Count} Segmente gesnappt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Snap fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        PushDraftToMap();
        if (CommitDraftToWorkspace())
        {
            StatusMessage += " – lokal gespeichert.";
        }
    }

    [RelayCommand]
    private void ApplyNavSymbol()
    {
        if (_draft is null || string.IsNullOrEmpty(_selectedSegmentFrom) || string.IsNullOrEmpty(_selectedSegmentTo))
        {
            StatusMessage = "Bitte zuerst ein Segment auf der Karte anklicken.";
            return;
        }

        PushUndoSnapshot();
        var key = RoutePathDraft.SegmentEdgeKey(_selectedSegmentFrom, _selectedSegmentTo);
        if (!_draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) || mans.Count == 0)
        {
            mans =
            [
                new RoutePathSnapManeuver
                {
                    DistanceM = 0,
                    Instruction = SelectedManeuverText ?? "Manuell",
                    NavSymbolType = SelectedNavSymbol
                }
            ];
            _draft.RoadSegmentManeuvers[key] = mans;
        }
        else
        {
            var idx = Math.Clamp(_selectedManeuverIndex, 0, mans.Count - 1);
            mans[idx].NavSymbolType = SelectedNavSymbol;
            if (!string.IsNullOrWhiteSpace(SelectedManeuverText))
            {
                mans[idx].Instruction = SelectedManeuverText;
            }
        }

        RebuildMergedManeuvers();
        StatusMessage = $"Navi-Symbol „{SelectedNavSymbol}“ gesetzt.";
        PushDraftToMap();
    }

    [RelayCommand]
    private void SaveToPackage()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            StatusMessage = "Nichts zu speichern.";
            return;
        }

        if (!CommitDraftToWorkspace())
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        StatusMessage =
            $"Fahrweg lokal gespeichert (routePathDrafts[\"{SelectedRoute}\"]) – für Fahrzeuge über Dropbox senden.";
    }

    public bool CommitDraftToWorkspace()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null || _draft is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return false;
        }

        RoutePathDraftRepository.SaveToPackage(editor.PackageRoot, _draft);
        AppServices.Routes.ApplyEditorChanges("navidaten");
        return true;
    }

    private void RebuildMergedManeuvers()
    {
        if (_draft is null) return;
        var merged = new List<RoutePathSnapManeuver>();
        foreach (var seg in _draft.Segments.OrderBy(s => s.Order))
        {
            var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
            if (_draft.RoadSegmentManeuvers.TryGetValue(key, out var mans))
            {
                merged.AddRange(mans);
            }
        }
        _draft.SnappedManeuvers = merged;
    }

    private void PushDraftToMap(IList<RouteStopItem>? stopsForBounds = null, bool resetMapView = false)
    {
        if (_draft is null) return;
        try
        {
            var node = JsonNode.Parse(RoutePathDraftSerializer.ToJson(_draft))!.AsObject();
            if (!string.IsNullOrEmpty(_selectedSegmentFrom) && !string.IsNullOrEmpty(_selectedSegmentTo))
            {
                node["selectedSegmentFrom"] = _selectedSegmentFrom;
                node["selectedSegmentTo"] = _selectedSegmentTo;
            }
            else
            {
                node.Remove("selectedSegmentFrom");
                node.Remove("selectedSegmentTo");
            }

            var json = node.ToJsonString();
            var bounds = resetMapView
                ? BuildBoundsJson(stopsForBounds ?? CollectStopsForSelectedRoute())
                : null;
            PushDraftToMapRequested?.Invoke(json, bounds, resetMapView);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karte konnte nicht aktualisiert werden: {ex.Message}";
        }
    }

    private IList<RouteStopItem> CollectStopsForSelectedRoute()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return [];
        }

        var editor = AppServices.Routes.Editor;
        return editor?.GetStops(SelectedRoute).Where(s => !s.IsWaypoint).ToList() ?? [];
    }

    private static string? BuildBoundsJson(IList<RouteStopItem> stops)
    {
        var pts = new StringBuilder();
        foreach (var stop in stops)
        {
            if (!RouteCoordinateParser.TryParse(stop.StopCoordinates, out var lat, out var lon) &&
                !RouteCoordinateParser.TryParse(stop.GpsCoordinates, out lat, out lon))
            {
                continue;
            }

            if (pts.Length > 0) pts.Append(',');
            pts.Append('[')
                .Append(lat.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(lon.ToString(CultureInfo.InvariantCulture))
                .Append(']');
        }

        return pts.Length == 0 ? null : $"[{pts}]";
    }

    public void NotifyMapStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusMessage = message;
        }
    }
}
