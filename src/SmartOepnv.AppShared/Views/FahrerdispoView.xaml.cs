using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class FahrerdispoView : UserControl
{
    public FahrerdispoView()
    {
        InitializeComponent();
    }

    private void DriverName_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not FahrzeugdispoVehicleRowVm row ||
            DataContext is not FahrerdispoViewModel viewModel)
        {
            return;
        }

        viewModel.OpenEmployeeManagement(row.PhoneKey, row.PersonnelNumber);
        e.Handled = true;
    }

    private void AssignmentBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not FahrzeugdispoAssignmentBarVm bar ||
            DataContext is not FahrerdispoViewModel viewModel ||
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
            DataContext is not FahrerdispoViewModel viewModel ||
            string.IsNullOrWhiteSpace(bar.AssignmentId))
        {
            return;
        }

        viewModel.EditShift(bar.AssignmentId);
        e.Handled = true;
    }
}
