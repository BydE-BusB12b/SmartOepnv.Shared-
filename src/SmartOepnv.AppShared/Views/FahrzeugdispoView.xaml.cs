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

        var clickX = e.GetPosition(element).X;
        var absoluteX = bar.Left + clickX;
        var dayIndex = Math.Max(0, (int)(absoluteX / FahrzeugdispoViewModel.DayCellWidth));
        var targetDate = viewModel.ViewStartDate.Date.AddDays(dayIndex);
        viewModel.OpenHourViewFromAssignmentBar(bar.VehiclePhoneKey, targetDate);
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
