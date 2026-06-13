using System.Windows;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class DropboxSetupWindow : Window
{
    public DropboxSetupWindow()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.PersistFolderPath();
        }

        Close();
    }
}
