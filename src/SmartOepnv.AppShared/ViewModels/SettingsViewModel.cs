using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.AppShared.Views;

namespace SmartOepnv.AppShared.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string folderPath = DropboxConstants.DefaultFolderPath;
    [ObservableProperty] private string connectionStatus = "Nicht verbunden";
    [ObservableProperty] private string accountInfo = "—";
    [ObservableProperty] private string testResult = string.Empty;
    [ObservableProperty] private string routeFileInfo = "—";
    [ObservableProperty] private string folderFilesSummary = "—";
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isBusy;

    public SettingsViewModel()
    {
        ReloadFromStore();
    }

    public void ReloadFromStore()
    {
        var s = AppServices.Dropbox.Settings;
        FolderPath = s.FolderPath;
        IsConnected = s.IsConnected;
        AccountInfo = s.IsConnected
            ? $"{s.ConnectedAccountName} ({s.ConnectedAccountEmail})"
            : "—";
        ConnectionStatus = s.IsConnected ? "Verbunden" : "Nicht verbunden";
    }

    [RelayCommand]
    private async Task ConnectDropboxAsync()
    {
        var owner = System.Windows.Application.Current.MainWindow;
        var dialog = new DropboxOAuthWindow { Owner = owner };
        var ok = dialog.ShowDialog();
        if (ok == true)
        {
            ReloadFromStore();
            ConnectionStatus = "Dropbox verbunden";
            await TestConnectionAsync();
        }
    }

    [RelayCommand]
    private void DisconnectDropbox()
    {
        AppServices.Dropbox.Disconnect();
        ReloadFromStore();
        TestResult = string.Empty;
        RouteFileInfo = "—";
        FolderFilesSummary = "—";
    }

    [RelayCommand]
    private void SaveFolderPath()
    {
        var s = AppServices.Dropbox.Settings;
        s.FolderPath = string.IsNullOrWhiteSpace(FolderPath)
            ? DropboxConstants.DefaultFolderPath
            : FolderPath.Trim();
        AppServices.Dropbox.SaveSettings(s);
        TestResult = "Ordnerpfad gespeichert.";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!IsConnected)
        {
            TestResult = "Bitte zuerst mit Dropbox verbinden.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await AppServices.Dropbox.TestConnectionAsync();
            TestResult = result.Message;
            AccountInfo = result.AccountDisplay;
            RouteFileInfo = result.RouteFileExists
                ? $"{DropboxConstants.RouteFileName} – {result.RouteFileSizeBytes / 1024} KB, geändert {result.RouteFileServerModified:dd.MM.yyyy HH:mm}"
                : $"{DropboxConstants.RouteFileName} nicht im Ordner";
            FolderFilesSummary = result.FilesInFolder.Count > 0
                ? $"{result.FilesInFolder.Count} Dateien: {string.Join(", ", result.FilesInFolder.Take(8))}{(result.FilesInFolder.Count > 8 ? " …" : "")}"
                : "Ordner leer oder nicht lesbar";
            ConnectionStatus = result.Success ? "Verbindung OK" : "Verbindungsfehler";
        }
        catch (Exception ex)
        {
            TestResult = ex.Message;
            ConnectionStatus = "Verbindungsfehler";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
