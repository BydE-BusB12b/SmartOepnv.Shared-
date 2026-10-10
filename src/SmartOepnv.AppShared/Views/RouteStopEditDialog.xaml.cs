using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public partial class RouteStopEditDialog : Window
{
    private readonly RoutesViewModel _viewModel;
    private readonly RouteStopItem _stop;
    private readonly RouteStopItem _snapshot;
    private readonly bool _commitPackageOnSave;

    /// <param name="commitPackageOnSave">
    /// false = nur Haltestelle im Speicher übernehmen (z. B. Umlauf-Import-Entwurf), kein Paket speichern.
    /// </param>
    public RouteStopEditDialog(RoutesViewModel viewModel, RouteStopItem stop, bool commitPackageOnSave = true)
    {
        _viewModel = viewModel;
        _stop = stop;
        _snapshot = stop.Clone();
        _commitPackageOnSave = commitPackageOnSave;

        viewModel.PrepareStopEditDialog(stop);

        InitializeComponent();
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        var displayName = string.IsNullOrWhiteSpace(stop.Name) ? "Haltestelle" : stop.Name.Trim();
        TitleText.Text = displayName;
        Title = $"Haltestelle bearbeiten – {displayName}";

        EditPanel.DataContext = viewModel;
        EditPanel.SyncComboSelectionsFromStop(_stop);

        EditorScroll.SizeChanged += (_, _) => SyncEditPanelWidth();
        Loaded += (_, _) =>
        {
            FitToWorkingArea();
            SyncEditPanelWidth();
        };
        ContentRendered += (_, _) => SyncEditPanelWidth();
        Closed += (_, _) => DetachEditorBindings();
    }

    /// <summary>Fenster an sichtbaren Arbeitsbereich anpassen, damit Speichern/Abbrechen erreichbar bleiben.</summary>
    private void FitToWorkingArea()
    {
        var work = SystemParameters.WorkArea;
        var maxH = Math.Max(420, work.Height - 48);
        var maxW = Math.Max(720, work.Width - 48);
        MaxHeight = maxH;
        MaxWidth = maxW;
        if (Height > maxH)
        {
            Height = maxH;
        }

        if (Width > maxW)
        {
            Width = maxW;
        }

        // Zentrieren, falls Owner/Bildschirm die Unterkante abschneiden würde
        if (Owner is not null)
        {
            var left = Owner.Left + (Owner.ActualWidth - ActualWidth) / 2;
            var top = Owner.Top + (Owner.ActualHeight - ActualHeight) / 2;
            Left = Math.Max(work.Left, Math.Min(left, work.Right - ActualWidth));
            Top = Math.Max(work.Top, Math.Min(top, work.Bottom - ActualHeight));
        }
        else
        {
            Left = work.Left + (work.Width - ActualWidth) / 2;
            Top = work.Top + (work.Height - ActualHeight) / 2;
        }
    }

    private void DetachEditorBindings()
    {
        EditPanel.DataContext = null;
        _viewModel.SelectedStop = null;
    }

    private void SyncEditPanelWidth()
    {
        var width = EditorScroll.ViewportWidth;
        if (width > 0)
        {
            EditPanel.Width = width;
            EditPanel.MinWidth = width;
            // Kein SyncComboSelectionsFromStop hier: SizeChanged (Dropdown öffnen/schließen)
            // würde sonst die gerade gewählte Zielauswahl zurücksetzen.
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Alle Textfelder inkl. Zeit/Radius sofort übernehmen (auch ohne vorheriges LostFocus).
        FlushPendingFieldBindings(EditPanel, includeComboBoxes: false);
        if (!string.IsNullOrWhiteSpace(_stop.Time))
        {
            _stop.Time = RouteScheduleTimeCalculator.NormalizeTimeInput(_stop.Time);
        }

        EditPanel.ApplyComboSelectionsToStop(_stop);
        // Startziele/Zielwechsel-Ziele behalten; sonst leere Felder (kein Ansage-aus-Marker).
        if (_viewModel.IsStartStop)
        {
            _viewModel.MaintainStartStopMarkerAfterEdit();
        }
        else if (!_viewModel.ZielwechselEnabled)
        {
            RouteStopEditorCatalog.ClearStartStopFields(_stop);
        }
        else
        {
            // Zielwechsel: Ansage muss an bleiben (nicht wie Starthaltestelle).
            _stop.IsAnnouncementEnabled = true;
        }

        if (_commitPackageOnSave)
        {
            _viewModel.StopDetailEditedCommand.Execute(null);
            _viewModel.SaveChangesCommand.Execute(null);
        }

        // RefreshStopAfterEdit macht RoutesView nach ShowDialog – hier nicht doppelt.
        EditPanel.DataContext = null;
        DialogResult = true;
        Close();
    }

    private static void FlushPendingFieldBindings(DependencyObject root, bool includeComboBoxes = true)
    {
        foreach (var textBox in EnumerateVisualChildren<TextBox>(root))
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        foreach (var comboBox in EnumerateVisualChildren<ComboBox>(root))
        {
            if (!includeComboBoxes)
            {
                continue;
            }

            comboBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
        }

        foreach (var checkBox in EnumerateVisualChildren<CheckBox>(root))
        {
            checkBox.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
        }
    }

    private static IEnumerable<T> EnumerateVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in EnumerateVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _stop.CopyFrom(_snapshot);
        EditPanel.DataContext = null;
        _viewModel.SelectedStop = null;
        DialogResult = false;
        Close();
    }
}
