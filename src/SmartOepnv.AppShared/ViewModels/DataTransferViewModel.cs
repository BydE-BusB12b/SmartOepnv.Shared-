using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Models;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using System.Windows;

namespace SmartOepnv.AppShared.ViewModels;

public partial class DataTransferViewModel : ObservableObject
{
    public event Action? RoutePackageImported;
    [ObservableProperty] private int routeCount;
    [ObservableProperty] private int stopCount;
    [ObservableProperty] private int driverCount;
    [ObservableProperty] private bool hasLoadedPackage;
    [ObservableProperty] private string lastActionMessage = "Noch kein Route-Paket geladen.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isDropboxConnected;
    [ObservableProperty] private string localWorkspaceHint = string.Empty;
    [ObservableProperty] private bool hasInspectionWarnings;
    [ObservableProperty] private bool hasDriverCredentialWarnings;

    public ObservableCollection<VehicleInspectionWarningItem> InspectionWarnings { get; } = [];
    public ObservableCollection<DriverCredentialWarningItem> DriverCredentialWarnings { get; } = [];

    /// <summary>Wird beim Klick auf einen HU-/SP-Hinweis ausgelöst (normalisierte Telefonnummer oder leer).</summary>
    public event Action<string?>? NavigateToVehicleManagementRequested;

    /// <summary>Wird beim Klick auf eine Fahrer-Warnung ausgelöst (Personalnummer, 4-stellig).</summary>
    public event Action<string?>? NavigateToEmployeeManagementRequested;

    public DataTransferViewModel()
    {
        RefreshStats();
        IsDropboxConnected = AppServices.Dropbox.Settings.IsConnected;
        UpdateLocalWorkspaceHint();
    }

    private void UpdateLocalWorkspaceHint()
    {
        if (!AppServices.IsInitialized)
        {
            LocalWorkspaceHint = string.Empty;
            return;
        }

        LocalWorkspaceHint =
            $"Lokaler Arbeits-Speicher: {AppServices.Workspace.PackageFilePath} – gleicher Inhalt wie Dropbox (Routen, Navidaten, Hinweise, Fahrer, Fahrzeuge).";
    }

    public void RefreshStats()
    {
        var stats = AppServices.Routes.Stats;
        RouteCount = stats.RouteCount;
        StopCount = stats.StopCount;
        DriverCount = stats.DriverCount;
        HasLoadedPackage = AppServices.Routes.HasPackage;
        IsDropboxConnected = AppServices.Dropbox.Settings.IsConnected;
        UpdateLocalWorkspaceHint();
        RefreshInspectionWarnings();
        RefreshDriverCredentialWarnings();
    }

    public void RefreshInspectionWarnings()
    {
        InspectionWarnings.Clear();
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            HasInspectionWarnings = false;
            return;
        }

        foreach (var warning in VehicleInspectionWarningEvaluator.Evaluate(editor.RegisteredVehicles))
        {
            InspectionWarnings.Add(VehicleInspectionWarningItem.FromWarning(warning));
        }

        HasInspectionWarnings = InspectionWarnings.Count > 0;
    }

    public void RefreshDriverCredentialWarnings()
    {
        DriverCredentialWarnings.Clear();
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            HasDriverCredentialWarnings = false;
            return;
        }

        foreach (var warning in DriverCredentialWarningEvaluator.Evaluate(editor.Employees))
        {
            DriverCredentialWarnings.Add(DriverCredentialWarningItem.FromWarning(warning));
        }

        HasDriverCredentialWarnings = DriverCredentialWarnings.Count > 0;
    }

    [RelayCommand]
    private void OpenVehicleFromInspectionWarning(VehicleInspectionWarningItem? item)
    {
        var key = item?.PhoneNormalized;
        NavigateToVehicleManagementRequested?.Invoke(
            string.IsNullOrWhiteSpace(key) ? null : key);
    }

    [RelayCommand]
    private void OpenEmployeeFromCredentialWarning(DriverCredentialWarningItem? item)
    {
        var key = item?.PersonnelNumberNormalized;
        NavigateToEmployeeManagementRequested?.Invoke(
            string.IsNullOrWhiteSpace(key) ? null : key);
    }

    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Route-Paket importieren",
            Filter = "JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*",
            FileName = DropboxConstants.RouteFileName
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await AppServices.Routes.LoadFromFileAsync(dialog.FileName, persistLocally: true, source: "file-import");
            LastActionMessage = $"Importiert und lokal gespeichert: {dialog.FileName}";
            RefreshStats();
            RoutePackageImported?.Invoke();
        });
    }

    [RelayCommand]
    private async Task ExportToFileAsync()
    {
        if (!AppServices.Routes.HasPackage)
        {
            LastActionMessage = "Kein Paket geladen – zuerst importieren.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Route-Paket exportieren",
            Filter = "JSON-Dateien (*.json)|*.json",
            FileName = DropboxConstants.RouteFileName
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var json = AppServices.Routes.PrepareExportJson();
            await AppServices.Routes.SaveToFileAsync(dialog.FileName);
            LastActionMessage = $"Exportiert nach: {dialog.FileName} ({json.Length / 1024} KB)";
            RefreshStats();
        });
    }

    [RelayCommand]
    private async Task ImportFromDropboxAsync()
    {
        await RunAsync(async () =>
        {
            if (await TryImportFromDropboxAsync())
            {
                RoutePackageImported?.Invoke();
            }
        });
    }

    /// <summary>
    /// Lädt routes_export.json von Dropbox in den Editor. Wird beim Programmstart und manuell aufgerufen.
    /// </summary>
    public async Task<bool> TryImportFromDropboxAsync(CancellationToken cancellationToken = default)
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return false;
        }

        var json = await AppServices.Dropbox.DownloadRouteFileAsync(cancellationToken);
        AppServices.Routes.LoadFromJson(json, persistLocally: true, source: "dropbox-import");
        LastActionMessage = $"Importiert von Dropbox und lokal gespeichert: {AppServices.Dropbox.GetRouteFilePath()}";
        RefreshStats();
        return true;
    }

    [RelayCommand]
    private async Task ExportToDropboxAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return;
        }

        if (!AppServices.Routes.HasPackage)
        {
            LastActionMessage = "Kein Paket geladen – zuerst importieren.";
            return;
        }

        await RunAsync(async () =>
        {
            var json = AppServices.Routes.PrepareExportJson();
            await AppServices.Dropbox.UploadRouteFileAsync(json);
            LastActionMessage = $"Nach Dropbox hochgeladen: {AppServices.Dropbox.GetRouteFilePath()}";
        });
    }

    [RelayCommand]
    private async Task ExportToDropboxWithRemoteUpdateAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return;
        }

        if (!AppServices.Routes.HasPackage)
        {
            LastActionMessage = "Kein Paket geladen – zuerst importieren.";
            return;
        }

        var json = AppServices.Routes.CurrentJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            LastActionMessage = "Kein Route-Paket geladen.";
            return;
        }

        var vehicles = RegisteredVehicleInfo.ParseFromJson(json);
        var picker = new RemoteUpdateVehicleDialog(vehicles)
        {
            Owner = Application.Current.MainWindow
        };
        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedPhoneNumber))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var exportJson = AppServices.Routes.PrepareExportJson();
            await AppServices.Dropbox.UploadRouteFileAsync(exportJson);
            await AppServices.Dropbox.TriggerRemoteManualUpdateAsync(picker.SelectedPhoneNumber);
            LastActionMessage =
                $"Route gesendet + Fernupdate ausgelöst für {picker.SelectedPhoneNumber} ({AppServices.Dropbox.GetRouteFilePath()})";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
