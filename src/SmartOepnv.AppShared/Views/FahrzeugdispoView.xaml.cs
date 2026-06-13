using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class FahrzeugdispoView : UserControl
{
    public FahrzeugdispoView()
    {
        InitializeComponent();
    }

    private void AssignmentBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not FahrzeugdispoAssignmentBarVm bar ||
            DataContext is not FahrzeugdispoViewModel viewModel ||
            string.IsNullOrWhiteSpace(bar.VehiclePhoneKey))
        {
            return;
        }

        viewModel.OpenHourViewFromAssignmentBar(bar.VehiclePhoneKey, bar.FirstVisibleDate);
        e.Handled = true;
    }

    private void AssignmentBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not FahrzeugdispoAssignmentBarVm bar ||
            DataContext is not FahrzeugdispoViewModel viewModel ||
            string.IsNullOrWhiteSpace(bar.AssignmentId))
        {
            return;
        }

        viewModel.EditTrip(bar.AssignmentId);
        e.Handled = true;
    }
}
