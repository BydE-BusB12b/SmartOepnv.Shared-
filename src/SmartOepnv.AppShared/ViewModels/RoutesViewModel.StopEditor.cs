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
    public ObservableCollection<string> Ds003aDestinations { get; } = [];
    public ObservableCollection<string> LineCourseTripRoutes { get; } = [];

    public bool HasSelectedStop => SelectedStop is not null;

    public bool IsStartStop
    {
        get => SelectedStop is not null && !SelectedStop.IsAnnouncementEnabled;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.IsAnnouncementEnabled = !value;
            if (value &&
                string.IsNullOrWhiteSpace(SelectedStop.Destination) &&
                string.IsNullOrWhiteSpace(SelectedStop.Ds003aDestination) &&
                string.IsNullOrWhiteSpace(SelectedStop.LineNumber))
            {
                SelectedStop.Destination = "Starthaltestelle";
            }

            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

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

    [RelayCommand]
    private void StopDetailEdited() => MarkStopDetailDirty();

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
        RefreshStopEditorCatalogs();
        EnsureCatalogContainsStopSelections(stop);
    }

    private void EnsureCatalogContainsStopSelections(RouteStopItem stop)
    {
        EnsureComboValue(Ds021tDestinations, ToComboLabel(stop.Destination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds003aDestinations, ToComboLabel(stop.Ds003aDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds021tDestinations, ToComboLabel(stop.EndDestination, RouteStopEditorCatalog.NoDestinationLabel));
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
        Ds003aDestinations.Clear();
        LineCourseTripRoutes.Clear();

        Ds021tDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
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
        OnPropertyChanged(nameof(IsEndStop));
        OnPropertyChanged(nameof(PlayEndStopAnnouncement));
        OnPropertyChanged(nameof(RouteChangeEnabled));
        OnPropertyChanged(nameof(ShowStartStopFields));
        OnPropertyChanged(nameof(ShowEndStopFields));
        OnPropertyChanged(nameof(ShowRouteChangeFields));
        OnPropertyChanged(nameof(SelectedDestinationDs021t));
        OnPropertyChanged(nameof(SelectedDestinationDs003a));
        OnPropertyChanged(nameof(SelectedEndDestinationDs021t));
        OnPropertyChanged(nameof(SelectedEndDestinationDs003a));
        OnPropertyChanged(nameof(SelectedLineCourseTrip));
    }

    private void MarkStopDetailDirty()
    {
        _sync.MarkDirty();
        StatusMessage = "Haltestellen-Änderungen – bitte „Speichern“.";
    }

    private static string? ToComboLabel(string? value, string emptyLabel) =>
        string.IsNullOrWhiteSpace(value) ? emptyLabel : value.Trim();

    private static string FromComboLabel(string? value, string emptyLabel) =>
        string.Equals(value?.Trim(), emptyLabel, StringComparison.Ordinal) ? string.Empty : value?.Trim() ?? string.Empty;
}
