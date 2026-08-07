using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Kom;
using SmartOepnv.AppShared.Models;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.RoutePath;
using System.Windows;
using System.Text;

namespace SmartOepnv.AppShared.ViewModels;

public enum TransferButtonVisualState
{
    Idle,
    Active,
    Done
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
    [ObservableProperty] private TransferButtonVisualState dropboxImportButtonState = TransferButtonVisualState.Idle;
    [ObservableProperty] private TransferButtonVisualState dropboxExportButtonState = TransferButtonVisualState.Idle;
    [ObservableProperty] private TransferButtonVisualState dropboxLiteUpdateButtonState = TransferButtonVisualState.Idle;
    [ObservableProperty] private TransferButtonVisualState dropboxRemoteUpdateButtonState = TransferButtonVisualState.Idle;
    [ObservableProperty] private TransferButtonVisualState dropboxRemoteSettingsButtonState = TransferButtonVisualState.Idle;
    [ObservableProperty] private TransferButtonVisualState leitstelleStandButtonState = TransferButtonVisualState.Idle;
    [ObservableProperty] private TransferButtonVisualState planerWorkspaceImportButtonState = TransferButtonVisualState.Idle;
    [ObservableProperty] private TransferButtonVisualState planerWorkspaceExportButtonState = TransferButtonVisualState.Idle;
    private bool _remoteUpdateFlowRunning;
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
        IsDropboxConnected = AppServices.Dropbox.Settings.IsConnected;
        if (!_isLeitstelleProfile)
        {
            RefreshStats();
            UpdateLocalWorkspaceHint();
            RefreshPackageVersions();
        }
    }

    /// <summary>Nur Smart-ÖPNV Planer: Dropbox-Upload „Für Leitstelle speichern“.</summary>
    public bool ShowLeitstelleStandExportButton => !_isLeitstelleProfile;

    /// <summary>Nur Planer: Tablet-Einstellungen remote senden.</summary>
    public bool ShowRemoteSettingsSend => !_isLeitstelleProfile;

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
            $"Planer-Arbeitsstand: {new PlanerWorkspaceService(AppServices.SettingsSubfolder).LocalFilePath} " +
            $"(lokaler Routen-Cache: {AppServices.Workspace.PackageFilePath}; " +
            $"App-Vollstand routes_export.json nur manuell nach Dropbox).";
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
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(RefreshStats);
            return;
        }

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
        ExportLiteVehicleUpdateToDropboxCommand.NotifyCanExecuteChanged();
    }

    private bool CanTransferRouteUpdate() =>
        !IsBusy &&
        HasLoadedPackage &&
        IsDropboxConnected &&
        GetSelectedRouteNames().Count >= 1;

    private bool CanTransferMultipleRoutesSend() =>
        !IsBusy &&
        HasLoadedPackage &&
        IsDropboxConnected &&
        GetSelectedRouteNames().Count > 0;

    private bool CanExportLiteVehicleUpdate() =>
        !IsBusy &&
        HasLoadedPackage &&
        IsDropboxConnected &&
        DropboxLiteUpdateButtonState != TransferButtonVisualState.Active;

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

    [RelayCommand(CanExecute = nameof(CanTransferRouteUpdate))]
    private async Task TransferSelectedRoutesUpdateAsync()
    {
        var selected = GetSelectedRouteNames();
        if (selected.Count == 0)
        {
            LastActionMessage = "Update: bitte mindestens eine Route auswählen.";
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

    [RelayCommand(CanExecute = nameof(CanExportLiteVehicleUpdate))]
    private async Task ExportLiteVehicleUpdateToDropboxAsync()
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
            if (!ConfirmRoutePathIntegrityOrCancel())
            {
                LastActionMessage = "Kleines Fahrzeugupdate abgebrochen – Fahrweg-Prüfung.";
                return;
            }

            var json = AppServices.Routes.PrepareFullLiteVehicleUpdateJson();
            await AppServices.Dropbox.UploadNamedFileAsync(DropboxConstants.RouteUpdateFileName, json);
            LastActionMessage =
                $"Kleines Fahrzeugupdate hochgeladen ({DropboxConstants.RouteUpdateFileName}) – " +
                "Routen/Haltestellen ohne Tondateien; bestehende Ansagen auf dem Gerät bleiben.";
            DropboxLiteUpdateButtonState = TransferButtonVisualState.Done;
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Kleines Fahrzeugupdate fehlgeschlagen: {ex.Message}";
            DropboxLiteUpdateButtonState = TransferButtonVisualState.Idle;
        }
        finally
        {
            IsBusy = false;
            NotifyRouteTransferCommandsCanExecute();
        }
    }

    /// <summary>
    /// true = fortfahren, false = Abbruch.
    /// </summary>
    private bool ConfirmRoutePathIntegrityOrCancel()
    {
        var editor = AppServices.Routes.Editor;
        if (editor?.PackageRoot is null)
        {
            return true;
        }

        var bad = RoutePathDraftIntegrity.ScanPackage(editor.PackageRoot);
        if (bad.Count == 0)
        {
            return true;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{bad.Count} Fahrweg(e) wirken fehlerhaft (zu lang / Ende falsch / doppelte Segmente):");
        sb.AppendLine();
        foreach (var (routeKey, finding) in bad.Take(12))
        {
            sb.AppendLine($"• {routeKey}");
            sb.AppendLine($"  {finding.Message}");
            sb.AppendLine();
        }

        if (bad.Count > 12)
        {
            sb.AppendLine($"… und {bad.Count - 12} weitere.");
            sb.AppendLine();
        }

        sb.Append("Trotzdem hochladen? (Empfehlung: nein – zuerst im Fahrweg-Editor bereinigen.)");

        return SmartConfirmDialog.ShowConfirm(
            Application.Current?.MainWindow,
            "Fahrweg-Prüfung",
            sb.ToString(),
            confirmButton: "Ja",
            cancelButton: "Nein",
            preferCancel: true,
            width: 640);
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
            if (!ConfirmRoutePathIntegrityOrCancel())
            {
                LastActionMessage = $"{actionLabel} abgebrochen – Fahrweg-Prüfung.";
                return;
            }

            var json = AppServices.Routes.PrepareVehicleTransferJson(
                selectedRoutes,
                pruneOthersOnDevice,
                liteVehicleUpdate: true);
            await AppServices.Dropbox.UploadNamedFileAsync(DropboxConstants.RouteUpdateFileName, json);

            var routeLabel = selectedRoutes.Count == 1
                ? $"„{selectedRoutes[0]}“"
                : $"{selectedRoutes.Count} Routen";

            LastActionMessage = pruneOthersOnDevice
                ? $"{actionLabel}: {routeLabel} → {DropboxConstants.RouteUpdateFileName} (ohne Audio). Nicht enthaltene Routen werden auf dem Gerät entfernt."
                : $"{actionLabel}: {routeLabel} → {DropboxConstants.RouteUpdateFileName} (ohne Audio). Bestehende Tondateien bleiben erhalten.";

            DropboxLiteUpdateButtonState = TransferButtonVisualState.Done;
        }
        catch (Exception ex)
        {
            LastActionMessage = $"{actionLabel} fehlgeschlagen: {ex.Message}";
            DropboxLiteUpdateButtonState = TransferButtonVisualState.Idle;
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
            var json = await File.ReadAllTextAsync(dialog.FileName);
            if (_isLeitstelleProfile && LiteRouteUpdateMerge.IsLiteVehicleUpdate(json))
            {
                if (AppServices.Routes.TryMergeLiteRouteUpdateJson(json, out var mergeMessage))
                {
                    LastActionMessage = $"{mergeMessage} ({dialog.FileName})";
                }
                else
                {
                    LastActionMessage = mergeMessage;
                }
            }
            else
            {
                await AppServices.Routes.LoadFromFileAsync(dialog.FileName, persistLocally: true, source: "file-import");
                LastActionMessage = $"Importiert und lokal gespeichert: {dialog.FileName}";
            }

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

    [RelayCommand(CanExecute = nameof(CanImportFromDropbox))]
    private Task ImportFromDropboxAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return Task.CompletedTask;
        }

        _ = RunBackgroundTransferAsync(
            state => DropboxImportButtonState = state,
            async _ =>
            {
                if (await TryImportFromDropboxAsync().ConfigureAwait(true))
                {
                    RoutePackageImported?.Invoke();
                }
            });
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanImportPlanerWorkspace))]
    private Task ImportPlanerWorkspaceFromDropboxAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return Task.CompletedTask;
        }

        _ = RunBackgroundTransferAsync(
            state => PlanerWorkspaceImportButtonState = state,
            async progress =>
            {
                var result = await PlanerDropboxWorkspaceSync.TryImportFromDropboxAsync(forceOverwrite: false, progress)
                    .ConfigureAwait(true);
                if (!result.Imported &&
                    result.RemoteTimestamp > 0 &&
                    (result.LocalTimestamp > result.RemoteTimestamp || result.RemoteHasMoreContent))
                {
                    var reason = result.RemoteHasMoreContent
                        ? "Dropbox enthält mehr Daten als der lokale Stand (z. B. vom anderen Rechner)."
                        : "Der lokale Planer-Arbeitsstand ist neuer als Dropbox.";
                    var confirm = SmartConfirmDialog.ShowConfirm(
                        Application.Current?.MainWindow,
                        "Planer-Arbeitsstand laden",
                        $"{reason}\n\n" +
                        "Trotzdem von Dropbox laden? Ungespeicherte lokale Änderungen gehen dabei verloren.",
                        confirmButton: "Ja",
                        cancelButton: "Nein",
                        preferCancel: true);
                    if (confirm)
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
            },
            showProgress: true);
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanExportPlanerWorkspace))]
    private Task ExportPlanerWorkspaceToDropboxAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return Task.CompletedTask;
        }

        _ = RunBackgroundTransferAsync(
            state => PlanerWorkspaceExportButtonState = state,
            async progress =>
            {
                var result = await PlanerDropboxWorkspaceSync.TryExportAsync(flushBeforeCapture: true, progress: progress)
                    .ConfigureAwait(true);
                LastActionMessage = result.Message;
            },
            showProgress: true);
        return Task.CompletedTask;
    }

    private bool CanImportFromDropbox() =>
        IsDropboxConnected && DropboxImportButtonState != TransferButtonVisualState.Active;

    private bool CanImportPlanerWorkspace() =>
        IsDropboxConnected && PlanerWorkspaceImportButtonState != TransferButtonVisualState.Active;

    private bool CanExportPlanerWorkspace() =>
        IsDropboxConnected && PlanerWorkspaceExportButtonState != TransferButtonVisualState.Active;

    /// <summary>Lädt routes_export.json manuell von Dropbox in den Editor.</summary>
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

            var leitstelleRoutesResult = await LeitstelleRoutesDropboxSync.TryMergeFromDropboxAsync(cancellationToken)
                .ConfigureAwait(false);
            if (leitstelleRoutesResult.Imported)
            {
                LastActionMessage += $" {leitstelleRoutesResult.Message}";
            }

            var liteResult = await LiteRouteUpdateDropboxSync.TryMergeFromDropboxAsync(cancellationToken)
                .ConfigureAwait(false);
            if (liteResult.Imported)
            {
                LastActionMessage += $" {liteResult.Message}";
            }
            else if (!string.IsNullOrWhiteSpace(liteResult.Message) &&
                     !liteResult.Message.Contains("bereits übernommen", StringComparison.OrdinalIgnoreCase) &&
                     !liteResult.Message.Contains("Keine routes_update", StringComparison.OrdinalIgnoreCase))
            {
                LastActionMessage += $" ({liteResult.Message})";
            }
        }

        LastActionMessage = $"Importiert von Dropbox und lokal gespeichert: {AppServices.Dropbox.GetRouteFilePath()}";
        RefreshStats();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanExportToDropbox))]
    private Task ExportToDropboxAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return Task.CompletedTask;
        }

        if (!AppServices.Routes.HasPackage)
        {
            LastActionMessage = "Kein Paket geladen – zuerst importieren.";
            return Task.CompletedTask;
        }

        _ = RunBackgroundTransferAsync(
            state => DropboxExportButtonState = state,
            async _ =>
            {
                var json = AppServices.Routes.PrepareExportJson();
                await AppServices.Dropbox.UploadRouteFileAsync(json).ConfigureAwait(true);
                LastActionMessage = $"Nach Dropbox hochgeladen: {AppServices.Dropbox.GetRouteFilePath()}";
            });
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanExportToDropboxWithRemoteUpdate))]
    private Task ExportToDropboxWithRemoteUpdateAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return Task.CompletedTask;
        }

        if (!AppServices.Routes.HasPackage)
        {
            LastActionMessage = "Kein Paket geladen – zuerst importieren.";
            return Task.CompletedTask;
        }

        var json = AppServices.Routes.CurrentJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            LastActionMessage = "Kein Route-Paket geladen.";
            return Task.CompletedTask;
        }

        var vehicles = RegisteredVehicleInfo.ParseFromJson(json);
        var picker = new RemoteUpdateVehicleDialog(vehicles)
        {
            Owner = Application.Current.MainWindow
        };
        if (picker.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(picker.SelectedPhoneNumber) ||
            string.IsNullOrWhiteSpace(picker.SelectedVehicleName))
        {
            return Task.CompletedTask;
        }

        var vehicleName = picker.SelectedVehicleName;
        var vehiclePhone = picker.SelectedPhoneNumber;
        var transferMode = picker.SelectedTransferMode;
        var owner = Application.Current.MainWindow;

        _ = RunRemoteUpdateFlowAsync(owner!, vehicleName, vehiclePhone, transferMode);
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanSendRemoteSettings))]
    private Task SendRemoteSettingsAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return Task.CompletedTask;
        }

        var json = AppServices.Routes.CurrentJson;
        var vehicles = string.IsNullOrWhiteSpace(json)
            ? Array.Empty<RegisteredVehicleInfo>()
            : RegisteredVehicleInfo.ParseFromJson(json);

        var picker = new RemoteSettingsSendDialog(vehicles)
        {
            Owner = Application.Current.MainWindow
        };
        if (picker.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(picker.SelectedPhoneNumber) ||
            picker.Settings is null)
        {
            return Task.CompletedTask;
        }

        var vehicleName = picker.SelectedVehicleName ?? picker.SelectedPhoneNumber;
        var vehiclePhone = picker.SelectedPhoneNumber;
        var settings = picker.Settings;

        _ = RunBackgroundTransferAsync(
            state => DropboxRemoteSettingsButtonState = state,
            async _ =>
            {
                var commandId = await RemoteDeviceSettingsService
                    .UploadAsync(AppServices.Dropbox, vehiclePhone, settings)
                    .ConfigureAwait(true);
                var fileName = RemoteDeviceSettingsService.BuildFileName(vehiclePhone);
                LastActionMessage =
                    $"Einstellungen an {vehicleName} gesendet ({fileName}, commandId={commandId}).";
            });
        return Task.CompletedTask;
    }

    private async Task RunRemoteUpdateFlowAsync(
        Window owner,
        string vehicleName,
        string vehiclePhone,
        RemoteRouteTransferMode transferMode)
    {
        _remoteUpdateFlowRunning = true;
        ExportToDropboxWithRemoteUpdateCommand.NotifyCanExecuteChanged();
        DropboxRemoteUpdateButtonState = TransferButtonVisualState.Active;
        await YieldUiRenderAsync().ConfigureAwait(true);

        try
        {
            string uploadLabel;
            if (transferMode == RemoteRouteTransferMode.LiteUpdate)
            {
                var json = AppServices.Routes.PrepareFullLiteVehicleUpdateJson();
                await AppServices.Dropbox.UploadNamedFileAsync(DropboxConstants.RouteUpdateFileName, json)
                    .ConfigureAwait(true);
                uploadLabel =
                    $"{DropboxConstants.RouteUpdateFileName} nach Dropbox hochgeladen ({AppServices.Dropbox.GetNamedFilePath(DropboxConstants.RouteUpdateFileName)})";
            }
            else
            {
                var exportJson = AppServices.Routes.PrepareExportJson();
                await AppServices.Dropbox.UploadRouteFileAsync(exportJson).ConfigureAwait(true);
                uploadLabel =
                    $"{DropboxConstants.RouteFileName} nach Dropbox hochgeladen ({AppServices.Dropbox.GetRouteFilePath()})";
            }

            KomCommandAckFeedback.ShowSent(owner, vehicleName, uploadLabel);

            var outcome = await KomCommandSendFlow.ExecuteAsync(
                owner,
                statusLine: null,
                vehicleName,
                vehiclePhone,
                RemoteManualUpdateService.CommandType,
                ct => AppServices.Dropbox.TriggerRemoteManualUpdateAsync(vehiclePhone, ct),
                onProgressAck: () =>
                {
                    if (Application.Current?.Dispatcher is not { } dispatcher)
                    {
                        return;
                    }

                    dispatcher.BeginInvoke(() =>
                    {
                        DropboxRemoteUpdateButtonState = TransferButtonVisualState.Done;
                    });
                }).ConfigureAwait(true);

            LastActionMessage = outcome switch
            {
                KomCommandSendOutcome.Success =>
                    transferMode == RemoteRouteTransferMode.LiteUpdate
                        ? $"Update ({DropboxConstants.RouteUpdateFileName}) gesendet + Fernupdate abgeschlossen für {vehicleName}."
                        : $"Route gesendet + Fernupdate abgeschlossen für {vehicleName}.",
                KomCommandSendOutcome.ProgressOnly =>
                    transferMode == RemoteRouteTransferMode.LiteUpdate
                        ? $"Update hochgeladen, {vehicleName} lädt routes_update.json (Abschluss ausstehend)."
                        : $"Route hochgeladen, {vehicleName} lädt das Update (Abschluss ausstehend).",
                KomCommandSendOutcome.Timeout =>
                    transferMode == RemoteRouteTransferMode.LiteUpdate
                        ? $"Update hochgeladen, Fernupdate gesendet – keine Rückmeldung von {vehicleName}."
                        : $"Route hochgeladen, Fernupdate gesendet – keine Rückmeldung von {vehicleName}.",
                KomCommandSendOutcome.AckError =>
                    transferMode == RemoteRouteTransferMode.LiteUpdate
                        ? $"Update hochgeladen – Fehler beim Fernupdate ({vehicleName})."
                        : $"Route hochgeladen – Fehler beim Fernupdate ({vehicleName}).",
                _ => $"Fernupdate fehlgeschlagen für {vehicleName}."
            };

            DropboxRemoteUpdateButtonState = outcome switch
            {
                KomCommandSendOutcome.UploadFailed or KomCommandSendOutcome.AckError =>
                    TransferButtonVisualState.Idle,
                _ => TransferButtonVisualState.Done
            };
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Fernupdate fehlgeschlagen: {ex.Message}";
            DropboxRemoteUpdateButtonState = TransferButtonVisualState.Idle;
        }
        finally
        {
            _remoteUpdateFlowRunning = false;
            ExportToDropboxWithRemoteUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveLeitstelleStand))]
    private Task SaveLeitstelleStandToDropboxAsync()
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            LastActionMessage = "Dropbox nicht verbunden – bitte Einstellungen öffnen.";
            return Task.CompletedTask;
        }

        if (!AppServices.Routes.HasPackage)
        {
            LastActionMessage = "Kein Paket geladen – zuerst importieren.";
            return Task.CompletedTask;
        }

        _ = RunBackgroundTransferAsync(
            state => LeitstelleStandButtonState = state,
            async _ =>
            {
                AppServices.FlushAllPendingEdits();
                var result = await LeitstelleStandDropboxSync.TryExportAsync().ConfigureAwait(true);
                LastActionMessage = result.Exported
                    ? $"Für Leitstelle gespeichert: {DropboxConstants.LeitstelleStandFileName} + " +
                      $"{DropboxConstants.LeitstelleRoutesFileName} (Fahrer, Fahrzeuge, Vorlagen, Routen, Fahrwege – ohne Fahrzeug-Update)."
                    : result.Message;
            });
        return Task.CompletedTask;
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

    private async Task RunBackgroundTransferAsync(
        Action<TransferButtonVisualState> setButtonState,
        Func<IProgress<DropboxTransferProgress>?, Task> work,
        bool showProgress = false)
    {
        setButtonState(TransferButtonVisualState.Active);
        if (showProgress)
        {
            IsTransferProgressVisible = true;
            TransferProgressPhase = "Wird gestartet…";
            TransferProgressPercent = 0;
        }

        await YieldUiRenderAsync().ConfigureAwait(true);

        IProgress<DropboxTransferProgress>? progress = showProgress ? CreateTransferProgress() : null;
        try
        {
            await work(progress).ConfigureAwait(true);
            await RunOnUiAsync(() => setButtonState(TransferButtonVisualState.Done)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                LastActionMessage = $"Fehler: {ex.Message}";
                setButtonState(TransferButtonVisualState.Idle);
            }).ConfigureAwait(true);
        }
        finally
        {
            if (showProgress)
            {
                ResetTransferProgress();
            }
        }
    }

    private static async Task YieldUiRenderAsync()
    {
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
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

    partial void OnDropboxImportButtonStateChanged(TransferButtonVisualState value) =>
        ImportFromDropboxCommand.NotifyCanExecuteChanged();

    partial void OnDropboxExportButtonStateChanged(TransferButtonVisualState value) =>
        ExportToDropboxCommand.NotifyCanExecuteChanged();

    partial void OnDropboxLiteUpdateButtonStateChanged(TransferButtonVisualState value) =>
        ExportLiteVehicleUpdateToDropboxCommand.NotifyCanExecuteChanged();

    partial void OnDropboxRemoteUpdateButtonStateChanged(TransferButtonVisualState value) =>
        ExportToDropboxWithRemoteUpdateCommand.NotifyCanExecuteChanged();

    partial void OnDropboxRemoteSettingsButtonStateChanged(TransferButtonVisualState value) =>
        SendRemoteSettingsCommand.NotifyCanExecuteChanged();

    partial void OnLeitstelleStandButtonStateChanged(TransferButtonVisualState value) =>
        SaveLeitstelleStandToDropboxCommand.NotifyCanExecuteChanged();

    partial void OnPlanerWorkspaceImportButtonStateChanged(TransferButtonVisualState value) =>
        ImportPlanerWorkspaceFromDropboxCommand.NotifyCanExecuteChanged();

    partial void OnPlanerWorkspaceExportButtonStateChanged(TransferButtonVisualState value) =>
        ExportPlanerWorkspaceToDropboxCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        ExportToDropboxCommand.NotifyCanExecuteChanged();
        ExportLiteVehicleUpdateToDropboxCommand.NotifyCanExecuteChanged();
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
        DropboxExportButtonState != TransferButtonVisualState.Active;

    private bool CanExportToDropboxWithRemoteUpdate() =>
        !_remoteUpdateFlowRunning &&
        DropboxRemoteUpdateButtonState != TransferButtonVisualState.Active;

    private bool CanSendRemoteSettings() =>
        DropboxRemoteSettingsButtonState != TransferButtonVisualState.Active;

    private bool CanSaveLeitstelleStand() =>
        LeitstelleStandButtonState != TransferButtonVisualState.Active;
}
