using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public partial class RoutesView : UserControl
{
    private bool _openingStopDialog;
    private bool _suppressChainTripPick;

    public RoutesView()
    {
        InitializeComponent();
    }

    private void ChainRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not string head || DataContext is not RoutesViewModel vm)
        {
            return;
        }

        var trips = vm.TripsInChain(head);
        _suppressChainTripPick = true;
        ChainTripList.ItemsSource = trips;
        ChainTripList.SelectedIndex = -1;
        _suppressChainTripPick = false;

        ChainTripPopup.PlacementTarget = button;
        ChainTripPopup.Width = Math.Max(240, button.ActualWidth);
        ChainTripPopup.IsOpen = true;
        vm.RouteListSelection = head;
        e.Handled = true;
    }

    private void ChainTripList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChainTripPick || ChainTripList.SelectedItem is not string trip || DataContext is not RoutesViewModel vm)
        {
            return;
        }

        ChainTripPopup.IsOpen = false;
        vm.SelectChainTrip(trip);
    }

    private void StopsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_openingStopDialog || DataContext is not RoutesViewModel vm)
        {
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var row = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (row?.Content is not RouteStopItem stop)
        {
            return;
        }

        if (!vm.Stops.Contains(stop))
        {
            return;
        }

        OpenStopEditDialog(vm, stop);
    }

    private void OpenStopEditDialog(RoutesViewModel vm, RouteStopItem stop)
    {
        if (_openingStopDialog)
        {
            return;
        }

        _openingStopDialog = true;
        Window? owner = null;
        try
        {
            owner = Window.GetWindow(this);
            var dialog = new RouteStopEditDialog(vm, stop) { Owner = owner };
            var saved = dialog.ShowDialog() == true;
            if (saved)
            {
                vm.RefreshStopAfterEdit(stop);
            }

            CollectionViewSource.GetDefaultView(vm.Stops)?.Refresh();
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Haltestelle konnte nicht geöffnet werden: {ex.Message}";
            MessageBox.Show(
                owner ?? Application.Current?.MainWindow,
                ex.Message,
                "Haltestelle bearbeiten",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _openingStopDialog = false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
