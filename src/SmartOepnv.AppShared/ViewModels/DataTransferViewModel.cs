using System.Collections.ObjectModel;
using System.ComponentModel;
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
    [ObservableProperty] private bool hasDocumentCheckWarnings;
    [ObservableProperty] private string newVersionLabel = string.Empty;
    [ObservableProperty] private PlannerPackageVersionInfo? selectedPackageVersion;
    [ObservableProperty] private bool isTransferProgressVisible;
    [ObservableProperty] private double transferProgressPercent;
    [ObservableProperty] private string transferProgressPhase = string.Empty;
    [ObservableProperty] private string transferProgressEta = string.Empty;

    public ObservableCollection<VehicleInspectionWarningItem> InspectionWarnings { get; } = [];
    public ObservableCollection<DriverCredentialWarningItem> DriverCredentialWarnings { get; } = [];
    public ObservableCollection<EmployeeDocumentCheckWarningItem> DocumentCheckWarnings { get; } = [];
    public ObservableCollection<PlannerPackageVersionInfo> PackageVersions { get; } = [];
    public ObservableCollection<RouteTransferSelectionItem> RouteTransferItems { get; } = [];

    public bool HasRouteTransferItems => RouteTransferItems.Count > 0;

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

    /// <summary>Nur Planer: planer_workspace.json manuell mit Dropbox abgleichen.</summary>
    public bool ShowPlanerWorkspaceSync => !_isLeitstelleProfile && AppServices.IsPlannerApp;

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
        RefreshDocumentCheckWarnings();
        RefreshRouteTransferSelections();
    }

    private void RefreshRouteTransferSelections()
    {
        foreach (var item in RouteTransferItems)
        {
            item.PropertyChanged -= OnRouteTransferItemPropertyChanged;
        }

        RouteTransferItems.Clear();
        OnPropertyChanged(nameof(HasRouteTransferItems));

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            NotifyRouteTransferCommandsCanExecute();
            return;
        }

        foreach (var route in editor.RouteNames.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
        {
            var item = new RouteTransferSelectionItem(route);
            item.PropertyChanged += OnRouteTransferItemPropertyChanged;
            RouteTransferItems.Add(item);
        }

        OnPropertyChanged(nameof(HasRouteTransferItems));
        NotifyRouteTransferCommandsCanExecute();
    }

    private void OnRouteTransferItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RouteTransferSelectionItem.IsSelected))
        {
            NotifyRouteTransferCommandsCanExecute();
        }
    }

    private List<string> GetSelectedRouteNames() =>
        RouteTransferItems.Where(i => i.IsSelected).Select(i => i.RouteName).ToList();

    private void NotifyRouteTransferCommandsCanExecute()
    {
        TransferSelectedRoutesUpdateCommand.NotifyCanExecuteChanged();
        TransferSelectedRoutesSendCommand.NotifyCanExecuteChanged();
    }

    private bool CanTransferSingleRouteUpdate() =>
        !IsBusy &&
        HasLoadedPackage &&
        IsDropboxConnected &&
        GetSelectedRouteNames().Count == 1;

    private bool CanTransferMultipleRoutesSend() =>
        !IsBusy &&
        HasLoadedPackage &&
        IsDropboxConnected &&
        GetSelectedRouteNames().Count > 0;

    [RelayCommand]
    private void SelectAllRoutesForTransfer()
    {
        foreach (var item in RouteTransferItems)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAllRoutesForTransfer()
    {
        foreach (var item in RouteTransferItems)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanTransferSingleRouteUpdate))]
    private async Task TransferSelectedRoutesUpdateAsync()
    {
        var selected = GetSelectedRouteNames();
        if (selected.Count != 1)
        {
            LastActionMessage = "Update: bitte genau eine Route auswählen.";
            return;
        }

        await UploadVehicleTransferAsync(selected, pruneOthersOnDevice: false, "Update");
    }

    [RelayCommand(CanExecute = nameof(CanTransferMultipleRoutesSend))]
    private async Task TransferSelectedRoutesSendAsync()
    {
        var selected = GetSelectedRouteNames();
        if (selected.Count == 0)
        {
            LastActionMessage = "Senden: bitte mindestens eine Route auswählen.";
            return;
        }

        await UploadVehicleTransferAsync(selected, pruneOthersOnDevice: true, "Senden");
    }

    private async Task UploadVehicleTransferAsync(
        IReadOnlyList<string> selectedRoutes,
        bool pruneOthersOnDevice,
        string actionLabel)
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

        IsBusy = true;
        try
        {
            var json = AppServices.Routes.PrepareVehicleTransferJson(selectedRoutes, pruneOthersOnDevice);
            await AppServices.Dropbox.UploadRouteFileAsync(json);

            var routeLabel = selectedRoutes.Count == 1
                ? $"„{selectedRoutes[0]}“"
                : $"{selectedRoutes.Count} Routen";

            LastActionMessage = pruneOthersOnDevice
                ? $"{actionLabel}: {routeLabel} nach Dropbox gesendet – Fahrzeuge gleichen ab, andere Routen werden entfernt."
                : $"{actionLabel}: {routeLabel} nach Dropbox gesendet – Fahrzeuge ergänzen/aktualisieren nur diese Route(n).";

            DropboxExportButtonState = DropboxExportButtonState.Sent;
        }
        catch (Exception ex)
        {
            LastActionMessage = $"{actionLabel} fehlgeschlagen: {ex.Message}";
            DropboxExportButtonState = DropboxExportButtonState.Idle;
        }
        finally
        {
            IsBusy = false;
            NotifyRouteTransferCommandsCanExecute();
        }
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

    public void RefreshDocumentCheckWarnings()
    {
        DocumentCheckWarnings.Clear();
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            HasDocumentCheckWarnings = false;
            return;
        }

        foreach (var warning in EmployeeDocumentCheckWarningEvaluator.Evaluate(editor.Employees))
        {
            DocumentCheckWarnings.Add(EmployeeDocumentCheckWarningItem.FromWarning(warning));
        }

        HasDocumentCheckWarnings = DocumentCheckWarnings.Count > 0;
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
    private void OpenEmployeeFromDocumentCheckWarning(EmployeeDocumentCheckWarningItem? item)
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

    [RelayCommand(CanExecute = nameof(CanUsePlanerWorkspaceSync))]
    private async Task ImportPlanerWorkspaceFromDropboxAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return;
        }

        await RunAsync(async () =>
        {
            var progress = CreateTransferProgress();
            var result = await PlanerDropboxWorkspaceSync.TryImportFromDropboxAsync(forceOverwrite: false, progress)
                .ConfigureAwait(true);
            if (!result.Imported &&
                result.RemoteTimestamp > 0 &&
                (result.LocalTimestamp > result.RemoteTimestamp || result.RemoteHasMoreContent))
            {
                var reason = result.RemoteHasMoreContent
                    ? "Dropbox enthält mehr Daten als der lokale Stand (z. B. vom anderen Rechner)."
                    : "Der lokale Planer-Arbeitsstand ist neuer als Dropbox.";
                var confirm = MessageBox.Show(
                    $"{reason}\n\n" +
                    "Trotzdem von Dropbox laden? Ungespeicherte lokale Änderungen gehen dabei verloren.",
                    "Planer-Arbeitsstand laden",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm == MessageBoxResult.Yes)
                {
                    result = await PlanerDropboxWorkspaceSync.TryImportFromDropboxAsync(forceOverwrite: true, progress)
                        .ConfigureAwait(true);
                }
            }

            LastActionMessage = result.Message;
            if (result.Imported)
            {
                RefreshStats();
                RefreshPackageVersions();
                RoutePackageImported?.Invoke();
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUsePlanerWorkspaceSync))]
    private async Task ExportPlanerWorkspaceToDropboxAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return;
        }

        await RunAsync(async () =>
        {
            AppServices.FlushAllPendingEditsBestEffort();
            var progress = CreateTransferProgress();
            var result = await PlanerDropboxWorkspaceSync.TryExportAsync(flushBeforeCapture: true, progress: progress)
                .ConfigureAwait(true);
            LastActionMessage = result.Message;
        });
    }

    private bool CanUsePlanerWorkspaceSync() => !IsBusy && IsDropboxConnected;

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
            var standResult = await LeitstelleStandDropboxSync.TryMergeFromDropboxAsync(cancellationToken)
                .ConfigureAwait(false);
            if (standResult.Imported)
            {
                LastActionMessage += $" {standResult.Message}";
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
            var result = await LeitstelleStandDropboxSync.TryExportAsync();
            LastActionMessage = result.Exported
                ? $"Für Leitstelle gespeichert: {DropboxConstants.LeitstelleStandFileName} (Fahrer, Fahrzeuge, Vorlagen, Fahrwege)."
                : result.Message;
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

    private IProgress<DropboxTransferProgress> CreateTransferProgress()
    {
        return new Progress<DropboxTransferProgress>(p =>
        {
            IsTransferProgressVisible = true;
            TransferProgressPhase = p.Phase;
            TransferProgressPercent = p.Percent;
            TransferProgressEta = p.EtaDisplay;
        });
    }

    private void ResetTransferProgress()
    {
        IsTransferProgressVisible = false;
        TransferProgressPhase = string.Empty;
        TransferProgressPercent = 0;
        TransferProgressEta = string.Empty;
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
            ResetTransferProgress();
        }
    }

    partial void OnDropboxExportButtonStateChanged(DropboxExportButtonState value) =>
        ExportToDropboxCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        ExportToDropboxCommand.NotifyCanExecuteChanged();
        ImportPlanerWorkspaceFromDropboxCommand.NotifyCanExecuteChanged();
        ExportPlanerWorkspaceToDropboxCommand.NotifyCanExecuteChanged();
        NotifyRouteTransferCommandsCanExecute();
    }

    partial void OnIsDropboxConnectedChanged(bool value)
    {
        ImportPlanerWorkspaceFromDropboxCommand.NotifyCanExecuteChanged();
        ExportPlanerWorkspaceToDropboxCommand.NotifyCanExecuteChanged();
        NotifyRouteTransferCommandsCanExecute();
    }

    partial void OnHasLoadedPackageChanged(bool value) =>
        NotifyRouteTransferCommandsCanExecute();

    private bool CanExportToDropbox() =>
        !IsBusy && DropboxExportButtonState != DropboxExportButtonState.Sending;
}
