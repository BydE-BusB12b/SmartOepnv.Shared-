using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Vrr;

namespace SmartOepnv.AppShared.ViewModels;

public partial class RoutesViewModel
{
    public ObservableCollection<string> Ds021tDestinations { get; } = [];
    public ObservableCollection<string> Ds021NeuDestinations { get; } = [];
    public ObservableCollection<string> FmaS1Destinations { get; } = [];
    public ObservableCollection<string> Ds003aDestinations { get; } = [];
    public ObservableCollection<string> ZielnummerDestinations { get; } = [];
    public ObservableCollection<string> LineCourseTripRoutes { get; } = [];

    private bool _startStopCheckbox;

    public bool HasSelectedStop => SelectedStop is not null;

    public bool IsStartStop
    {
        get => _startStopCheckbox;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            // Unveränderter Wert: nichts tun. Sonst setzt Speichern-Flush der unchecked
            // „Starthaltestelle“-Checkbox IsAnnouncementEnabled immer auf true und
            // überschreibt „Ansage ausblenden“.
            if (_startStopCheckbox == value)
            {
                return;
            }

            _startStopCheckbox = value;
            if (value)
            {
                SelectedStop.IsAnnouncementEnabled = false;
                RouteStopEditorCatalog.EnsureStartStopMarker(SelectedStop);
            }
            else
            {
                RouteStopEditorCatalog.ClearStartStopFields(SelectedStop);
                SelectedStop.IsAnnouncementEnabled = true;
            }

            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool IsAnnouncementHidden
    {
        get => SelectedStop is not null && !IsStartStop && !SelectedStop.IsAnnouncementEnabled;
        set
        {
            if (SelectedStop is null || IsStartStop)
            {
                return;
            }

            var enabled = !value;
            if (SelectedStop.IsAnnouncementEnabled == enabled)
            {
                return;
            }

            SelectedStop.IsAnnouncementEnabled = enabled;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool ShowAnnouncementHiddenOption => HasSelectedStop && !IsStartStop;

    public bool IsEndStop
    {
        get => SelectedStop?.IsEndStop ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.IsEndStop = value;

            if (value && SelectedStop.Radius <= 0)
            {
                SelectedStop.Radius = 15;
            }

            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool PlayEndStopAnnouncement
    {
        get => SelectedStop?.PlayEndStopAnnouncement ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.PlayEndStopAnnouncement = value;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool RouteChangeEnabled
    {
        get => SelectedStop?.RouteChangeEnabled ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.RouteChangeEnabled = value;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool ShowStartStopFields => HasSelectedStop && IsStartStop;
    /** Endziel-Felder: bei Endhaltestelle oder Endhaltestellen-Ansage (ohne Routenwechsel). */
    public bool ShowEndDestinationFields =>
        HasSelectedStop && (IsEndStop || PlayEndStopAnnouncement);
    /** @deprecated Alias – Prefer [ShowEndDestinationFields]. */
    public bool ShowEndStopFields => ShowEndDestinationFields;
    /** Endhaltestellen-Ansage-Checkbox unabhängig von Endhaltestelle. */
    public bool ShowEndStopAnnouncementFields => HasSelectedStop;
    /** Automatischer Routenwechsel nur bei echter Endhaltestelle. */
    public bool ShowRouteChangeOption => HasSelectedStop && IsEndStop;
    public bool ShowRouteChangeFields => HasSelectedStop && IsEndStop && RouteChangeEnabled;

    [ObservableProperty]
    private string selectedStopVrrStopId = string.Empty;

    private bool _syncingStopVrrStopId;

    public string? SelectedDestinationDs021t
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds021T,
            SelectedStop?.DestinationId,
            SelectedStop?.Destination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds021T, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationDs021Neu
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds021Neu,
            SelectedStop?.Ds021NeuDestinationId,
            SelectedStop?.Ds021NeuDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds021Neu, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationFmaS1
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.FmaS1,
            SelectedStop?.FmaS1DestinationId,
            SelectedStop?.FmaS1Destination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.FmaS1, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationDs003a
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds003aKrefeld,
            SelectedStop?.Ds003aDestinationId,
            SelectedStop?.Ds003aDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds003aKrefeld, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationZielnummer
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Zielnummer,
            SelectedStop?.ZielnummerDestinationId,
            SelectedStop?.ZielnummerDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Zielnummer, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs021t
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds021T,
            SelectedStop?.EndDestinationId,
            SelectedStop?.EndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds021T, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs021Neu
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds021Neu,
            SelectedStop?.Ds021NeuEndDestinationId,
            SelectedStop?.Ds021NeuEndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds021Neu, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationFmaS1
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.FmaS1,
            SelectedStop?.FmaS1EndDestinationId,
            SelectedStop?.FmaS1EndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.FmaS1, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs003a
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds003aKrefeld,
            SelectedStop?.Ds003aEndDestinationId,
            SelectedStop?.Ds003aEndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds003aKrefeld, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationZielnummer
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Zielnummer,
            SelectedStop?.ZielnummerEndDestinationId,
            SelectedStop?.ZielnummerEndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Zielnummer, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    private IReadOnlyList<OutsideDisplayDestinationResolver.CatalogEntry> DestinationCatalog =>
        OutsideDisplayDestinationResolver.BuildCatalog(
            AppServices.Routes.Editor?.OutsideDisplays ?? Array.Empty<string>());

    private string? ResolveComboLabel(
        OutsideDisplayProtocolKind protocol,
        string? destinationId,
        string? destinationName)
    {
        var resolved = OutsideDisplayDestinationResolver.ResolveDisplayName(
            DestinationCatalog,
            protocol,
            destinationId,
            destinationName);
        return ToComboLabel(resolved, RouteStopEditorCatalog.NoDestinationLabel);
    }

    private void ApplyDestinationSelection(
        OutsideDisplayProtocolKind protocol,
        bool isEnd,
        string? comboLabel)
    {
        if (SelectedStop is null)
        {
            return;
        }

        OutsideDisplayDestinationResolver.ApplySelection(
            SelectedStop,
            protocol,
            isEnd,
            comboLabel,
            DestinationCatalog);
    }

    public string? SelectedLineCourseTrip
    {
        get => ToComboLabel(SelectedStop?.SelectedLineCourseTrip, RouteStopEditorCatalog.NoLineCourseTripLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.SelectedLineCourseTrip = FromComboLabel(value, RouteStopEditorCatalog.NoLineCourseTripLabel);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    [ObservableProperty] private string lineCourseTripQuickEntry = string.Empty;

    partial void OnLineCourseTripQuickEntryChanged(string value) =>
        ApplyLineCourseTripByNumberCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanApplyLineCourseTripByNumber))]
    private void ApplyLineCourseTripByNumber()
    {
        if (SelectedStop is null)
        {
            return;
        }

        var routes = LineCourseTripRoutes
            .Where(route => !string.Equals(route, RouteStopEditorCatalog.NoLineCourseTripLabel, StringComparison.Ordinal));
        if (!RouteStopEditorCatalog.TryResolveLineCourseTripByTripNumber(
                routes,
                LineCourseTripQuickEntry,
                SelectedRoute,
                out var matchedRoute,
                out var error))
        {
            StatusMessage = error ?? "Fahrt konnte nicht übernommen werden.";
            return;
        }

        SelectedLineCourseTrip = matchedRoute;
        LineCourseTripQuickEntry = string.Empty;
        StatusMessage = $"Routenwechsel-Fahrt übernommen: {matchedRoute}";
    }

    private bool CanApplyLineCourseTripByNumber() =>
        ShowRouteChangeFields && !string.IsNullOrWhiteSpace(LineCourseTripQuickEntry);

    partial void OnSelectedStopVrrStopIdChanged(string value)
    {
        if (_syncingStopVrrStopId || SelectedStop is null)
        {
            return;
        }

        var trimmed = value?.Trim() ?? string.Empty;
        if (string.Equals(SelectedStop.VrrStopId, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        SelectedStop.VrrStopId = trimmed;
        MarkStopDetailDirty();
    }

    [RelayCommand]
    private void StopDetailEdited() => MarkStopDetailDirty();

    [RelayCommand]
    private void PickEndDestinationCoordinatesOnMap()
    {
        if (SelectedStop is null)
        {
            return;
        }

        try
        {
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var initial = string.IsNullOrWhiteSpace(SelectedStop.EndDestinationCoordinates)
                ? SelectedStop.GpsCoordinates
                : SelectedStop.EndDestinationCoordinates;
            var dialog = new GpsMapPickerDialog(
                "Endziel-GPS",
                initial,
                SelectedStop.GpsCoordinates,
                "Haltestelle",
                radiusMeters: SelectedStop.Radius > 0 ? SelectedStop.Radius : 50)
            {
                Owner = owner
            };
            if (dialog.ShowDialog() != true || !dialog.HasSelection)
            {
                return;
            }

            SelectedStop.EndDestinationCoordinates = dialog.SelectedCoordinates;
            OnPropertyChanged(nameof(SelectedStop));
            MarkStopDetailDirty();
            StatusMessage = "Endziel-GPS auf der Karte gesetzt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karte: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PickVrrStop()
    {
        if (SelectedStop is null)
        {
            return;
        }

        try
        {
            var prefill = VrrStopAssignmentManager.PrefillQuery(
                SelectedStop.Name,
                SelectedStop.VrrStopId);
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var dialog = new VrrStopFinderDialog(prefill) { Owner = owner };
            if (dialog.ShowDialog() != true || dialog.SelectedEntry is null)
            {
                return;
            }

            var assignment = VrrStopAssignmentManager.FromCatalogEntry(dialog.SelectedEntry);
            VrrStopAssignmentManager.ApplyToRouteStop(SelectedStop, assignment);
            SyncSelectedStopVrrStopIdFromStop();
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
            StatusMessage = $"VRR-ID „{SelectedStop.VrrStopId}“ übernommen ({assignment.DisplayName}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"VRR-Suche fehlgeschlagen: {ex.Message}";
        }
    }

    public void OnStopGridEdited()
    {
        NotifyStopEditorStateChanged();
        MarkStopDetailDirty();
    }

    /// <summary>Kataloge und ComboBox-Auswahl vor dem Bearbeitungsdialog vorbereiten (verhindert Absturz bei fehlenden Listeneinträgen).</summary>
    public void PrepareStopEditDialog(RouteStopItem stop)
    {
        SelectedStop = stop;
        _startStopCheckbox = RouteStopEditorCatalog.IsStartStop(stop);
        if (_startStopCheckbox)
        {
            RouteStopEditorCatalog.EnsureStartStopMarker(stop);
        }

        RefreshStopEditorCatalogs();
        EnsureCatalogContainsStopSelections(stop);
        SyncSelectedStopVrrStopIdFromStop();
    }

    private void SyncSelectedStopVrrStopIdFromStop()
    {
        _syncingStopVrrStopId = true;
        SelectedStopVrrStopId = SelectedStop?.VrrStopId ?? string.Empty;
        _syncingStopVrrStopId = false;
    }

    private void EnsureCatalogContainsStopSelections(RouteStopItem stop)
    {
        EnsureComboValue(Ds021tDestinations, ToComboLabel(stop.Destination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds021NeuDestinations, ToComboLabel(stop.Ds021NeuDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(FmaS1Destinations, ToComboLabel(stop.FmaS1Destination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds003aDestinations, ToComboLabel(stop.Ds003aDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(ZielnummerDestinations, ToComboLabel(stop.ZielnummerDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds021tDestinations, ToComboLabel(stop.EndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds021NeuDestinations, ToComboLabel(stop.Ds021NeuEndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(FmaS1Destinations, ToComboLabel(stop.FmaS1EndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds003aDestinations, ToComboLabel(stop.Ds003aEndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(ZielnummerDestinations, ToComboLabel(stop.ZielnummerEndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(
            LineCourseTripRoutes,
            ToComboLabel(stop.SelectedLineCourseTrip, RouteStopEditorCatalog.NoLineCourseTripLabel));
    }

    private static void EnsureComboValue(ObservableCollection<string> items, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || items.Contains(value))
        {
            return;
        }

        items.Add(value);
    }

    public void RefreshStopEditorCatalogs()
    {
        Ds021tDestinations.Clear();
        Ds021NeuDestinations.Clear();
        FmaS1Destinations.Clear();
        Ds003aDestinations.Clear();
        ZielnummerDestinations.Clear();
        LineCourseTripRoutes.Clear();

        Ds021tDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        Ds021NeuDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        FmaS1Destinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        Ds003aDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        ZielnummerDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        LineCourseTripRoutes.Add(RouteStopEditorCatalog.NoLineCourseTripLabel);

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        foreach (var name in RouteStopEditorCatalog.LoadDs021tNames(editor))
        {
            Ds021tDestinations.Add(name);
        }

        foreach (var name in RouteStopEditorCatalog.LoadDs021NeuNames(editor))
        {
            Ds021NeuDestinations.Add(name);
        }

        foreach (var name in RouteStopEditorCatalog.LoadFmaS1Names(editor))
        {
            FmaS1Destinations.Add(name);
        }

        foreach (var name in RouteStopEditorCatalog.LoadDs003aNames(editor))
        {
            Ds003aDestinations.Add(name);
        }

        foreach (var name in RouteStopEditorCatalog.LoadZielnummerNames(editor))
        {
            ZielnummerDestinations.Add(name);
        }

        foreach (var route in RouteStopEditorCatalog.LoadLineCourseTripRoutes(editor))
        {
            LineCourseTripRoutes.Add(route);
        }
    }

    public void NotifyStopEditorStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedStop));
        OnPropertyChanged(nameof(IsStartStop));
        OnPropertyChanged(nameof(IsAnnouncementHidden));
        OnPropertyChanged(nameof(ShowAnnouncementHiddenOption));
        OnPropertyChanged(nameof(IsEndStop));
        OnPropertyChanged(nameof(PlayEndStopAnnouncement));
        OnPropertyChanged(nameof(RouteChangeEnabled));
        OnPropertyChanged(nameof(ShowStartStopFields));
        OnPropertyChanged(nameof(ShowEndDestinationFields));
        OnPropertyChanged(nameof(ShowEndStopFields));
        OnPropertyChanged(nameof(ShowEndStopAnnouncementFields));
        OnPropertyChanged(nameof(ShowRouteChangeOption));
        OnPropertyChanged(nameof(ShowRouteChangeFields));
        OnPropertyChanged(nameof(SelectedDestinationDs021t));
        OnPropertyChanged(nameof(SelectedDestinationDs021Neu));
        OnPropertyChanged(nameof(SelectedDestinationFmaS1));
        OnPropertyChanged(nameof(SelectedDestinationDs003a));
        OnPropertyChanged(nameof(SelectedDestinationZielnummer));
        OnPropertyChanged(nameof(SelectedEndDestinationDs021t));
        OnPropertyChanged(nameof(SelectedEndDestinationDs021Neu));
        OnPropertyChanged(nameof(SelectedEndDestinationFmaS1));
        OnPropertyChanged(nameof(SelectedEndDestinationDs003a));
        OnPropertyChanged(nameof(SelectedEndDestinationZielnummer));
        OnPropertyChanged(nameof(SelectedLineCourseTrip));
        ApplyLineCourseTripByNumberCommand.NotifyCanExecuteChanged();
        RouteChangeDisplayTick++;
    }

    private void MarkStopDetailDirty()
    {
        CancelSaveButtonSuccessFeedback();
        MaintainStartStopMarkerIfNeeded();
        _sync.MarkDirty();
        if (!RefreshStopTimeOrderWarnings(showDialog: false))
        {
            StatusMessage = "Haltestellen-Änderungen – bitte „Speichern“.";
        }
    }

    private void MaintainStartStopMarkerIfNeeded()
    {
        if (!_startStopCheckbox || SelectedStop is null)
        {
            return;
        }

        // Manuelle Liniennummer nicht mehr nutzen (Fehleingaben wie „000“ überschrieben DS001 am Ziel)
        if (RouteStopEditorCatalog.HasStartStopDestination(SelectedStop.Destination) ||
            RouteStopEditorCatalog.HasStartStopDestination(SelectedStop.Ds021NeuDestination) ||
            RouteStopEditorCatalog.HasStartStopDestination(SelectedStop.FmaS1Destination) ||
            !string.IsNullOrWhiteSpace(SelectedStop.Ds003aDestination) ||
            !string.IsNullOrWhiteSpace(SelectedStop.ZielnummerDestination))
        {
            SelectedStop.LineNumber = string.Empty;
        }

        RouteStopEditorCatalog.EnsureStartStopMarker(SelectedStop);
    }

    public void MaintainStartStopMarkerAfterEdit() => MaintainStartStopMarkerIfNeeded();

    public void ApplyDestinationComboSelection(string fieldKey, string? comboLabel)
    {
        if (SelectedStop is null)
        {
            return;
        }

        switch (fieldKey)
        {
            case "startDs021t":
                SelectedDestinationDs021t = comboLabel;
                break;
            case "startDs021Neu":
                SelectedDestinationDs021Neu = comboLabel;
                break;
            case "startFmaS1":
                SelectedDestinationFmaS1 = comboLabel;
                break;
            case "startDs003a":
                SelectedDestinationDs003a = comboLabel;
                break;
            case "startZielnummer":
                SelectedDestinationZielnummer = comboLabel;
                break;
            case "endDs021t":
                SelectedEndDestinationDs021t = comboLabel;
                break;
            case "endDs021Neu":
                SelectedEndDestinationDs021Neu = comboLabel;
                break;
            case "endFmaS1":
                SelectedEndDestinationFmaS1 = comboLabel;
                break;
            case "endDs003a":
                SelectedEndDestinationDs003a = comboLabel;
                break;
            case "endZielnummer":
                SelectedEndDestinationZielnummer = comboLabel;
                break;
            case "lineCourseTrip":
                SelectedLineCourseTrip = comboLabel;
                break;
            default:
                return;
        }
    }

    private static string? ToComboLabel(string? value, string emptyLabel) =>
        RouteStopEditorCatalog.ToComboLabel(value, emptyLabel);

    private static string FromComboLabel(string? value, string emptyLabel) =>
        RouteStopEditorCatalog.FromComboLabel(value, emptyLabel);
}
