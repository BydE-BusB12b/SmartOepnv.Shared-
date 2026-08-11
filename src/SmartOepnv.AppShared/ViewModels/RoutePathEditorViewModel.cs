using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.AppShared.ViewModels;

public sealed record RouteLineColorOption(string Label, string Hex);

/// <summary>Speichern-Button: Blau = ungespeicherte Änderungen, Orange = speichert, Grün = gespeichert.</summary>
public enum RoutePathSaveButtonState
{
    Unsaved,
    Saving,
    Saved
}

public partial class RoutePathEditorViewModel : ObservableObject
{
    private const int MaxUndoSteps = 30;

    private readonly OsrmSnapService _osrm = new();
    private readonly List<RoutePathDraft> _undoStack = [];
    private RoutePathDraft? _draft;
    private string? _selectedSegmentFrom;
    private string? _selectedSegmentTo;
    private int _selectedManeuverIndex;
    private int? _activeEditSegmentOrder;
    private string? _lastManualConnectFrom;
    private string? _lastManualConnectTo;
    private readonly HashSet<string> _edgesNeedingResnap = new(StringComparer.Ordinal);
    private int _draftGeneration;
    /// <summary>Zuletzt aus der Karte übernommene <c>mapEditGeneration</c> (Doppelklick-Symbole).</summary>
    private int _lastAppliedMapEditGeneration;
    private CancellationTokenSource? _previewSnapCts;
    private bool _suppressDirtyTracking;
    private bool _suppressNavManeuverSelectionSync;
    private string? _selectedNavMarkerKey;
    /// <summary>Planer hat gerade loadDraft gesendet – Karten-JSON bis draftLoaded ignorieren (verhindert Doppel-Liste nach „Symbol übernehmen“).</summary>
    private bool _awaitingMapLoadAfterPlannerPush;
    private bool _applyingNavSymbol;
    private readonly object _navListLock = new();

    public event Action<string, string?, bool, bool>? PushDraftToMapRequested;

    /// <summary>Listet den gewählten Hinweis in der Sidebar sichtbar (Karten↔Liste).</summary>
    /// <summary>Liest den aktuellen Karten-Entwurf (WebView) – gesetzt von RoutePathEditorView.</summary>
    public Func<Task<string?>>? PullMapDraftJsonAsync { get; set; }

    [ObservableProperty] private string statusMessage = "Route wählen und Fahrweg planen.";
    /// <summary>Warnung bei verdoppeltem/zu langem Fahrweg (Nav-Übernahme).</summary>
    [ObservableProperty] private string? integrityWarning;
    [ObservableProperty] private string? selectedRoute;
    [ObservableProperty] private bool isBusy;

    public bool HasIntegrityWarning => !string.IsNullOrWhiteSpace(IntegrityWarning);

    partial void OnIntegrityWarningChanged(string? value) =>
        OnPropertyChanged(nameof(HasIntegrityWarning));
    [ObservableProperty] private string? selectedManeuverText;
    [ObservableProperty] private string selectedNavSymbol = "straight";
    [ObservableProperty] private RoutePathNavManeuverListItem? selectedNavManeuverItem;

    [ObservableProperty]
    private ObservableCollection<RoutePathNavManeuverListItem> navManeuverItems = new();

    public bool HasNavManeuvers => NavManeuverItems.Count > 0;

    partial void OnNavManeuverItemsChanged(ObservableCollection<RoutePathNavManeuverListItem> value) =>
        OnPropertyChanged(nameof(HasNavManeuvers));

    public IReadOnlyList<NavSymbolPickerOption> NavSymbolPickerOptions { get; } =
        NavSymbolCatalog.All
            .Select(x => new NavSymbolPickerOption(x.Id, x.Label, NavSymbolImageHelper.GetImageUri(x.Id)))
            .ToList();

    /// <summary>
    /// Ausgewähltes Symbol inkl. Label/Icon für die ComboBox-Anzeige neben dem Dropdown-Pfeil.
    /// </summary>
    public NavSymbolPickerOption? SelectedNavSymbolOption
    {
        get => NavSymbolPickerOptions.FirstOrDefault(o =>
            string.Equals(o.Id, SelectedNavSymbol, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null)
            {
                return;
            }

            if (string.Equals(SelectedNavSymbol, value.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectedNavSymbol = value.Id;
        }
    }

    partial void OnSelectedNavSymbolChanged(string value) =>
        OnPropertyChanged(nameof(SelectedNavSymbolOption));

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
    [ObservableProperty] private RoutePathSaveButtonState saveButtonState = RoutePathSaveButtonState.Saved;

    public IReadOnlyList<string> AvailableRoutes
    {
        get
        {
            var editor = AppServices.Routes.Editor;
            if (editor is null)
            {
                return [];
            }

            return RouteDisplayHelper.SortRoutesByLineCourseAndTrip(editor.RouteNames);
        }
    }

    partial void OnSelectedRouteChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        LoadDraftForRoute(value);
    }

    partial void OnSaveButtonStateChanged(RoutePathSaveButtonState value)
    {
        SaveToPackageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedNavManeuverItemChanged(RoutePathNavManeuverListItem? value)
    {
        if (_suppressNavManeuverSelectionSync)
        {
            return;
        }

        if (value is null)
        {
            _selectedNavMarkerKey = null;
            PushDraftToMap();
            DeleteNavSymbolCommand.NotifyCanExecuteChanged();
            return;
        }

        _selectedSegmentFrom = value.FromNodeId;
        _selectedSegmentTo = value.ToNodeId;
        _selectedManeuverIndex = value.ManeuverIndex;
        _selectedNavMarkerKey = value.MapMarkerKey;
        SelectedNavSymbol = value.SymbolTypeId;
        SelectedManeuverText = value.Instruction;
        StatusMessage = $"Navi-Hinweis {value.DisplayNumber} gewählt – Symbol oder Text anpassen.";
        PushDraftToMap(skipNavListRefresh: true);
        DeleteNavSymbolCommand.NotifyCanExecuteChanged();
    }

    private void MarkDraftDirty()
    {
        if (_suppressDirtyTracking || SaveButtonState == RoutePathSaveButtonState.Saving)
        {
            return;
        }

        SaveButtonState = RoutePathSaveButtonState.Unsaved;
    }

    private void MarkDraftSaved()
    {
        SaveButtonState = RoutePathSaveButtonState.Saved;
    }

    public void ReloadMapFromDraft()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return;
        }

        PushDraftToMap(CollectStopsForSelectedRoute(), resetMapView: false, restoreMapView: HasSavedMapView(_draft));
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
        if (!_suppressDirtyTracking)
        {
            MarkDraftDirty();
        }

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

        _suppressDirtyTracking = true;
        try
        {
            var stops = editor.GetStops(routeName).Where(s => !s.IsWaypoint).ToList();
            _undoStack.Clear();
            CanUndo = false;
            UndoLastChangeCommand.NotifyCanExecuteChanged();

            _draft = RoutePathDraftRepository.LoadOrCreate(routeName, stops, editor.PackageRoot);
            RoutePathSegmentOrdering.RenumberContiguous(_draft);
            // Nav-Übernahme kann Rest-Zweige in der Segmentliste lassen → Shape-Ende falsch
            RoutePathDraftRepair.ReorderSegmentsAsSinglePath(_draft);
            RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(_draft);
            _draftGeneration = 0;
            _lastAppliedMapEditGeneration = 0;
            _activeEditSegmentOrder = PickDefaultEditSegmentOrder();
            // Kein EnsureStopsOnDraft hier: das würde Halt-/Ansage-Positionen aus der
            // Halteliste neu setzen und gespeicherte Karten-Verschiebungen verwerfen.
            // LoadOrCreate → RefreshNodesFromStops behält Lat/Lon und Manuell-Reihenfolge.
            SelectedRouteLineColor = string.IsNullOrWhiteSpace(_draft.RouteLineColor)
                ? "#2196f3"
                : _draft.RouteLineColor;
            ReportDraftStatus();
            MarkDraftSaved();
            PushDraftToMap(stops, resetMapView: !HasSavedMapView(_draft), restoreMapView: HasSavedMapView(_draft));
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
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
            // GPS aus Halteliste – nicht Positionen gelöschter Halte am gleichen Index behalten.
            EnsureStopsOnDraft(stops, preferStopListCoordinates: true);
        }

        // Punkt-zu-Punkt wie am Handy: nur Knoten, keine Auto-Kette zwischen allen Haltestellen.
        _draft.Segments.Clear();
        RoutePathDraftMutator.ClearSnapData(_draft);
        _lastManualConnectFrom = null;
        _lastManualConnectTo = null;
        _activeEditSegmentOrder = null;
        _selectedSegmentFrom = null;
        _selectedSegmentTo = null;
        StatusMessage =
            "Haltestellen auf der Karte – zwei Knoten nacheinander tippen (gelbe Linie), dann snappen.";
        _draft.MapViewLat = null;
        _draft.MapViewLon = null;
        _draft.MapViewZoom = null;
        MarkDraftDirty();
        PushDraftToMap(stops, resetMapView: true);
    }

    public void OnMapViewChangedFromMap(double lat, double lon, double zoom)
    {
        if (_draft is null || !double.IsFinite(lat) || !double.IsFinite(lon) || zoom <= 0)
        {
            return;
        }

        _draft.MapViewLat = lat;
        _draft.MapViewLon = lon;
        _draft.MapViewZoom = zoom;
    }

    private static bool HasSavedMapView(RoutePathDraft draft) =>
        draft.MapViewZoom is > 0 &&
        draft.MapViewLat is { } lat &&
        draft.MapViewLon is { } lon &&
        double.IsFinite(lat) &&
        double.IsFinite(lon);

    private void EnsureStopsOnDraft(IList<RouteStopItem> stops, bool preferStopListCoordinates = false)
    {
        if (_draft is null)
        {
            return;
        }

        // Gleiche Logik wie beim Laden: Positionen und Manuell-Reihenfolge behalten
        // (außer preferStopListCoordinates → GPS aus Halteliste, z. B. nach gelöschtem Halt).
        RoutePathNodeRefresh.RefreshNodesFromStops(_draft, stops, preferStopListCoordinates);
    }

    private void ReportDraftStatus()
    {
        if (_draft is null)
        {
            IntegrityWarning = null;
            StatusMessage = "Kein Entwurf geladen.";
            return;
        }

        var stopCount = _draft.Nodes.Count(n => n.Type == RoutePathNodeType.STOP);
        var annCount = _draft.Nodes.Count(n => n.Type == RoutePathNodeType.ANNOUNCEMENT);
        if (stopCount == 0 && annCount == 0)
        {
            IntegrityWarning = null;
            StatusMessage = "Keine GPS-Koordinaten in den Haltestellen – bitte unter „Routen“ gpsCoordinates/stopCoordinates pflegen, dann „Haltestellen laden“.";
            return;
        }

        RefreshIntegrityWarning();
        var length = RoutePathDraftIntegrity.FormatLengthSummary(_draft);
        StatusMessage =
            $"„{SelectedRoute}“ – {stopCount} Haltestellen, {annCount} Ansagepunkte, {_draft.Segments.Count} Verbindungen · {length}.";
    }

    private void RefreshIntegrityWarning()
    {
        IntegrityWarning = RoutePathDraftIntegrity.FormatWarning(
            RoutePathDraftIntegrity.Evaluate(_draft));
    }

    public bool ApplyDraftJsonFromMap(string json, bool recordUndo = false, bool forceFromMap = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var root = RoutePathDraftSerializer.CoerceToObject(JsonNode.Parse(json))
                       ?? throw new InvalidOperationException("Ungültiges JSON.");

            var manualConnectFrom = root["manualConnectFrom"]?.GetValue<string>()?.Trim();
            var manualConnectTo = root["manualConnectTo"]?.GetValue<string>()?.Trim();
            var forceApplyFromMap = !string.IsNullOrEmpty(manualConnectFrom) &&
                                    !string.IsNullOrEmpty(manualConnectTo);
            var incomingGeneration = root["draftGeneration"]?.GetValue<int>() ?? 0;
            var mapEditGeneration = root["mapEditGeneration"]?.GetValue<int>() ?? 0;
            var hasNavManeuverEdits = MapJsonHasNavManeuverEdits(root);
            var mapNavUserEdit = mapEditGeneration > _lastAppliedMapEditGeneration;
            var navForwardProgress = MapJsonNavIsForwardProgress(root);
            var navStaleDuplicate = MapJsonNavLooksLikeStaleDuplicate(root);
            // Navi-Sync: neue Karten-Edits (mapEditGeneration) oder mehr Manöver – nicht veraltete Doppel-Kopien.
            var forceNavManeuverSync = hasNavManeuverEdits &&
                                       (forceFromMap || !navStaleDuplicate) &&
                                       (forceFromMap ||
                                        mapNavUserEdit ||
                                        navForwardProgress) &&
                                       (incomingGeneration == 0 ||
                                        incomingGeneration >= _draftGeneration ||
                                        navForwardProgress);
            var allowWhileBlocked = forceFromMap || forceNavManeuverSync;

            // Nach PushDraftToMap: Karten-JSON ist veraltet, bis draftLoaded (sonst kommen gelöschte Symbole zurück).
            if (_awaitingMapLoadAfterPlannerPush && !forceFromMap && !forceApplyFromMap)
            {
                return false;
            }

            if (_applyingNavSymbol && !allowWhileBlocked)
            {
                return false;
            }

            // Karte kann kurz eine ältere draftGeneration senden (Race nach PushDraftToMap) – manuelle Verbindung trotzdem übernehmen.
            // forceFromMap (Speichern / expliziter Pull): Kartenstand immer übernehmen.
            if (!forceFromMap &&
                _draftGeneration > 0 &&
                incomingGeneration < _draftGeneration &&
                !forceApplyFromMap &&
                !forceNavManeuverSync)
            {
                StatusMessage =
                    $"Karten-Update verworfen (Entwurf Gen. {incomingGeneration}, Planer Gen. {_draftGeneration}) – erneut verbinden oder Snappen erneut klicken.";
                return false;
            }

            var previousSegmentCount = _draft?.Segments.Count ?? 0;
            if (recordUndo && _draft is not null)
            {
                PushUndoSnapshot();
            }

            var parsed = RoutePathDraftSerializer.FromJsonNode(root);
            if (!string.IsNullOrWhiteSpace(SelectedRoute))
            {
                parsed.RouteName = SelectedRoute;
            }

            if (forceApplyFromMap)
            {
                ClearSegmentSnapState(parsed, manualConnectFrom!, manualConnectTo!);
            }

            var movedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            if (forceFromMap && _draft is not null)
            {
                // Speichern / expliziter Pull: Karte ist Quelle der Wahrheit.
                // Alte Segmente/Snaps nicht zurückholen – sonst wirken Änderungen „ungespeichert“.
                movedNodeIds = DetectMovedNodeIds(_draft, parsed);
                parsed.CreatedAtEpochMs = _draft.CreatedAtEpochMs;
                MergePreservedSnapData(
                    _draft, parsed, manualConnectFrom, manualConnectTo, movedNodeIds, forceRestoreAllSnaps: false);
            }
            else if (_draft is not null)
            {
                parsed.CreatedAtEpochMs = _draft.CreatedAtEpochMs;
                movedNodeIds = DetectMovedNodeIds(_draft, parsed);
                // Neue gelbe Verbindung: Snap benachbarter Segments nicht löschen (Knoten-Precision / gemeinsamer Knoten).
                if (!forceApplyFromMap)
                {
                    foreach (var nodeId in movedNodeIds)
                    {
                        foreach (var seg in parsed.Segments.Where(s =>
                                     s.FromNodeId == nodeId || s.ToNodeId == nodeId))
                        {
                            ClearSegmentSnapState(parsed, seg.FromNodeId, seg.ToNodeId);
                        }
                    }
                }

                MergePreservedSnapData(_draft, parsed, manualConnectFrom, manualConnectTo, movedNodeIds);
            }

            if (_draft is not null && forceFromMap)
            {
                parsed.CreatedAtEpochMs = _draft.CreatedAtEpochMs;
            }

            SyncRoadManeuversFromMapSegmentSnaps(root, parsed);
            if (_draft is not null && !forceApplyFromMap && parsed.Segments.Count > _draft.Segments.Count)
            {
                // Veraltete Karten-Kopie nach Planer-Löschung darf gelöschte Verbindungen nicht zurückholen.
                var allowedKeys = _draft.Segments
                    .Select(s => RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId))
                    .ToHashSet(StringComparer.Ordinal);
                parsed.Segments = parsed.Segments
                    .Where(s => allowedKeys.Contains(RoutePathDraft.SegmentEdgeKey(s.FromNodeId, s.ToNodeId)))
                    .ToList();
            }

            _draft = parsed;
            if (incomingGeneration > 0)
            {
                _draftGeneration = Math.Max(_draftGeneration, incomingGeneration);
            }

            if (mapEditGeneration > 0)
            {
                _lastAppliedMapEditGeneration = Math.Max(_lastAppliedMapEditGeneration, mapEditGeneration);
            }

            RoutePathDraftMutator.DeduplicateSegmentsByEdge(_draft);
            var selectedDistanceBeforeDedupe = TryPeekSelectedManeuverDistanceM(_draft, root);
            RoutePathDraftMutator.DeduplicateManeuversPerEdge(_draft);
            RoutePathDraftMutator.EnsureBusStraightEdgeKeys(_draft);
            RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(_draft);
            SyncSelectionFromMapJson(root);
            RemapSelectedNavMarkerKeyAfterManeuverDedupe(selectedDistanceBeforeDedupe);
            MarkDraftDirty();

            if (movedNodeIds.Count > 0 && !forceApplyFromMap)
            {
                var touched = _draft.Segments
                    .Where(s => movedNodeIds.Contains(s.FromNodeId) || movedNodeIds.Contains(s.ToNodeId))
                    .OrderByDescending(s => s.Order)
                    .ToList();
                if (touched.Count > 0)
                {
                    foreach (var seg in touched)
                    {
                        _edgesNeedingResnap.Add(
                            RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId));
                    }

                    var focus = touched[0];
                    _selectedSegmentFrom = focus.FromNodeId;
                    _selectedSegmentTo = focus.ToNodeId;
                    PrepareSegmentForResnap(focus);
                    StatusMessage =
                        $"Punkt verschoben – Segment #{focus.Order} ({focus.FromNodeId} → {focus.ToNodeId}): Luftlinie, „Straße snappen“.";
                    PushDraftToMap();
                    return true;
                }
            }

            if (_draft.Segments.Count > previousSegmentCount)
            {
                var added = !string.IsNullOrEmpty(manualConnectFrom) && !string.IsNullOrEmpty(manualConnectTo)
                    ? _draft.Segments.FirstOrDefault(s =>
                        s.FromNodeId == manualConnectFrom && s.ToNodeId == manualConnectTo)
                    : null;
                added ??= _draft.Segments.MaxBy(s => s.Order);
                if (added is not null)
                {
                    RememberLastManualSegment(added.FromNodeId, added.ToNodeId, clearSnapState: false);
                    SetSelectedSegment(added.FromNodeId, added.ToNodeId, segmentOrder: added.Order);
                    StatusMessage =
                        $"Gelbe Verbindung #{added.Order} ({added.FromNodeId} → {added.ToNodeId}) – jetzt snappen.";
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(manualConnectFrom) && !string.IsNullOrEmpty(manualConnectTo))
            {
                RememberLastManualSegment(manualConnectFrom, manualConnectTo, clearSnapState: false);
                StatusMessage =
                    $"Gelbe Verbindung ({manualConnectFrom} → {manualConnectTo}) – „Straße snappen“ oder Busspur.";
                PushDraftToMap();
                return true;
            }

            StatusMessage = $"Entwurf aktualisiert – {_draft.Segments.Count} Verbindungen.";
            RefreshNavManeuverList(_selectedNavMarkerKey);
            // Nach Dedup/Index-Shift: Karten-Badges an die Listen-Nummern koppeln.
            PushDraftToMap(skipNavListRefresh: true);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karten-Sync fehlgeschlagen: {ex.Message}";
            return false;
        }
    }

    /// <summary>Karten-Auswahl ins ViewModel. Snap-Ziel nur über activeEditSegmentOrder, nicht über Manöver-Auswahl.</summary>
    private void SyncSelectionFromMapJson(JsonObject root)
    {
        if (_draft is null) return;

        if (root["activeEditSegmentOrder"]?.GetValue<int>() is int editOrder)
        {
            var editSeg = _draft.Segments.FirstOrDefault(s => s.Order == editOrder);
            if (editSeg is not null)
            {
                _activeEditSegmentOrder = editOrder;
                if (!IsSegmentBusStraight(editSeg))
                {
                    _lastManualConnectFrom = editSeg.FromNodeId;
                    _lastManualConnectTo = editSeg.ToNodeId;
                }
            }
        }

        var from = root["selectedSegmentFrom"]?.GetValue<string>()?.Trim();
        var to = root["selectedSegmentTo"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            return;
        }

        if (_draft.Segments.FirstOrDefault(s => s.FromNodeId == from && s.ToNodeId == to) is { } selectedSeg)
        {
            _selectedSegmentFrom = from;
            _selectedSegmentTo = to;
            if (!IsSegmentBusStraight(selectedSeg))
            {
                _lastManualConnectFrom = from;
                _lastManualConnectTo = to;
            }
        }

        var navKey = root["selectedNavMarkerKey"]?.GetValue<string>()?.Trim();
        if (!string.IsNullOrEmpty(navKey))
        {
            _selectedNavMarkerKey = navKey;
        }
    }

    /// <summary>
    /// Nach Dedup können <c>_m{i}</c>-Indizes verrutschen. Auswahl anhand Distanz neu binden.
    /// </summary>
    private void RemapSelectedNavMarkerKeyAfterManeuverDedupe(double? preferredDistanceM)
    {
        if (_draft is null ||
            string.IsNullOrWhiteSpace(_selectedSegmentFrom) ||
            string.IsNullOrWhiteSpace(_selectedSegmentTo))
        {
            return;
        }

        var key = RoutePathDraft.SegmentEdgeKey(_selectedSegmentFrom, _selectedSegmentTo);
        if (!_draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) || mans.Count == 0)
        {
            return;
        }

        var preferredIndex = 0;
        if (preferredDistanceM is double targetDist)
        {
            preferredIndex = mans
                .Select((m, i) => (Index: i, Delta: Math.Abs(m.DistanceM - targetDist)))
                .OrderBy(x => x.Delta)
                .ThenBy(x => x.Index)
                .First().Index;
        }
        else
        {
            preferredIndex = Math.Clamp(_selectedManeuverIndex, 0, mans.Count - 1);
        }

        _selectedManeuverIndex = preferredIndex;
        _selectedNavMarkerKey = $"{key}_m{preferredIndex}";
    }

    private static double? TryPeekSelectedManeuverDistanceM(RoutePathDraft draft, JsonObject root)
    {
        var navKey = root["selectedNavMarkerKey"]?.GetValue<string>()?.Trim();
        var from = root["selectedSegmentFrom"]?.GetValue<string>()?.Trim();
        var to = root["selectedSegmentTo"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            // Fallback: Key „from\u0001to_m{i}“
            if (string.IsNullOrEmpty(navKey))
            {
                return null;
            }

            var sep = navKey.LastIndexOf("_m", StringComparison.Ordinal);
            if (sep <= 0)
            {
                return null;
            }

            var edge = navKey[..sep];
            var parts = edge.Split('\u0001', 2);
            if (parts.Length != 2)
            {
                return null;
            }

            from = parts[0];
            to = parts[1];
            if (!int.TryParse(navKey[(sep + 2)..], out var idxFromKey))
            {
                return null;
            }

            var edgeKey = RoutePathDraft.SegmentEdgeKey(from, to);
            if (!draft.RoadSegmentManeuvers.TryGetValue(edgeKey, out var list) ||
                idxFromKey < 0 ||
                idxFromKey >= list.Count)
            {
                return null;
            }

            return list[idxFromKey].DistanceM;
        }

        var key = RoutePathDraft.SegmentEdgeKey(from, to);
        if (!draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) || mans.Count == 0)
        {
            return null;
        }

        var idx = 0;
        if (!string.IsNullOrEmpty(navKey) &&
            navKey.StartsWith(key + "_m", StringComparison.Ordinal) &&
            int.TryParse(navKey[(key.Length + 2)..], out var parsed))
        {
            idx = parsed;
        }

        idx = Math.Clamp(idx, 0, mans.Count - 1);
        return mans[idx].DistanceM;
    }

    private void SetActiveEditSegmentOrder(int order)
    {
        if (_draft is null) return;
        if (_draft.Segments.Any(s => s.Order == order))
        {
            _activeEditSegmentOrder = order;
        }
    }

    private static void ClearSegmentSnapState(RoutePathDraft draft, string from, string to)
    {
        var key = RoutePathDraft.SegmentEdgeKey(from, to);
        draft.RoadSnappedEdgeKeys.Remove(key);
        draft.RoadBusStraightEdgeKeys.Remove(key);
        draft.RoadSegmentPolylines.Remove(key);
        draft.RoadSegmentManeuvers.Remove(key);
        RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(draft);
    }

    /// <summary>Neu verbundene gelbe Kante merken; alte Snap/Bus-Markierungen an dieser Kante entfernen.</summary>
    private void RememberLastManualSegment(string from, string to, bool clearSnapState = true)
    {
        if (_draft is null) return;
        var seg = _draft.Segments.FirstOrDefault(s => s.FromNodeId == from && s.ToNodeId == to);
        if (seg is null) return;

        if (clearSnapState)
        {
            ClearSegmentSnapState(_draft, from, to);
        }

        RoutePathSegmentOrdering.ApplyOrderForNewEdge(_draft, from, to);
        _lastManualConnectFrom = from;
        _lastManualConnectTo = to;
        SetActiveEditSegmentOrder(seg.Order);
    }

    private int? PickDefaultEditSegmentOrder()
    {
        if (_draft is null || _draft.Segments.Count == 0) return null;

        return _draft.Segments
            .OrderByDescending(s => s.Order)
            .FirstOrDefault(s => IsSegmentOpen(s))
            ?.Order;
    }

    private void CancelPreviewSnap()
    {
        _previewSnapCts?.Cancel();
        _previewSnapCts?.Dispose();
        _previewSnapCts = null;
    }

    public void OnSegmentAddedFromMap(string? from, string? to)
    {
        // Verbindung wird in ApplyDraftJsonFromMap (draftChanged) übernommen – kein zweites PushDraftToMap.
        _ = from;
        _ = to;
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

        ClearSegmentSelectionAfterDelete();
        StatusMessage = $"Verbindung gelöscht – {_draft.Segments.Count} Verbindungen übrig.";
        MarkDraftDirty();
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
        MarkDraftDirty();
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
        MarkDraftDirty();
        PushDraftToMap();
    }

    [RelayCommand]
    private void RepairBloatedPath()
    {
        if (_draft is null)
        {
            StatusMessage = "Kein Entwurf geladen.";
            return;
        }

        PushUndoSnapshot();
        var ok = RoutePathDraftRepair.TryRepair(_draft);
        RefreshIntegrityWarning();
        StatusMessage = ok
            ? $"Fahrweg bereinigt · {RoutePathDraftIntegrity.FormatLengthSummary(_draft)}."
            : $"Bereinigung ausgeführt – bitte prüfen: {IntegrityWarning ?? RoutePathDraftIntegrity.FormatLengthSummary(_draft)}";
        MarkDraftDirty();
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
        MarkDraftDirty();
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

        ClearSegmentSelectionAfterDelete();
        StatusMessage = $"Verbindung gelöscht – {_draft.Segments.Count} übrig.";
        MarkDraftDirty();
        PushDraftToMap();
    }

    private void ClearSegmentSelectionAfterDelete()
    {
        _selectedSegmentFrom = null;
        _selectedSegmentTo = null;
        _selectedNavMarkerKey = null;
        _activeEditSegmentOrder = null;
        SelectedManeuverText = null;
        _suppressNavManeuverSelectionSync = true;
        SelectedNavManeuverItem = null;
        _suppressNavManeuverSelectionSync = false;
    }

    private static HashSet<string> DetectMovedNodeIds(RoutePathDraft previous, RoutePathDraft current)
    {
        var moved = new HashSet<string>(StringComparer.Ordinal);
        var prevById = previous.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var node in current.Nodes)
        {
            if (!prevById.TryGetValue(node.Id, out var prev))
            {
                continue;
            }

            if (Math.Abs(prev.Lat - node.Lat) > 1e-5 || Math.Abs(prev.Lon - node.Lon) > 1e-5)
            {
                moved.Add(node.Id);
            }
        }

        return moved;
    }

    /// <summary>Karte hat andere Navi-Manöver als der Planer-Entwurf (z. B. Doppelklick auf Waypoint-Route).</summary>
    private bool MapJsonHasNavManeuverEdits(JsonObject root)
    {
        if (_draft is null || root["segmentSnaps"] is not JsonArray snaps)
        {
            return false;
        }

        foreach (var snap in snaps.OfType<JsonObject>())
        {
            var from = snap["fromNodeId"]?.GetValue<string>()?.Trim();
            var to = snap["toNodeId"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                continue;
            }

            if (snap["maneuvers"] is not JsonArray mans || mans.Count == 0)
            {
                continue;
            }

            var key = RoutePathDraft.SegmentEdgeKey(from, to);
            if (!_draft.RoadSegmentManeuvers.TryGetValue(key, out var current) || current.Count != mans.Count)
            {
                return true;
            }

            for (var i = 0; i < mans.Count; i++)
            {
                if (mans[i] is not JsonObject mobj)
                {
                    continue;
                }

                var c = current[i];
                var sym = mobj["navSymbolType"]?.GetValue<string>();
                var ins = mobj["instruction"]?.GetValue<string>();
                var dist = mobj["distanceM"]?.GetValue<double>() ?? 0;
                if (!string.Equals(sym, c.NavSymbolType, StringComparison.Ordinal) ||
                    !string.Equals(ins, c.Instruction, StringComparison.Ordinal) ||
                    Math.Abs(dist - c.DistanceM) > 0.5)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool MapJsonNavIsForwardProgress(JsonObject root)
    {
        if (_draft is null)
        {
            return true;
        }

        var incoming = CountManeuversInMapJson(root);
        var current = CountManeuversInDraft(_draft);
        if (incoming > current)
        {
            return true;
        }

        if (incoming < current)
        {
            return false;
        }

        return MapJsonHasNavManeuverEdits(root);
    }

    private bool MapJsonNavLooksLikeStaleDuplicate(JsonObject root)
    {
        if (_draft is null || root["segmentSnaps"] is not JsonArray snaps)
        {
            return false;
        }

        foreach (var snap in snaps.OfType<JsonObject>())
        {
            var from = snap["fromNodeId"]?.GetValue<string>()?.Trim();
            var to = snap["toNodeId"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                continue;
            }

            if (snap["maneuvers"] is not JsonArray mans)
            {
                continue;
            }

            var key = RoutePathDraft.SegmentEdgeKey(from, to);
            if (!_draft.RoadSegmentManeuvers.TryGetValue(key, out var current) || current.Count == 0)
            {
                continue;
            }

            var incoming = RoutePathDraftSerializer.ParseManeuvers(mans);
            if (RoutePathDraftMutator.IsConcatenatedDuplicateOfPrevious(current, incoming))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountManeuversInMapJson(JsonObject root)
    {
        if (root["segmentSnaps"] is not JsonArray snaps)
        {
            return 0;
        }

        var count = 0;
        foreach (var snap in snaps.OfType<JsonObject>())
        {
            if (snap["maneuvers"] is JsonArray mans)
            {
                count += mans.Count;
            }
        }

        return count;
    }

    private static int CountManeuversInDraft(RoutePathDraft draft) =>
        draft.RoadSegmentManeuvers.Values.Sum(list => list.Count);

    /// <summary>Manöver aus Karten-<c>segmentSnaps</c> übernehmen (nach Merge, damit Doppelklick/Busspur nicht verworfen werden).</summary>
    private static void SyncRoadManeuversFromMapSegmentSnaps(JsonObject root, RoutePathDraft draft)
    {
        if (root["segmentSnaps"] is not JsonArray snaps)
        {
            return;
        }

        foreach (var snap in snaps.OfType<JsonObject>())
        {
            var from = snap["fromNodeId"]?.GetValue<string>()?.Trim();
            var to = snap["toNodeId"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                continue;
            }

            if (snap["maneuvers"] is not JsonArray manArr || manArr.Count == 0)
            {
                continue;
            }

            var key = RoutePathDraft.SegmentEdgeKey(from, to);
            draft.RoadSegmentManeuvers[key] = RoutePathDraftSerializer.ParseManeuvers(manArr);
            draft.RoadSnappedEdgeKeys.Add(key);
        }
    }

    private static void MergePreservedSnapData(
        RoutePathDraft previous,
        RoutePathDraft current,
        string? manualConnectFrom = null,
        string? manualConnectTo = null,
        IReadOnlySet<string>? skipRestoreForMovedNodes = null,
        bool forceRestoreAllSnaps = false)
    {
        var freshManualKey = !string.IsNullOrEmpty(manualConnectFrom) && !string.IsNullOrEmpty(manualConnectTo)
            ? RoutePathDraft.SegmentEdgeKey(manualConnectFrom, manualConnectTo)
            : null;

        if (forceRestoreAllSnaps)
        {
            foreach (var key in previous.RoadSegmentPolylines.Keys
                         .Concat(previous.RoadSegmentManeuvers.Keys)
                         .Concat(previous.RoadSnappedEdgeKeys)
                         .Concat(previous.RoadBusStraightEdgeKeys)
                         .Distinct(StringComparer.Ordinal))
            {
                if (freshManualKey is not null && string.Equals(key, freshManualKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (skipRestoreForMovedNodes is not null && EdgeTouchesNode(key, skipRestoreForMovedNodes))
                {
                    continue;
                }

                if (previous.RoadSegmentPolylines.TryGetValue(key, out var prevPts) && prevPts.Count >= 2)
                {
                    current.RoadSegmentPolylines[key] = prevPts
                        .Select(p => new RoutePathLatLng { Lat = p.Lat, Lon = p.Lon })
                        .ToList();
                }

                if (previous.RoadSegmentManeuvers.TryGetValue(key, out var prevMans) && prevMans.Count > 0)
                {
                    current.RoadSegmentManeuvers[key] = prevMans
                        .Select(m => new RoutePathSnapManeuver
                        {
                            DistanceM = m.DistanceM,
                            Instruction = m.Instruction,
                            CurrentStreet = m.CurrentStreet,
                            NextStreet = m.NextStreet,
                            NavSymbolType = m.NavSymbolType
                        })
                        .ToList();
                }

                if (previous.RoadSnappedEdgeKeys.Contains(key))
                {
                    current.RoadSnappedEdgeKeys.Add(key);
                }

                if (previous.RoadBusStraightEdgeKeys.Contains(key))
                {
                    current.RoadBusStraightEdgeKeys.Add(key);
                }
            }
        }

        foreach (var seg in current.Segments)
        {
            var key = RoutePathDraft.SegmentEdgeKey(seg.FromNodeId, seg.ToNodeId);
            var isFreshManualConnect = !string.IsNullOrEmpty(manualConnectFrom) &&
                                       !string.IsNullOrEmpty(manualConnectTo) &&
                                       seg.FromNodeId == manualConnectFrom &&
                                       seg.ToNodeId == manualConnectTo;
            if (isFreshManualConnect)
            {
                continue;
            }

            if (skipRestoreForMovedNodes is not null &&
                (skipRestoreForMovedNodes.Contains(seg.FromNodeId) ||
                 skipRestoreForMovedNodes.Contains(seg.ToNodeId)))
            {
                continue;
            }

            if (!current.RoadSegmentPolylines.ContainsKey(key) &&
                previous.RoadSegmentPolylines.TryGetValue(key, out var pts) &&
                pts.Count >= 2)
            {
                current.RoadSegmentPolylines[key] = pts;
                current.RoadSnappedEdgeKeys.Add(key);
            }
            else if (previous.RoadSnappedEdgeKeys.Contains(key) &&
                     current.RoadSegmentPolylines.TryGetValue(key, out var existingPts) &&
                     existingPts.Count >= 2)
            {
                current.RoadSnappedEdgeKeys.Add(key);
            }

            // Busspur nur für diese Kante mergen – und nicht über einen neuen Straßensnap legen.
            if (previous.RoadBusStraightEdgeKeys.Contains(key) &&
                !LooksLikeStreetSnapOverridingBus(current, key))
            {
                current.RoadBusStraightEdgeKeys.Add(key);
            }

            if (previous.RoadSegmentManeuvers.TryGetValue(key, out var prevMans))
            {
                if (!current.RoadSegmentManeuvers.TryGetValue(key, out var curMans) || curMans.Count == 0)
                {
                    current.RoadSegmentManeuvers[key] = prevMans;
                }
                else if (RoutePathDraftMutator.IsConcatenatedDuplicateOfPrevious(prevMans, curMans))
                {
                    // Verdoppelte Karten-Kopie (z. B. nach „Symbol übernehmen“ + Durchgriff) – Planer-Stand behalten.
                    current.RoadSegmentManeuvers[key] = prevMans;
                }
                else if (curMans.Count < prevMans.Count && current.RoadBusStraightEdgeKeys.Contains(key))
                {
                    // Busspur: Karte hat weniger Manöver als Planer – Planer-Stand behalten (nicht überschreiben).
                    current.RoadSegmentManeuvers[key] = prevMans;
                }
                else if (curMans.Count > prevMans.Count)
                {
                    current.RoadSegmentManeuvers[key] = curMans;
                }
            }

            if (current.RoadBusStraightEdgeKeys.Contains(key) &&
                previous.RoadSegmentPolylines.TryGetValue(key, out var prevBusPts) &&
                prevBusPts.Count >= 2 &&
                (!current.RoadSegmentPolylines.TryGetValue(key, out var curPts) || curPts.Count < 2))
            {
                current.RoadSegmentPolylines[key] = prevBusPts;
                current.RoadSnappedEdgeKeys.Add(key);
            }
        }
    }

    private static bool EdgeTouchesNode(string edgeKey, IReadOnlySet<string> nodeIds)
    {
        var parts = edgeKey.Split('\u0001', 2);
        return parts.Length == 2 &&
               (nodeIds.Contains(parts[0]) || nodeIds.Contains(parts[1]));
    }

    /// <summary>
    /// Aktueller Stand hat Bus-Key bewusst entfernt und liefert Straßen-Geometrie –
    /// vorherige Busspur-Markierung nicht wiederherstellen.
    /// </summary>
    private static bool LooksLikeStreetSnapOverridingBus(RoutePathDraft current, string key)
    {
        if (current.RoadBusStraightEdgeKeys.Contains(key))
        {
            return false;
        }

        if (!current.RoadSnappedEdgeKeys.Contains(key) ||
            !current.RoadSegmentPolylines.TryGetValue(key, out var pts) ||
            pts.Count < 4)
        {
            return false;
        }

        if (current.RoadSegmentManeuvers.TryGetValue(key, out var mans) &&
            mans.Any(m => (m.Instruction ?? string.Empty)
                .Contains("Busspur", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Genug Stützpunkte ohne Busspur-Text → Straßensnap, Bus-Key nicht zurückholen.
        return true;
    }

    private bool IsSegmentRoadSnapped(RoutePathSegment segment)
    {
        if (_draft is null) return false;
        if (IsSegmentBusStraight(segment)) return false;
        return HasRealRoadGeometry(segment);
    }

    private bool IsSegmentBusStraight(RoutePathSegment segment)
    {
        if (_draft is null) return false;
        var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
        return _draft.RoadBusStraightEdgeKeys.Contains(key);
    }

    private bool HasRealRoadGeometry(RoutePathSegment segment)
    {
        if (_draft is null) return false;
        return RoutePathSnapOrchestrator.SegmentHasRealRoadGeometry(_draft, segment);
    }

    private bool IsManualUnsnappedSegment(RoutePathSegment segment)
    {
        if (_draft is null) return false;
        if (IsSegmentBusStraight(segment)) return false;
        return !HasRealRoadGeometry(segment);
    }

    private bool IsSegmentOpen(RoutePathSegment segment)
    {
        if (_draft is null) return false;
        if (IsSegmentBusStraight(segment)) return false;
        return !HasRealRoadGeometry(segment);
    }

    private RoutePathSegment PrepareSegmentForResnap(RoutePathSegment segment)
    {
        ClearSegmentSnapState(_draft!, segment.FromNodeId, segment.ToNodeId);
        _lastManualConnectFrom = segment.FromNodeId;
        _lastManualConnectTo = segment.ToNodeId;
        SetActiveEditSegmentOrder(segment.Order);
        _selectedSegmentFrom = segment.FromNodeId;
        _selectedSegmentTo = segment.ToNodeId;
        return segment;
    }

    /// <summary>Gewählte oder nach Verschieben offene Kante für Snap/Busspur.</summary>
    private RoutePathSegment? ResolveSegmentForSnapOrBus()
    {
        if (_draft is null || _draft.Segments.Count == 0)
        {
            StatusMessage = "Zuerst Knoten A, dann Knoten B auf der Karte tippen (gelbe Linie).";
            return null;
        }

        RoutePathSegment? FindSegment(string? from, string? to, bool allowBus = false)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                return null;
            }

            var segment = _draft!.Segments.FirstOrDefault(s =>
                s.FromNodeId == from && s.ToNodeId == to);
            if (segment is null)
            {
                return null;
            }

            // Explizite Auswahl: Busspur → Straße snappen erlauben (Bus-Key wird in Prepare entfernt).
            if (allowBus)
            {
                return segment;
            }

            return IsSegmentBusStraight(segment) ? null : segment;
        }

        bool NeedsSnap(RoutePathSegment segment)
        {
            var key = RoutePathDraft.SegmentEdgeKey(segment.FromNodeId, segment.ToNodeId);
            return _edgesNeedingResnap.Contains(key) || IsSegmentOpen(segment);
        }

        // 1. Explizites Karten-Ziel (Linien-Klick / neue Verbindung) – vor älterer Segment-Auswahl.
        if (_activeEditSegmentOrder is int editOrder)
        {
            var editing = _draft.Segments.FirstOrDefault(s => s.Order == editOrder);
            if (editing is not null)
            {
                return editing;
            }
        }

        // 2. Zuletzt manuell verbundene gelbe Kante.
        var manual = FindSegment(_lastManualConnectFrom, _lastManualConnectTo);
        if (manual is not null && NeedsSnap(manual))
        {
            return manual;
        }

        // 3. Nach Knoten-Verschieben offene Kanten.
        foreach (var key in _edgesNeedingResnap.ToList())
        {
            var parts = key.Split('\u0001', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var pending = FindSegment(parts[0], parts[1]);
            if (pending is not null)
            {
                return pending;
            }

            _edgesNeedingResnap.Remove(key);
        }

        // 4. Auswahl nur, wenn das Teilstück noch gelb/offen ist (nicht bereits gesnapptes Blau).
        //    Busspur-Auswahl: trotzdem erlauben, damit „Straße snappen“ die Busspur ersetzen kann.
        var selected = FindSegment(_selectedSegmentFrom, _selectedSegmentTo, allowBus: true);
        if (selected is not null && (NeedsSnap(selected) || IsSegmentBusStraight(selected)))
        {
            return selected;
        }

        var newestOpen = _draft.Segments
            .OrderByDescending(s => s.Order)
            .FirstOrDefault(s => IsSegmentOpen(s));
        if (newestOpen is not null)
        {
            return newestOpen;
        }

        var newestUnsnapped = _draft.Segments
            .OrderByDescending(s => s.Order)
            .FirstOrDefault(s => !IsSegmentBusStraight(s) && !IsSegmentRoadSnapped(s));
        if (newestUnsnapped is not null)
        {
            _lastManualConnectFrom = newestUnsnapped.FromNodeId;
            _lastManualConnectTo = newestUnsnapped.ToNodeId;
            return newestUnsnapped;
        }

        StatusMessage =
            "Kein gelbes Teilstück – zuerst zwei Knoten verbinden (gelbe Linie).";
        return null;
    }

    private async Task<bool> TrySyncDraftFromMapAsync(bool pickUnsnappedTarget = true)
    {
        if (PullMapDraftJsonAsync is null)
        {
            return false;
        }

        try
        {
            var json = await PullMapDraftJsonAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            if (!ApplyDraftJsonFromMap(json, recordUndo: false, forceFromMap: true))
            {
                return false;
            }

            if (pickUnsnappedTarget)
            {
                PickLastUnsnappedSegmentTarget();
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karten-Sync vor Snappen fehlgeschlagen: {ex.Message}";
            return false;
        }
    }

    private void PickLastUnsnappedSegmentTarget()
    {
        if (_draft is null || _draft.Segments.Count == 0)
        {
            return;
        }

        var open = _draft.Segments
            .Where(s => IsSegmentOpen(s))
            .OrderByDescending(s => s.Order)
            .FirstOrDefault();
        if (open is null)
        {
            return;
        }

        _lastManualConnectFrom = open.FromNodeId;
        _lastManualConnectTo = open.ToNodeId;
        SetActiveEditSegmentOrder(open.Order);
    }

    private void CommitSegmentEditTarget(RoutePathSegment segment)
    {
        SetActiveEditSegmentOrder(segment.Order);
        _selectedSegmentFrom = segment.FromNodeId;
        _selectedSegmentTo = segment.ToNodeId;
    }

    /// <summary>Löschen u. ä.: gewähltes Teilstück oder letztes.</summary>
    private RoutePathSegment? ResolveSelectedOrLastSegment()
    {
        if (_draft is null || _draft.Segments.Count == 0) return null;

        if (!string.IsNullOrEmpty(_selectedSegmentFrom) && !string.IsNullOrEmpty(_selectedSegmentTo))
        {
            var selected = _draft.Segments.FirstOrDefault(s =>
                s.FromNodeId == _selectedSegmentFrom && s.ToNodeId == _selectedSegmentTo);
            if (selected is not null) return selected;
        }

        return _draft.Segments.MaxBy(s => s.Order);
    }

    [RelayCommand]
    private async Task ApplyBusStraightLane()
    {
        if (_draft is null || _draft.Segments.Count == 0)
        {
            StatusMessage = "Keine Verbindung vorhanden.";
            return;
        }

        CancelPreviewSnap();
        await TrySyncDraftFromMapAsync(pickUnsnappedTarget: false);
        var target = ResolveSegmentForSnapOrBus();
        if (target is null)
        {
            return;
        }

        StatusMessage =
            $"Busspur für Teilstück #{target.Order} ({target.FromNodeId} → {target.ToNodeId})…";
        PushUndoSnapshot();
        var busKey = RoutePathDraft.SegmentEdgeKey(target.FromNodeId, target.ToNodeId);
        var preserveManeuvers = IsSegmentBusStraight(target) &&
                                _draft.RoadSegmentManeuvers.TryGetValue(busKey, out var existingBus) &&
                                existingBus.Count > 0;
        if (!preserveManeuvers)
        {
            PrepareSegmentForResnap(target);
        }

        RoutePathBusLaneHelper.ApplyBusStraightToSegment(_draft, target, preserveManeuvers);
        SetActiveEditSegmentOrder(target.Order);
        _edgesNeedingResnap.Remove(RoutePathDraft.SegmentEdgeKey(target.FromNodeId, target.ToNodeId));
        RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(_draft);
        SelectedManeuverText = "Geradeaus (Busspur / direkt)";
        SelectedNavSymbol = "straight";
        CommitSegmentEditTarget(target);
        StatusMessage =
            $"Segment #{target.Order} ({target.FromNodeId} → {target.ToNodeId}): Busspur/direkt gesetzt.";
        MarkDraftDirty();
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

    public void OnNodeMovedFromMap(string? nodeId)
    {
        // Snap-Löschen + Karte: erfolgt in ApplyDraftJsonFromMap (draftChanged nach dragend).
        _ = nodeId;
    }

    public void SchedulePreviewSnapForNode(string? nodeId)
    {
        OnNodeMovedFromMap(nodeId);
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

    public void SetSelectedSegment(
        string? from,
        string? to,
        int? maneuverIndex = null,
        int? segmentOrder = null)
    {
        _selectedSegmentFrom = from;
        _selectedSegmentTo = to;
        _selectedManeuverIndex = maneuverIndex ?? 0;
        if (segmentOrder is int order && _draft is not null)
        {
            var seg = _draft.Segments.FirstOrDefault(s => s.Order == order);
            if (seg is not null)
            {
                SetActiveEditSegmentOrder(order);
                _selectedSegmentFrom = seg.FromNodeId;
                _selectedSegmentTo = seg.ToNodeId;
                if (IsManualUnsnappedSegment(seg))
                {
                    RememberLastManualSegment(seg.FromNodeId, seg.ToNodeId, clearSnapState: true);
                }
                else if (!IsSegmentBusStraight(seg))
                {
                    _lastManualConnectFrom = seg.FromNodeId;
                    _lastManualConnectTo = seg.ToNodeId;
                }
            }
        }

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
            SelectedNavSymbol = string.IsNullOrWhiteSpace(m.NavSymbolType) ? "straight" : m.NavSymbolType;
            SelectedManeuverText = NavManeuverHelper.GetDisplayInstruction(m, SelectedNavSymbol);
            StatusMessage = $"Manöver {idx + 1}/{mans.Count} gewählt.";
        }
        else if ((_draft.Segments.FirstOrDefault(s => s.FromNodeId == from && s.ToNodeId == to) is { } picked &&
                 IsSegmentRoadSnapped(picked)) ||
                 (_draft.RoadSegmentPolylines.TryGetValue(key, out var poly) && poly.Count >= 2))
        {
            SelectedManeuverText = "Manuell";
            StatusMessage =
                "Gesnapptes Teilstück – Doppelklick auf die Linie für Navi-Symbol oder rechts „Übernehmen“.";
        }
        else
        {
            SelectedManeuverText = "Noch nicht gesnappt";
            StatusMessage = "Segment gewählt – „Straße snappen (Auswahl)“ für nur dieses Teilstück.";
        }

        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to) &&
            _draft?.RoadSegmentManeuvers.TryGetValue(
                RoutePathDraft.SegmentEdgeKey(from, to), out var segMans) == true &&
            segMans.Count > 0)
        {
            _selectedNavMarkerKey =
                $"{RoutePathDraft.SegmentEdgeKey(from, to)}_m{Math.Clamp(_selectedManeuverIndex, 0, segMans.Count - 1)}";
        }

        PushDraftToMap();
    }

    public void SelectNavManeuverFromMap(
        string? from,
        string? to,
        int? maneuverIndex,
        string? symbolType,
        string? instruction)
    {
        if (_applyingNavSymbol)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        _selectedSegmentFrom = from;
        _selectedSegmentTo = to;
        _selectedManeuverIndex = maneuverIndex ?? 0;
        _selectedNavMarkerKey =
            $"{RoutePathDraft.SegmentEdgeKey(from, to)}_m{_selectedManeuverIndex}";

        if (!string.IsNullOrWhiteSpace(symbolType))
        {
            SelectedNavSymbol = symbolType;
        }

        if (_draft is not null)
        {
            var key = RoutePathDraft.SegmentEdgeKey(from, to);
            if (_draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) && mans.Count > 0)
            {
                var idx = Math.Clamp(_selectedManeuverIndex, 0, mans.Count - 1);
                var sym = string.IsNullOrWhiteSpace(symbolType)
                    ? mans[idx].NavSymbolType
                    : symbolType;
                SelectedManeuverText = NavManeuverHelper.GetDisplayInstruction(
                    mans[idx],
                    string.IsNullOrWhiteSpace(sym) ? "straight" : sym!);
            }
        }
        else if (!string.IsNullOrWhiteSpace(instruction))
        {
            SelectedManeuverText = instruction;
        }

        StatusMessage = "Navi-Hinweis ausgewählt – Symbol/Anweisung rechts ändern und übernehmen.";

        var markerKey = _selectedNavMarkerKey;
        if (SelectedNavManeuverItem?.MapMarkerKey != markerKey)
        {
            SelectNavManeuverInListByMarkerKey(markerKey);
        }

        DeleteNavSymbolCommand.NotifyCanExecuteChanged();
    }

    public void ClearNavSymbolSelectionFromMap()
    {
        if (_applyingNavSymbol)
        {
            return;
        }

        _selectedNavMarkerKey = null;
        _selectedSegmentFrom = null;
        _selectedSegmentTo = null;
        SelectedManeuverText = null;
        _suppressNavManeuverSelectionSync = true;
        SelectedNavManeuverItem = null;
        _suppressNavManeuverSelectionSync = false;
        DeleteNavSymbolCommand.NotifyCanExecuteChanged();
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
        MarkDraftDirty();
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

        await TrySyncDraftFromMapAsync(pickUnsnappedTarget: false);

        var segment = ResolveSegmentForSnapOrBus();
        if (segment is null)
        {
            return;
        }

        CancelPreviewSnap();
        var from = segment.FromNodeId;
        var to = segment.ToNodeId;
        PushUndoSnapshot();
        IsBusy = true;
        StatusMessage = $"Straßensnap für Segment #{segment.Order} (Knoten {from} → {to})…";
        try
        {
            if (IsSegmentOpen(segment))
            {
                RoutePathSegmentOrdering.ApplyOrderForNewEdge(_draft, from, to);
            }

            PrepareSegmentForResnap(segment);
            await RoutePathSnapOrchestrator.SnapSegmentAsync(_draft, from, to, _osrm);
            CommitSegmentEditTarget(segment);
            var key = RoutePathDraft.SegmentEdgeKey(from, to);
            _edgesNeedingResnap.Remove(key);
            var ptCount = _draft.RoadSegmentPolylines.TryGetValue(key, out var poly) ? poly.Count : 0;
            StatusMessage =
                $"Segment #{segment.Order} ({from} → {to}) gesnappt – {ptCount} Straßenpunkte.";
            MarkDraftDirty();
            PushDraftToMap();
        }
        catch (Exception ex)
        {
            var restored = PopUndoSnapshot();
            if (restored is not null)
            {
                _draft = restored;
            }

            StatusMessage = $"Einzel-Snap fehlgeschlagen: {ex.Message}";
            MarkDraftDirty();
            PushDraftToMap();
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

        // Wie Android: Auswahl existiert, andere schon gesnappt → nur dieses Teilstück.
        var selected = ResolveSegmentForSnapOrBus();
        if (selected is not null &&
            _draft.RoadSnappedEdgeKeys.Count > 0 &&
            !IsSegmentRoadSnapped(selected))
        {
            await SnapSelectedSegmentAsync();
            return;
        }

        PushUndoSnapshot();
        IsBusy = true;
        StatusMessage = "OSRM-Snap läuft…";
        try
        {
            await RoutePathSnapOrchestrator.SnapAllSegmentsAsync(_draft, _osrm);
            RefreshIntegrityWarning();
            StatusMessage = IntegrityWarning is null
                ? $"Straßenzug fertig – {_draft.RoadSnappedEdgeKeys.Count} Segmente gesnappt · {RoutePathDraftIntegrity.FormatLengthSummary(_draft)}."
                : $"Straßenzug fertig – {_draft.RoadSnappedEdgeKeys.Count} Segmente · {RoutePathDraftIntegrity.FormatLengthSummary(_draft)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Snap fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        MarkDraftDirty();
        PushDraftToMap();
        if (CommitDraftToWorkspace())
        {
            StatusMessage += " – lokal gespeichert.";
            MarkDraftSaved();
        }
    }

    [RelayCommand]
    private void ApplyNavSymbol()
    {
        if (_draft is null)
        {
            return;
        }

        _applyingNavSymbol = true;
        _suppressNavManeuverSelectionSync = true;

        var from = SelectedNavManeuverItem?.FromNodeId ?? _selectedSegmentFrom;
        var to = SelectedNavManeuverItem?.ToNodeId ?? _selectedSegmentTo;
        var maneuverIndex = SelectedNavManeuverItem?.ManeuverIndex ?? _selectedManeuverIndex;

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            _applyingNavSymbol = false;
            StatusMessage = "Bitte zuerst einen Hinweis in der Liste oder ein Navi-Symbol auf der Karte wählen.";
            return;
        }

        try
        {
            PushUndoSnapshot();
            var key = RoutePathDraft.SegmentEdgeKey(from, to);
            if (_draft.RoadSegmentPolylines.TryGetValue(key, out _))
            {
                _draft.RoadSnappedEdgeKeys.Add(key);
            }

            var symbolLabel = NavSymbolCatalog.GetLabel(SelectedNavSymbol);
            if (!_draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) || mans.Count == 0)
            {
                mans =
                [
                    new RoutePathSnapManeuver
                    {
                        DistanceM = 0,
                        Instruction = ResolveInstructionForApply(symbolLabel),
                        NavSymbolType = SelectedNavSymbol
                    }
                ];
                _draft.RoadSegmentManeuvers[key] = mans;
                maneuverIndex = 0;
            }
            else
            {
                var idx = Math.Clamp(maneuverIndex, 0, mans.Count - 1);
                var wasManual = NavManeuverHelper.IsManualManeuver(mans[idx]);
                mans[idx].NavSymbolType = SelectedNavSymbol;
                mans[idx].Instruction = ResolveInstructionForApply(symbolLabel, mans[idx]);

                if (wasManual)
                {
                    NavManeuverHelper.SuppressNearbyAutoManeuvers(mans, mans[idx].DistanceM);
                }

                maneuverIndex = idx;
            }

            _selectedSegmentFrom = from;
            _selectedSegmentTo = to;
            _selectedManeuverIndex = maneuverIndex;
            _selectedNavMarkerKey = $"{key}_m{maneuverIndex}";

            RoutePathDraftMutator.DeduplicateSegmentsByEdge(_draft);
            RoutePathDraftMutator.DeduplicateManeuversPerEdge(_draft);
            RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(_draft);
            StatusMessage = $"Navi-Symbol „{symbolLabel}“ gesetzt.";
            MarkDraftDirty();
            RefreshNavManeuverList(_selectedNavMarkerKey);
            PushDraftToMap(skipNavListRefresh: true);
        }
        catch (Exception ex)
        {
            _applyingNavSymbol = false;
            StatusMessage = $"Symbol konnte nicht übernommen werden: {ex.Message}";
        }
        finally
        {
            _suppressNavManeuverSelectionSync = false;
            ScheduleClearApplyingNavSymbol();
        }
    }

    private bool CanDeleteNavSymbol() => TryResolveSelectedNavManeuver(out _, out _, out _, out _);

    [RelayCommand(CanExecute = nameof(CanDeleteNavSymbol))]
    private void DeleteNavSymbol()
    {
        if (_draft is null || !TryResolveSelectedNavManeuver(out var from, out var to, out var maneuverIndex, out var maneuver))
        {
            StatusMessage = "Bitte zuerst ein Navi-Symbol auf der Karte oder in der Liste wählen.";
            return;
        }

        _applyingNavSymbol = true;
        _suppressNavManeuverSelectionSync = true;

        try
        {
            PushUndoSnapshot();
            var key = RoutePathDraft.SegmentEdgeKey(from, to);
            if (!_draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) || mans.Count == 0)
            {
                StatusMessage = "Kein Navi-Hinweis zum Löschen gefunden.";
                return;
            }

            var idx = Math.Clamp(maneuverIndex, 0, mans.Count - 1);
            mans.RemoveAt(idx);
            if (mans.Count == 0)
            {
                _draft.RoadSegmentManeuvers.Remove(key);
            }

            _selectedSegmentFrom = null;
            _selectedSegmentTo = null;
            _selectedManeuverIndex = 0;
            _selectedNavMarkerKey = null;
            SelectedManeuverText = null;
            SelectedNavSymbol = "straight";
            _suppressNavManeuverSelectionSync = true;
            SelectedNavManeuverItem = null;
            _suppressNavManeuverSelectionSync = false;

            RoutePathDraftMutator.DeduplicateSegmentsByEdge(_draft);
            RoutePathDraftMutator.DeduplicateManeuversPerEdge(_draft);
            RoutePathSnapOrchestrator.RebuildMergedShapeAndManeuvers(_draft);
            StatusMessage = "Navi-Symbol entfernt.";
            MarkDraftDirty();
            RefreshNavManeuverList();
            PushDraftToMap(skipNavListRefresh: true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Symbol konnte nicht gelöscht werden: {ex.Message}";
        }
        finally
        {
            _suppressNavManeuverSelectionSync = false;
            DeleteNavSymbolCommand.NotifyCanExecuteChanged();
        }
    }

    private bool TryResolveSelectedNavManeuver(
        out string from,
        out string to,
        out int maneuverIndex,
        out RoutePathSnapManeuver maneuver)
    {
        from = SelectedNavManeuverItem?.FromNodeId ?? _selectedSegmentFrom ?? string.Empty;
        to = SelectedNavManeuverItem?.ToNodeId ?? _selectedSegmentTo ?? string.Empty;
        maneuverIndex = SelectedNavManeuverItem?.ManeuverIndex ?? _selectedManeuverIndex;
        maneuver = null!;

        if (_draft is null || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            return false;
        }

        var key = RoutePathDraft.SegmentEdgeKey(from, to);
        if (!_draft.RoadSegmentManeuvers.TryGetValue(key, out var mans) || mans.Count == 0)
        {
            return false;
        }

        var idx = Math.Clamp(maneuverIndex, 0, mans.Count - 1);
        maneuver = mans[idx];
        var fromId = from;
        var toId = to;
        var segment = _draft.Segments.FirstOrDefault(s => s.FromNodeId == fromId && s.ToNodeId == toId)
                      ?? new RoutePathSegment { FromNodeId = fromId, ToNodeId = toId };
        var symbolType = NavManeuverDisplayHelper.EffectiveSymbolType(maneuver);
        var segmentLength = NavManeuverDisplayHelper.SegmentLengthMeters(_draft, segment);
        return NavManeuverDisplayHelper.ShouldShowOnMap(maneuver, symbolType, segmentLength);
    }

    /// <summary>Manuell/leer → Symbolname; sonst eigener Anweisungstext aus dem Feld.</summary>
    private string ResolveInstructionForApply(string symbolLabel, RoutePathSnapManeuver? existing = null)
    {
        var custom = (SelectedManeuverText ?? string.Empty).Trim();
        if (existing is not null &&
            !NavManeuverHelper.IsManualManeuver(existing) &&
            !string.Equals((existing.Instruction ?? string.Empty).Trim(), NavManeuverHelper.ManualInstruction, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(custom) &&
            !string.Equals(custom, NavManeuverHelper.ManualInstruction, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(custom, symbolLabel, StringComparison.Ordinal))
        {
            return custom;
        }

        return symbolLabel;
    }

    private void ScheduleClearApplyingNavSymbol()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => _applyingNavSymbol = false,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public void OnMapDraftLoadedFromView()
    {
        _awaitingMapLoadAfterPlannerPush = false;
        _applyingNavSymbol = false;
        _lastAppliedMapEditGeneration = 0;
    }

    private bool CanSaveToPackage() =>
        SaveButtonState != RoutePathSaveButtonState.Saving &&
        _draft is not null &&
        !string.IsNullOrWhiteSpace(SelectedRoute);

    [RelayCommand(CanExecute = nameof(CanSaveToPackage))]
    private async Task SaveToPackageAsync()
    {
        if (_draft is null || string.IsNullOrWhiteSpace(SelectedRoute))
        {
            StatusMessage = "Nichts zu speichern.";
            return;
        }

        SaveButtonState = RoutePathSaveButtonState.Saving;
        IsBusy = true;
        try
        {
            if (!await TrySyncDraftFromMapAsync())
            {
                MarkDraftDirty();
                StatusMessage =
                    "Speichern abgebrochen – Kartenstand konnte nicht übernommen werden. Bitte kurz warten und erneut speichern.";
                return;
            }

            NavManeuverHelper.NormalizeManualManeuverInstructions(_draft!);
            RoutePathDraftMutator.EnsureBusStraightEdgeKeys(_draft!);
            await Task.Yield();
            // Auf UI-Thread speichern – _draft darf nicht parallel zu Karten-Events geändert werden.
            var ok = CommitDraftToWorkspace();
            if (!ok)
            {
                MarkDraftDirty();
                StatusMessage = "Kein Route-Paket geladen.";
                return;
            }

            MarkDraftSaved();
            RefreshIntegrityWarning();
            StatusMessage = IntegrityWarning is null
                ? $"Fahrweg lokal gespeichert (routePathDrafts[\"{_draft!.RouteName}\"]) – für Fahrzeuge über Dropbox senden · {RoutePathDraftIntegrity.FormatLengthSummary(_draft)}."
                : $"Fahrweg gespeichert – bitte prüfen: {IntegrityWarning}";
        }
        catch (Exception ex)
        {
            MarkDraftDirty();
            StatusMessage = $"Speichern fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool HasPendingDraftChanges =>
        SaveButtonState == RoutePathSaveButtonState.Unsaved;

    public void CommitDraftIfDirty()
    {
        if (HasPendingDraftChanges)
        {
            CommitDraftToWorkspace();
        }
    }

    public bool CommitDraftToWorkspace()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null || _draft is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_draft.RouteName))
        {
            if (string.IsNullOrWhiteSpace(SelectedRoute))
            {
                return false;
            }

            _draft.RouteName = SelectedRoute;
        }

        // 1) Draft in PackageRoot
        RoutePathDraftRepository.SaveToPackage(editor.PackageRoot, _draft);
        // 2) ToJson/SyncToRoot (kann Keys normalisieren)
        AppServices.Routes.ApplyEditorChanges("navidaten", rebuildEmbeddedMedia: false);
        // 3) Erneut setzen – SyncToRoot darf den frischen Draft nicht verlieren
        RoutePathDraftRepository.SaveToPackage(editor.PackageRoot, _draft);
        AppServices.Routes.PersistPackageBodyOnly("navidaten-draft");
        return true;
    }

    private void RefreshNavManeuverList(string? selectMarkerKey = null)
    {
        var previousKey = selectMarkerKey ?? SelectedNavManeuverItem?.MapMarkerKey ?? _selectedNavMarkerKey;
        List<RoutePathNavManeuverListItem> items;
        lock (_navListLock)
        {
            if (_draft is null)
            {
                items = [];
            }
            else
            {
                RoutePathDraftMutator.DeduplicateSegmentsByEdge(_draft);
                RoutePathDraftMutator.DeduplicateManeuversPerEdge(_draft);
                RoutePathDraftMutator.EnsureBusStraightEdgeKeys(_draft);

                var nodeTitles = _draft.Nodes.ToDictionary(n => n.Id, n => n.Title ?? n.Id, StringComparer.Ordinal);
                items = new List<RoutePathNavManeuverListItem>();
                var seenMarkerKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in NavManeuverDisplayHelper.EnumerateVisibleMapManeuvers(_draft))
                {
                    if (!seenMarkerKeys.Add(entry.MapMarkerKey))
                    {
                        continue;
                    }

                    var fromTitle = nodeTitles.GetValueOrDefault(entry.Segment.FromNodeId, entry.Segment.FromNodeId);
                    var toTitle = nodeTitles.GetValueOrDefault(entry.Segment.ToNodeId, entry.Segment.ToNodeId);
                    var segmentLabel = $"{fromTitle} → {toTitle}";
                    var displayInstruction = NavManeuverHelper.GetDisplayInstruction(
                        entry.Maneuver,
                        entry.SymbolType);
                    items.Add(new RoutePathNavManeuverListItem(
                        entry.DisplayNumber,
                        entry.Segment.FromNodeId,
                        entry.Segment.ToNodeId,
                        entry.ManeuverIndex,
                        entry.SymbolType,
                        displayInstruction,
                        (int)Math.Round(entry.Maneuver.DistanceM),
                        segmentLabel,
                        NavSymbolImageHelper.GetImageUri(entry.SymbolType),
                        entry.MapMarkerKey));
                }
            }

            NavManeuverItems = new ObservableCollection<RoutePathNavManeuverListItem>(items);
        }

        OnPropertyChanged(nameof(HasNavManeuvers));

        if (!string.IsNullOrEmpty(previousKey))
        {
            SelectNavManeuverInListByMarkerKey(previousKey);
        }
    }

    private void SelectNavManeuverInList(string from, string to, int maneuverIndex)
    {
        var key = $"{RoutePathDraft.SegmentEdgeKey(from, to)}_m{maneuverIndex}";
        SelectNavManeuverInListByMarkerKey(key);
    }

    private void SelectNavManeuverInListByMarkerKey(string mapMarkerKey)
    {
        _suppressNavManeuverSelectionSync = true;
        try
        {
            SelectedNavManeuverItem = NavManeuverItems.FirstOrDefault(x => x.MapMarkerKey == mapMarkerKey);
            if (SelectedNavManeuverItem is null &&
                !string.IsNullOrEmpty(_selectedSegmentFrom) &&
                !string.IsNullOrEmpty(_selectedSegmentTo))
            {
                // Index nach Dedup verschoben: erstes Manöver derselben Kante wählen.
                SelectedNavManeuverItem = NavManeuverItems.FirstOrDefault(x =>
                    string.Equals(x.FromNodeId, _selectedSegmentFrom, StringComparison.Ordinal) &&
                    string.Equals(x.ToNodeId, _selectedSegmentTo, StringComparison.Ordinal) &&
                    x.ManeuverIndex == _selectedManeuverIndex)
                    ?? NavManeuverItems.FirstOrDefault(x =>
                        string.Equals(x.FromNodeId, _selectedSegmentFrom, StringComparison.Ordinal) &&
                        string.Equals(x.ToNodeId, _selectedSegmentTo, StringComparison.Ordinal));
            }

            if (SelectedNavManeuverItem is not null)
            {
                _selectedNavMarkerKey = SelectedNavManeuverItem.MapMarkerKey;
                _selectedSegmentFrom = SelectedNavManeuverItem.FromNodeId;
                _selectedSegmentTo = SelectedNavManeuverItem.ToNodeId;
                _selectedManeuverIndex = SelectedNavManeuverItem.ManeuverIndex;
            }
        }
        finally
        {
            _suppressNavManeuverSelectionSync = false;
        }
    }

    private void PushDraftToMap(
        IList<RouteStopItem>? stopsForBounds = null,
        bool resetMapView = false,
        bool skipNavListRefresh = false,
        bool restoreMapView = false)
    {
        if (_draft is null) return;
        try
        {
            _draftGeneration++;
            _lastAppliedMapEditGeneration = 0;
            _awaitingMapLoadAfterPlannerPush = true;
            var node = RoutePathDraftSerializer.ToJsonNode(_draft);
            node["draftGeneration"] = _draftGeneration;
            node["mapEditGeneration"] = 0;
            node["navSymbolLabels"] = NavSymbolCatalog.BuildNavSymbolLabelsJson();
            if (_activeEditSegmentOrder is int editOrder)
            {
                node["activeEditSegmentOrder"] = editOrder;
            }
            else
            {
                node.Remove("activeEditSegmentOrder");
            }

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

            if (!string.IsNullOrEmpty(_selectedNavMarkerKey))
            {
                node["selectedNavMarkerKey"] = _selectedNavMarkerKey;
            }
            else
            {
                node.Remove("selectedNavMarkerKey");
            }

            var json = node.ToJsonString();
            var bounds = resetMapView
                ? BuildBoundsJson(stopsForBounds ?? CollectStopsForSelectedRoute())
                : null;
            PushDraftToMapRequested?.Invoke(json, bounds, resetMapView, restoreMapView);
            if (!resetMapView && !skipNavListRefresh)
            {
                RefreshNavManeuverList(_selectedNavMarkerKey);
            }
        }
        catch (Exception ex)
        {
            _awaitingMapLoadAfterPlannerPush = false;
            _applyingNavSymbol = false;
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
