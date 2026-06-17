using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class RouteStopEditPanel : UserControl
{
    public RouteStopEditPanel()
    {
        InitializeComponent();
    }

    private void Field_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is RoutesViewModel vm)
        {
            vm.StopDetailEditedCommand.Execute(null);
        }
    }
}
