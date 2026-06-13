using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class EmployeesView : UserControl
{
    public EmployeesView()
    {
        InitializeComponent();
    }

    private void DocumentCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is EmployeesViewModel viewModel)
        {
            viewModel.NotifyDocumentCheckChanged();
        }
    }
}
