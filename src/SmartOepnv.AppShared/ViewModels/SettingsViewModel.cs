using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.AppShared.Views;

namespace SmartOepnv.AppShared.ViewModels;

public sealed partial class CompanyLogoListItem : ObservableObject
{
    private readonly string _id;

    public CompanyLogoListItem(CompanyLogoEntry entry, string filePath)
    {
        _id = entry.Id;
        _name = entry.Name;
        FilePath = filePath;
    }

    public string Id => _id;

    public string FilePath { get; }

    [ObservableProperty] private string _name;
}

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
    [ObservableProperty] private string brandingStatus = string.Empty;
    [ObservableProperty] private CompanyLogoListItem? selectedCompanyLogo;

    public ObservableCollection<CompanyLogoListItem> CompanyLogos { get; } = [];

    public bool ShowBrandingSection => AppServices.IsPlannerApp;

    public bool HasCompanyLogos => CompanyLogos.Count > 0;

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
        ReloadBranding();
    }

    private void ReloadBranding()
    {
        CompanyLogos.Clear();
        SelectedCompanyLogo = null;

        if (!AppServices.IsPlannerApp)
        {
            OnPropertyChanged(nameof(HasCompanyLogos));
            return;
        }

        foreach (var entry in PlanerBrandingWorkspace.GetLogos(AppServices.SettingsSubfolder))
        {
            var path = PlanerBrandingWorkspace.TryGetLogoPath(AppServices.SettingsSubfolder, entry.Id);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            CompanyLogos.Add(new CompanyLogoListItem(entry, path));
        }

        SelectedCompanyLogo = CompanyLogos.FirstOrDefault();
        BrandingStatus = HasCompanyLogos
            ? $"{CompanyLogos.Count} Logo(s) gespeichert – in Dienstvorlagen auswählbar."
            : "Noch kein Firmenlogo hinterlegt.";
        OnPropertyChanged(nameof(HasCompanyLogos));
    }

    [RelayCommand]
    private void ChooseCompanyLogo()
    {
        if (!AppServices.IsPlannerApp)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Firmenlogo hinzufügen",
            Filter = "Bilder (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Alle Dateien (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var entry = PlanerBrandingWorkspace.AddLogoFromFile(
                AppServices.SettingsSubfolder,
                dialog.FileName);
            var path = PlanerBrandingWorkspace.TryGetLogoPath(AppServices.SettingsSubfolder, entry.Id);
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Logo wurde gespeichert, ist aber nicht auffindbar.");
            }

            var item = new CompanyLogoListItem(entry, path);
            CompanyLogos.Add(item);
            SelectedCompanyLogo = item;
            BrandingStatus = $"Logo „{entry.Name}“ hinzugefügt.";
            OnPropertyChanged(nameof(HasCompanyLogos));
        }
        catch (Exception ex)
        {
            BrandingStatus = $"Logo konnte nicht gespeichert werden: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedCompanyLogo))]
    private void RemoveSelectedCompanyLogo()
    {
        if (!AppServices.IsPlannerApp || SelectedCompanyLogo is null)
        {
            return;
        }

        var id = SelectedCompanyLogo.Id;
        var name = SelectedCompanyLogo.Name;
        if (!PlanerBrandingWorkspace.RemoveLogo(AppServices.SettingsSubfolder, id))
        {
            BrandingStatus = "Logo konnte nicht entfernt werden.";
            return;
        }

        var index = CompanyLogos.IndexOf(SelectedCompanyLogo);
        CompanyLogos.Remove(SelectedCompanyLogo);
        SelectedCompanyLogo = CompanyLogos.Count == 0
            ? null
            : CompanyLogos[Math.Min(index, CompanyLogos.Count - 1)];
        BrandingStatus = $"Logo „{name}“ entfernt.";
        OnPropertyChanged(nameof(HasCompanyLogos));
    }

    private bool CanRemoveSelectedCompanyLogo() => SelectedCompanyLogo is not null;

    [RelayCommand(CanExecute = nameof(CanSaveSelectedLogoName))]
    private void SaveSelectedLogoName()
    {
        if (SelectedCompanyLogo is null)
        {
            return;
        }

        PersistSelectedLogoName(SelectedCompanyLogo);
    }

    private bool CanSaveSelectedLogoName() => SelectedCompanyLogo is not null;

    private void PersistSelectedLogoName(CompanyLogoListItem item)
    {
        if (!AppServices.IsPlannerApp)
        {
            return;
        }

        if (!PlanerBrandingWorkspace.UpdateLogoName(AppServices.SettingsSubfolder, item.Id, item.Name))
        {
            BrandingStatus = "Logo-Bezeichnung konnte nicht gespeichert werden.";
            return;
        }

        BrandingStatus = "Logo-Bezeichnung gespeichert.";
    }

    [RelayCommand]
    private async Task ConnectDropboxAsync()
    {
        var owner = System.Windows.Window.GetWindow(
            System.Windows.Application.Current.Windows.OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current.MainWindow);
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
        if (PersistFolderPath())
        {
            TestResult = "Ordnerpfad gespeichert.";
        }
    }

    /// <summary>Speichert den Ordnerpfad aus der Eingabe (z. B. beim Schließen des Setup-Dialogs).</summary>
    public bool PersistFolderPath()
    {
        var normalized = NormalizeFolderPath(FolderPath);
        var stored = AppServices.Dropbox.Settings;
        if (string.Equals(stored.FolderPath, normalized, StringComparison.Ordinal))
        {
            FolderPath = normalized;
            return false;
        }

        stored.FolderPath = normalized;
        AppServices.Dropbox.SaveSettings(stored);
        FolderPath = normalized;
        return true;
    }

    private static string NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DropboxConstants.DefaultFolderPath;
        }

        var trimmed = path.Trim();
        return trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!IsConnected)
        {
            TestResult = "Bitte zuerst mit Dropbox verbinden.";
            return;
        }

        PersistFolderPath();

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
