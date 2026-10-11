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

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            await vm.CommitFolderPathAsync().ConfigureAwait(true);
        }

        // Bei Betrieb-Wechsel startet der Prozess neu – Fenster ggf. schon weg.
        if (IsLoaded)
        {
            Close();
        }
    }
}
