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

            SelectedStop.IsAnnouncementEnabled = !value;
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
            if (!value)
            {
                SelectedStop.PlayEndStopAnnouncement = false;
            }

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
            OnPropertyChanged();
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
    public bool ShowEndStopFields => HasSelectedStop && IsEndStop;
    public bool ShowRouteChangeFields => HasSelectedStop && IsEndStop && RouteChangeEnabled;

    [ObservableProperty]
    private string selectedStopVrrStopId = string.Empty;

    private bool _syncingStopVrrStopId;

    public string? SelectedDestinationDs021t
    {
        get => ToComboLabel(SelectedStop?.Destination, RouteStopEditorCatalog.NoDestinationLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.Destination = FromComboLabel(value, RouteStopEditorCatalog.NoDestinationLabel);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationDs021Neu
    {
        get => ToComboLabel(SelectedStop?.Ds021NeuDestination, RouteStopEditorCatalog.NoDestinationLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.Ds021NeuDestination = FromComboLabel(value, RouteStopEditorCatalog.NoDestinationLabel);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationFmaS1
    {
        get => ToComboLabel(SelectedStop?.FmaS1Destination, RouteStopEditorCatalog.NoDestinationLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.FmaS1Destination = FromComboLabel(value, RouteStopEditorCatalog.NoDestinationLabel);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationDs003a
    {
        get => ToComboLabel(SelectedStop?.Ds003aDestination, RouteStopEditorCatalog.NoDestinationLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.Ds003aDestination = FromComboLabel(value, RouteStopEditorCatalog.NoDestinationLabel);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs021t
    {
        get => ToComboLabel(SelectedStop?.EndDestination, RouteStopEditorCatalog.NoDestinationLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.EndDestination = FromComboLabel(value, RouteStopEditorCatalog.NoDestinationLabel);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs021Neu
    {
        get => ToComboLabel(SelectedStop?.Ds021NeuEndDestination, RouteStopEditorCatalog.NoDestinationLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.Ds021NeuEndDestination = FromComboLabel(value, RouteStopEditorCatalog.NoDestinationLabel);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationFmaS1
    {
        get => ToComboLabel(SelectedStop?.FmaS1EndDestination, RouteStopEditorCatalog.NoDestinationLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.FmaS1EndDestination = FromComboLabel(value, RouteStopEditorCatalog.NoDestinationLabel);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs003a
    {
        get => ToComboLabel(SelectedStop?.Ds003aEndDestination, RouteStopEditorCatalog.NoDestinationLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.Ds003aEndDestination = FromComboLabel(value, RouteStopEditorCatalog.NoDestinationLabel);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
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
                "Haltestelle")
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
        EnsureComboValue(Ds021tDestinations, ToComboLabel(stop.EndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds021NeuDestinations, ToComboLabel(stop.Ds021NeuEndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(FmaS1Destinations, ToComboLabel(stop.FmaS1EndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds003aDestinations, ToComboLabel(stop.Ds003aEndDestination, RouteStopEditorCatalog.NoDestinationLabel));
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
        LineCourseTripRoutes.Clear();

        Ds021tDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        Ds021NeuDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        FmaS1Destinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        Ds003aDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
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
        OnPropertyChanged(nameof(ShowEndStopFields));
        OnPropertyChanged(nameof(ShowRouteChangeFields));
        OnPropertyChanged(nameof(SelectedDestinationDs021t));
        OnPropertyChanged(nameof(SelectedDestinationDs021Neu));
        OnPropertyChanged(nameof(SelectedDestinationFmaS1));
        OnPropertyChanged(nameof(SelectedDestinationDs003a));
        OnPropertyChanged(nameof(SelectedEndDestinationDs021t));
        OnPropertyChanged(nameof(SelectedEndDestinationDs021Neu));
        OnPropertyChanged(nameof(SelectedEndDestinationFmaS1));
        OnPropertyChanged(nameof(SelectedEndDestinationDs003a));
        OnPropertyChanged(nameof(SelectedLineCourseTrip));
        ApplyLineCourseTripByNumberCommand.NotifyCanExecuteChanged();
    }

    private void MarkStopDetailDirty()
    {
        CancelSaveButtonSuccessFeedback();
        MaintainStartStopMarkerIfNeeded();
        _sync.MarkDirty();
        StatusMessage = "Haltestellen-Änderungen – bitte „Speichern“.";
    }

    private void MaintainStartStopMarkerIfNeeded()
    {
        if (!_startStopCheckbox || SelectedStop is null)
        {
            return;
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
