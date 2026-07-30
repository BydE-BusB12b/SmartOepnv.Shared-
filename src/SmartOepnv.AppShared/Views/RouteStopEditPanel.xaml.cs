using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.RoutePackage;
namespace SmartOepnv.AppShared.Views;

public partial class RouteStopEditPanel : UserControl
{
    private bool _suppressDestinationComboSync;

    public RouteStopEditPanel()
    {
        InitializeComponent();
        foreach (var combo in EnumerateDestinationCombos())
        {
            combo.DropDownClosed += DestinationCombo_DropDownClosed;
        }

        Loaded += (_, _) => SyncComboSelectionsFromViewModel();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RoutesViewModel oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (e.NewValue is RoutesViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;
        }

        SyncComboSelectionsFromViewModel();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RoutesViewModel.SelectedLineCourseTrip)
            or nameof(RoutesViewModel.IsStartStop)
            or nameof(RoutesViewModel.ShowStartStopFields))
        {
            SyncComboSelectionsFromViewModel();
        }
    }

    public void ApplyComboSelectionsToStop(RouteStopItem? targetStop = null)
    {
        var stop = targetStop;
        if (stop is null && DataContext is RoutesViewModel vmPick)
        {
            stop = vmPick.SelectedStop;
        }

        if (stop is null)
        {
            return;
        }

        if (DataContext is RoutesViewModel viewModel)
        {
            // Zielwahl inkl. DestinationId (Anzeige liest ID bevorzugt).
            // Ohne Starthaltestelle: Start-Ziele nicht aus (sichtbar versteckten) Combos zurückschreiben.
            foreach (var combo in EnumerateDestinationCombos())
            {
                if (combo.Tag is not string fieldKey)
                {
                    continue;
                }

                if (!viewModel.IsStartStop && IsStartDestinationField(fieldKey))
                {
                    viewModel.ApplyDestinationComboSelection(
                        fieldKey,
                        RouteStopEditorCatalog.NoDestinationLabel);
                    continue;
                }

                viewModel.ApplyDestinationComboSelection(fieldKey, ResolveComboSelection(combo));
            }

            if (viewModel.IsStartStop)
            {
                viewModel.MaintainStartStopMarkerAfterEdit();
            }
            else
            {
                RouteStopEditorCatalog.ClearStartStopFields(stop);
            }

            viewModel.NotifyStopEditorStateChanged();
            return;
        }

        foreach (var combo in EnumerateDestinationCombos())
        {
            if (combo.Tag is not string fieldKey)
            {
                continue;
            }

            ApplyDestinationToStop(stop, fieldKey, ResolveComboSelection(combo));
        }
    }

    private static bool IsStartDestinationField(string fieldKey) =>
        fieldKey.StartsWith("start", StringComparison.Ordinal);

    public void SyncComboSelectionsFromStop(RouteStopItem stop)
    {
        _suppressDestinationComboSync = true;
        try
        {
            // Anzeige/Combo an aufgelösten Namen (ID bevorzugt) ausrichten – sonst weicht Combo vom Text ab
            if (DataContext is RoutesViewModel vm)
            {
                SetComboSelection(StartDs021tCombo, vm.SelectedDestinationDs021t);
                SetComboSelection(StartDs021NeuCombo, vm.SelectedDestinationDs021Neu);
                SetComboSelection(StartFmaS1Combo, vm.SelectedDestinationFmaS1);
                SetComboSelection(StartDs003aCombo, vm.SelectedDestinationDs003a);
                SetComboSelection(StartZielnummerCombo, vm.SelectedDestinationZielnummer);
                SetComboSelection(EndDs021tCombo, vm.SelectedEndDestinationDs021t);
                SetComboSelection(EndDs021NeuCombo, vm.SelectedEndDestinationDs021Neu);
                SetComboSelection(EndFmaS1Combo, vm.SelectedEndDestinationFmaS1);
                SetComboSelection(EndDs003aCombo, vm.SelectedEndDestinationDs003a);
                SetComboSelection(EndZielnummerCombo, vm.SelectedEndDestinationZielnummer);
                SetComboSelection(LineCourseTripCombo, vm.SelectedLineCourseTrip);
                return;
            }

            SetComboSelection(
                StartDs021tCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.Destination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                StartDs021NeuCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.Ds021NeuDestination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                StartFmaS1Combo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.FmaS1Destination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                StartDs003aCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.Ds003aDestination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                StartZielnummerCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.ZielnummerDestination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                EndDs021tCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.EndDestination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                EndDs021NeuCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.Ds021NeuEndDestination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                EndFmaS1Combo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.FmaS1EndDestination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                EndDs003aCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.Ds003aEndDestination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                EndZielnummerCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.ZielnummerEndDestination,
                    RouteStopEditorCatalog.NoDestinationLabel));
            SetComboSelection(
                LineCourseTripCombo,
                RouteStopEditorCatalog.ToComboLabel(
                    stop.SelectedLineCourseTrip,
                    RouteStopEditorCatalog.NoLineCourseTripLabel));
        }
        finally
        {
            _suppressDestinationComboSync = false;
        }
    }

    public void SyncComboSelectionsFromViewModel()
    {
        if (DataContext is not RoutesViewModel vm || vm.SelectedStop is null)
        {
            return;
        }

        SyncComboSelectionsFromStop(vm.SelectedStop);
    }

    private static void ApplyDestinationToStop(RouteStopItem stop, string fieldKey, string? comboLabel)
    {
        switch (fieldKey)
        {
            case "startDs021t":
                stop.Destination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                // Linie nur noch über Zielprogramm – manuelle lineNumber (z. B. „000“) nicht behalten
                stop.LineNumber = string.Empty;
                break;
            case "startDs021Neu":
                stop.Ds021NeuDestination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                stop.LineNumber = string.Empty;
                break;
            case "startFmaS1":
                stop.FmaS1Destination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                stop.LineNumber = string.Empty;
                break;
            case "startDs003a":
                stop.Ds003aDestination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                stop.LineNumber = string.Empty;
                break;
            case "startZielnummer":
                stop.ZielnummerDestination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                stop.LineNumber = string.Empty;
                break;
            case "endDs021t":
                stop.EndDestination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                break;
            case "endDs021Neu":
                stop.Ds021NeuEndDestination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                break;
            case "endFmaS1":
                stop.FmaS1EndDestination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                break;
            case "endDs003a":
                stop.Ds003aEndDestination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                break;
            case "endZielnummer":
                stop.ZielnummerEndDestination = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoDestinationLabel);
                break;
            case "lineCourseTrip":
                stop.SelectedLineCourseTrip = RouteStopEditorCatalog.FromComboLabel(
                    comboLabel,
                    RouteStopEditorCatalog.NoLineCourseTripLabel);
                break;
        }
    }

    private IEnumerable<ComboBox> EnumerateDestinationCombos()
    {
        yield return StartDs021tCombo;
        yield return StartDs003aCombo;
        yield return StartDs021NeuCombo;
        yield return StartFmaS1Combo;
        yield return StartZielnummerCombo;
        yield return EndDs021tCombo;
        yield return EndDs003aCombo;
        yield return EndDs021NeuCombo;
        yield return EndFmaS1Combo;
        yield return EndZielnummerCombo;
        yield return LineCourseTripCombo;
    }

    private static string? ResolveComboSelection(ComboBox combo)
    {
        var fromSelectedItem = ExtractComboItemText(combo.SelectedItem);
        if (!string.IsNullOrWhiteSpace(fromSelectedItem))
        {
            return fromSelectedItem;
        }

        if (combo.SelectedIndex >= 0 && combo.SelectedIndex < combo.Items.Count)
        {
            var fromIndex = ExtractComboItemText(combo.Items[combo.SelectedIndex]);
            if (!string.IsNullOrWhiteSpace(fromIndex))
            {
                return fromIndex;
            }
        }

        var fromValue = ExtractComboItemText(combo.SelectedValue);
        if (!string.IsNullOrWhiteSpace(fromValue))
        {
            return fromValue;
        }

        return string.IsNullOrWhiteSpace(combo.Text) ? null : combo.Text.Trim();
    }

    private static string? ExtractComboItemText(object? item) =>
        item switch
        {
            null => null,
            string text => text,
            ComboBoxItem { Content: string content } => content,
            ComboBoxItem { Content: var content } => content?.ToString(),
            _ => item.ToString()
        };

    private static void SetComboSelection(ComboBox combo, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            combo.SelectedIndex = -1;
            return;
        }

        foreach (var item in combo.Items)
        {
            if (item is string candidate &&
                string.Equals(candidate, label, StringComparison.Ordinal))
            {
                combo.SelectedItem = candidate;
                return;
            }
        }

        combo.SelectedItem = label;
    }

    private void Field_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is RoutesViewModel vm)
        {
            vm.StopDetailEditedCommand.Execute(null);
        }
    }

    private void StopTime_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var normalized = RouteScheduleTimeCalculator.NormalizeTimeInput(textBox.Text);
        if (!string.Equals(textBox.Text, normalized, StringComparison.Ordinal))
        {
            textBox.Text = normalized;
        }

        if (DataContext is RoutesViewModel vm && vm.SelectedStop is not null)
        {
            vm.SelectedStop.Time = normalized;
            vm.NotifyStopTimeEdited(vm.SelectedStop);
        }

        Field_LostFocus(sender, e);
    }

    private void DestinationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDestinationComboSync ||
            sender is not ComboBox combo ||
            combo.Tag is not string fieldKey)
        {
            return;
        }

        if (DataContext is RoutesViewModel vm && vm.SelectedStop is not null)
        {
            // Über ViewModel: setzt Name + DestinationId (sonst bleibt alte ID sichtbar)
            vm.ApplyDestinationComboSelection(fieldKey, ResolveComboSelection(combo));
            vm.MaintainStartStopMarkerAfterEdit();
            vm.NotifyStopEditorStateChanged();
            vm.StopDetailEditedCommand.Execute(null);
        }
    }

    private void DestinationCombo_DropDownClosed(object sender, EventArgs e)
    {
        if (_suppressDestinationComboSync ||
            sender is not ComboBox combo ||
            combo.Tag is not string fieldKey)
        {
            return;
        }

        if (DataContext is RoutesViewModel vm && vm.SelectedStop is not null)
        {
            vm.ApplyDestinationComboSelection(fieldKey, ResolveComboSelection(combo));
            vm.MaintainStartStopMarkerAfterEdit();
            vm.NotifyStopEditorStateChanged();
            vm.StopDetailEditedCommand.Execute(null);
        }
    }
}
