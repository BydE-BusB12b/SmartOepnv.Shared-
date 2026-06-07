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

public enum DropboxExportButtonState
{
    Idle,
    Sending,
    Sent
}

public partial class DataTransferViewModel : ObservableObject
{
    public event Action? RoutePackageImported;
    [ObservableProperty] private int routeCount;
    [ObservableProperty] private int stopCount;
    [ObservableProperty] private int driverCount;
    [ObservableProperty] private bool hasLoadedPackage;
    [ObservableProperty] private string lastActionMessage = "Noch kein Route-Paket geladen.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private DropboxExportButtonState dropboxExportButtonState = DropboxExportButtonState.Idle;
    [ObservableProperty] private bool isDropboxConnected;
    [ObservableProperty] private string localWorkspaceHint = string.Empty;
    [ObservableProperty] private bool hasInspectionWarnings;
    [ObservableProperty] private bool hasDriverCredentialWarnings;
    [ObservableProperty] private string newVersionLabel = string.Empty;
    [ObservableProperty] private PlannerPackageVersionInfo? selectedPackageVersion;

    public ObservableCollection<VehicleInspectionWarningItem> InspectionWarnings { get; } = [];
    public ObservableCollection<DriverCredentialWarningItem> DriverCredentialWarnings { get; } = [];
    public ObservableCollection<PlannerPackageVersionInfo> PackageVersions { get; } = [];

    /// <summary>Wird beim Klick auf einen HU-/SP-Hinweis ausgelöst (normalisierte Telefonnummer oder leer).</summary>
    public event Action<string?>? NavigateToVehicleManagementRequested;

    /// <summary>Wird beim Klick auf eine Fahrer-Warnung ausgelöst (Personalnummer, 4-stellig).</summary>
    public event Action<string?>? NavigateToEmployeeManagementRequested;

    private readonly bool _isLeitstelleProfile;

    public DataTransferViewModel(SmartOepnvAppProfile profile)
    {
        _isLeitstelleProfile = profile.IsLeitstelle;
        RefreshStats();
        IsDropboxConnected = AppServices.Dropbox.Settings.IsConnected;
        UpdateLocalWorkspaceHint();
        RefreshPackageVersions();
    }

    /// <summary>Nur Smart-ÖPNV Planer: Dropbox-Upload „Für Leitstelle speichern“.</summary>
    public bool ShowLeitstelleStandExportButton => !_isLeitstelleProfile;

    /// <summary>Nur Planer: JSON-Snapshots als Versionen speichern/laden.</summary>
    public bool ShowVersionManagement => !_isLeitstelleProfile && AppServices.PlannerVersions is not null;

    public string PlannerLocalOverlayHint =>
        AppServices.PlannerLocal is null
            ? string.Empty
            : $"Fahrer & Fahrzeuge (Planer, Priorität): {AppServices.PlannerLocal.OverlayFilePath}";

    private void UpdateLocalWorkspaceHint()
    {
        if (!AppServices.IsInitialized)
        {
            LocalWorkspaceHint = string.Empty;
            return;
        }

        var routesHint =
            $"Routen-Arbeitsstand: {AppServices.Workspace.PackageFilePath} (Dropbox routes_export.json).";
        var overlayHint = PlannerLocalOverlayHint;
        LocalWorkspaceHint = string.IsNullOrWhiteSpace(overlayHint)
            ? routesHint
            : $"{routesHint} {overlayHint}";
        OnPropertyChanged(nameof(PlannerLocalOverlayHint));
    }

    public void RefreshPackageVersions()
    {
        PackageVersions.Clear();
        SelectedPackageVersion = null;
        if (AppServices.PlannerVersions is null)
        {
            return;
        }

        foreach (var v in AppServices.PlannerVersions.List())
        {
            PackageVersions.Add(v);
        }

        SelectedPackageVersion = PackageVersions.FirstOrDefault();
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
        if (_isLeitstelleProfile)
        {
            var stand = await AppServices.Dropbox.TryDownloadLeitstelleStandAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stand))
            {
                AppServices.Routes.TryMergeLeitstelleStandJson(stand);
            }
        }

        LastActionMessage = $"Importiert von Dropbox und lokal gespeichert: {AppServices.Dropbox.GetRouteFilePath()}";
        RefreshStats();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanExportToDropbox))]
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

        DropboxExportButtonState = DropboxExportButtonState.Sending;
        IsBusy = true;
        try
        {
            var json = AppServices.Routes.PrepareExportJson();
            await AppServices.Dropbox.UploadRouteFileAsync(json);
            LastActionMessage = $"Nach Dropbox hochgeladen: {AppServices.Dropbox.GetRouteFilePath()}";
            DropboxExportButtonState = DropboxExportButtonState.Sent;
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Fehler: {ex.Message}";
            DropboxExportButtonState = DropboxExportButtonState.Idle;
        }
        finally
        {
            IsBusy = false;
        }
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

    [RelayCommand]
    private async Task SaveLeitstelleStandToDropboxAsync()
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
            AppServices.FlushAllPendingEdits();
            var json = AppServices.Routes.BuildLeitstelleStandJson();
            await AppServices.Dropbox.UploadLeitstelleStandAsync(json);
            LastActionMessage =
                $"Für Leitstelle gespeichert: {DropboxConstants.LeitstelleStandFileName} (Fahrer, Fahrzeuge, Vorlagen, Fahrwege).";
        });
    }

    [RelayCommand]
    private void SavePackageVersion()
    {
        if (AppServices.PlannerVersions is null)
        {
            return;
        }

        if (!AppServices.Routes.HasPackage)
        {
            LastActionMessage = "Kein Paket geladen – zuerst importieren.";
            return;
        }

        try
        {
            AppServices.FlushAllPendingEdits();
            var json = AppServices.Routes.PrepareExportJson();
            var info = AppServices.PlannerVersions.Save(NewVersionLabel, json);
            RefreshPackageVersions();
            SelectedPackageVersion = PackageVersions.FirstOrDefault(v => v.Id == info.Id) ?? info;
            NewVersionLabel = string.Empty;
            LastActionMessage =
                $"Version gespeichert: {info.DisplayLine} – Fahrer/Fahrzeuge beim Laden weiterhin aus dem Planer-Overlay.";
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Fehler beim Speichern der Version: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadPackageVersion()
    {
        if (AppServices.PlannerVersions is null || SelectedPackageVersion is null)
        {
            LastActionMessage = "Bitte eine Version auswählen.";
            return;
        }

        try
        {
            var json = AppServices.PlannerVersions.TryLoadPackageJson(SelectedPackageVersion.Id);
            if (string.IsNullOrWhiteSpace(json))
            {
                LastActionMessage = "Version konnte nicht gelesen werden.";
                return;
            }

            AppServices.Routes.LoadFromJson(json, persistLocally: true, source: "planner-version");
            LastActionMessage =
                $"Version geladen: {SelectedPackageVersion.DisplayLine} – Fahrer/Fahrzeuge aus Planer-Overlay übernommen.";
            RefreshStats();
            RoutePackageImported?.Invoke();
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Fehler beim Laden der Version: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DeletePackageVersion()
    {
        if (AppServices.PlannerVersions is null || SelectedPackageVersion is null)
        {
            LastActionMessage = "Bitte eine Version zum Löschen auswählen.";
            return;
        }

        var label = SelectedPackageVersion.DisplayLine;
        if (!AppServices.PlannerVersions.TryDelete(SelectedPackageVersion.Id))
        {
            LastActionMessage = "Version konnte nicht gelöscht werden.";
            return;
        }

        RefreshPackageVersions();
        LastActionMessage = $"Version gelöscht: {label}";
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

    partial void OnDropboxExportButtonStateChanged(DropboxExportButtonState value) =>
        ExportToDropboxCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) =>
        ExportToDropboxCommand.NotifyCanExecuteChanged();

    private bool CanExportToDropbox() =>
        !IsBusy && DropboxExportButtonState != DropboxExportButtonState.Sending;
}
