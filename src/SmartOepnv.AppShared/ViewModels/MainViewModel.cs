using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using SmartOepnv.AppShared.Models;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SmartOepnvAppProfile _profile;
    private readonly DataTransferViewModel _dataTransferViewModel;
    private readonly SettingsViewModel _settingsViewModel = new();
    private readonly RoutesViewModel _routesViewModel = new();
    private readonly RoutePathEditorViewModel _routePathEditorViewModel = new();
    private readonly EmployeesViewModel _employeesViewModel = new();
    private readonly StopsLibraryViewModel _stopsLibraryViewModel = new();
    private readonly AnnouncementsLibraryViewModel _announcementsLibraryViewModel = new();
    private readonly VehicleManagementViewModel _vehicleManagementViewModel = new();
    private readonly MessagesViewModel _messagesViewModel = new();
    private readonly MessageSendViewModel _messageSendViewModel = new();
    private readonly LeitstelleMessagesInboxViewModel _leitstelleMessagesInboxViewModel = new();
    private readonly DisplaysOperationsViewModel _displaysOperationsViewModel = new();
    private readonly VehicleTrackingViewModel _vehicleTrackingViewModel = new();
    private readonly ZeitwirtschaftPlannerViewModel _zeitwirtschaftPlannerViewModel = new();
    private readonly SevSignEditorViewModel _sevSignEditorViewModel = new();
    private readonly FahrerdispoViewModel _fahrerdispoViewModel = new();
    private readonly FahrzeugdispoViewModel _fahrzeugdispoViewModel = new();
    private readonly DienstvorlagenViewModel _dienstvorlagenViewModel = new();
    private readonly DienstvorlagenLibraryViewModel _dienstvorlagenLibraryViewModel = new();

    private NavigationItem? _previousNavigationItem;
    private bool _suppressNavigationCommit;
    private NavigationItem? _leitstelleMessagesNavItem;
    private NavigationItem? _fahrzeugverwaltungNavItem;
    private NavigationItem? _personalverwaltungNavItem;

    public MainViewModel(SmartOepnvAppProfile profile)
    {
        _profile = profile;
        ProductName = profile.ProductName;
        ProductSubtitle = profile.ProductSubtitle;
        DashboardHint = profile.DashboardHint;
        AppVersion = ResolveDisplayedAppVersion();
        _dataTransferViewModel = new DataTransferViewModel(profile);

        NavigationItems = new ObservableCollection<NavigationItem>(CreateNavigationItems());
        SelectedNavigationItem = NavigationItems[0];
        CurrentPage = SelectedNavigationItem.Content;
        _previousNavigationItem = SelectedNavigationItem;

        _dataTransferViewModel.RoutePackageImported += OnRoutePackageLoaded;
        _dataTransferViewModel.NavigateToVehicleManagementRequested += OnNavigateToVehicleManagementRequested;
        _dataTransferViewModel.NavigateToEmployeeManagementRequested += OnNavigateToEmployeeManagementRequested;
        _fahrerdispoViewModel.NavigateToEmployeeManagementRequested += OnNavigateToEmployeeManagementFromDispoRequested;
        _leitstelleMessagesInboxViewModel.SosAlertRaised += OnLeitstelleSosAlertRaised;
        _leitstelleMessagesInboxViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LeitstelleMessagesInboxViewModel.UnreadMailCount) or
                nameof(LeitstelleMessagesInboxViewModel.HasUnreadMail))
            {
                UpdateLeitstelleMessagesBadge();
            }
        };
        _vehicleManagementViewModel.Maengelkarte.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MaengelkartePlannerViewModel.NewEntryCount))
            {
                UpdateFahrzeugverwaltungBadge();
            }
        };

        if (!profile.IsLeitstelle)
        {
            AppServices.RegisterFlushBeforeExport(CommitAllAreasBeforeExport);
        }

        if (profile.IsLeitstelle)
        {
            var localLoaded = LoadLocalWorkspaceOnStartup();
            if (localLoaded)
            {
                StatusText = BuildLocalStatusText("Lokal wiederhergestellt");
            }
            else if (profile.AutoLoadDropboxOnStartup && AppServices.Dropbox.Settings.IsConnected)
            {
                StatusText = "Lade Route-Paket von Dropbox…";
            }
            else
            {
                StatusText = "Bereit – Änderungen werden automatisch lokal gespeichert.";
            }

            if (profile.AutoLoadDropboxOnStartup && AppServices.Dropbox.Settings.IsConnected)
            {
                _ = SyncDropboxOnStartupAsync();
            }

            _leitstelleMessagesInboxViewModel.StartMonitoring();
            UpdateLeitstelleMessagesBadge();
        }
        else
        {
            StatusText = "Bereit.";
        }
    }

    /// <summary>Planer: Arbeitsstand und Dropbox-Sync nach Anmeldung im Hintergrund laden.</summary>
    public async Task InitializeAfterLoginAsync(IProgress<DropboxTransferProgress>? transferProgress = null)
    {
        if (_profile.IsLeitstelle)
        {
            return;
        }

        StatusText = "Lade Planer-Arbeitsstand…";
        PlanerWorkspaceSaveCoordinator.Reset();
        try
        {
            // Maßgeblich: planer_workspace.json lokal – routes_export.json nur manueller Fallback.
            if (await TryLoadLocalPlanerWorkspaceAsync().ConfigureAwait(true))
            {
                StatusText = BuildLocalStatusText("Planer-Arbeitsstand geladen");
            }

            var syncResult = await SyncDropboxAfterLoginAsync(transferProgress).ConfigureAwait(true);

            if (syncResult?.Imported == true)
            {
                StatusText = AppServices.Routes.HasPackage
                    ? BuildLocalStatusText("Dropbox synchronisiert")
                    : "Planer-Arbeitsstand aus Dropbox übernommen.";
                return;
            }

            if (!AppServices.Routes.HasPackage &&
                syncResult is { Imported: false, RemoteTimestamp: > 0, RemoteHasMoreContent: true })
            {
                var forced = await PlanerDropboxWorkspaceSync.TryImportFromDropboxAsync(
                        forceOverwrite: true,
                        transferProgress)
                    .ConfigureAwait(true);
                if (forced.Imported)
                {
                    OnRoutePackageLoaded();
                    _fahrerdispoViewModel.RefreshFromEditor();
                    _dienstvorlagenViewModel.RefreshFromEditor();
                    _dienstvorlagenLibraryViewModel.RefreshFromEditor();
                    StatusText = AppServices.Routes.HasPackage
                        ? BuildLocalStatusText("Dropbox übernommen (mehr Inhalt)")
                        : "Planer-Arbeitsstand aus Dropbox übernommen.";
                    _dataTransferViewModel.LastActionMessage = forced.Message;
                    return;
                }
            }

            if (!AppServices.Routes.HasPackage)
            {
                var localJson = await Task.Run(AppServices.Workspace.TryLoadPackageJson).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(localJson) && LoadLocalWorkspaceOnStartup(localJson))
                {
                    StatusText = BuildLocalStatusText("routes_export lokal geladen");
                    return;
                }
            }

            if (!AppServices.Routes.HasPackage && syncResult is not null)
            {
                StatusText = syncResult.Message;
            }
            else if (AppServices.Routes.HasPackage && syncResult is { Imported: false })
            {
                _dataTransferViewModel.LastActionMessage = syncResult.Message;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Laden fehlgeschlagen: {ex.Message}";
        }
    }

    private async Task<bool> TryLoadLocalPlanerWorkspaceAsync()
    {
        var loaded = await Task.Run(PlanerDropboxWorkspaceSync.TryApplyLocalWorkspace).ConfigureAwait(true);
        if (!loaded)
        {
            return false;
        }

        OnRoutePackageLoaded();
        return AppServices.Routes.HasPackage;
    }

    [ObservableProperty] private string productName = string.Empty;
    [ObservableProperty] private string productSubtitle = string.Empty;
    [ObservableProperty] private string dashboardHint = string.Empty;

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem? selectedNavigationItem;

    [ObservableProperty]
    private FrameworkElement? currentPage;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string appVersion = "0.3.0";

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (_suppressNavigationCommit)
        {
            ApplyNavigationSelection(value);
            return;
        }

        var leaving = _previousNavigationItem;
        if (leaving is not null && leaving != value && !TryCommitLeavingArea(leaving))
        {
            _suppressNavigationCommit = true;
            SelectedNavigationItem = leaving;
            _suppressNavigationCommit = false;
            return;
        }

        ApplyNavigationSelection(value);
    }

    private void ApplyNavigationSelection(NavigationItem? value)
    {
        _previousNavigationItem = value;

        if (value is null)
        {
            return;
        }

        CurrentPage = value.Content;
        StatusText = value.Description ?? value.Title;

        if (value.Title == "Einstellungen")
        {
            _settingsViewModel.ReloadFromStore();
        }
        else if (value.Title is "Übersicht" or "Versand")
        {
            _dataTransferViewModel.RefreshStats();
        }
        else if (value.Title == "Routen")
        {
            _routesViewModel.RefreshFromEditorIfNeeded();
        }
        else if (value.Title == "Navidaten")
        {
            _routePathEditorViewModel.RefreshRoutes();
        }
        else if (value.Title == "Personalverwaltung")
        {
            _employeesViewModel.RefreshFromEditorIfNeeded();
            UpdatePersonalverwaltungBadge();
        }
        else if (value.Title == "Fahrerdisposition")
        {
            _fahrerdispoViewModel.RefreshFromEditorIfNeeded();
        }
        else if (value.Title == "Fahrzeugdisposition")
        {
            _fahrzeugdispoViewModel.RefreshFromEditorIfNeeded();
        }
        else if (value.Title == "Haltestellen")
        {
            _stopsLibraryViewModel.RefreshFromEditorIfNeeded();
        }
        else if (value.Title == "Ansagen")
        {
            _announcementsLibraryViewModel.RefreshFromEditorIfNeeded();
        }
        else if (value.Title == "Fahrzeugverwaltung")
        {
            _vehicleManagementViewModel.RefreshFromEditorIfNeeded();
            _vehicleManagementViewModel.Maengelkarte.RefreshHint();
            UpdateFahrzeugverwaltungBadge();
        }
        else if (value.Title == "Nachrichten")
        {
            if (_profile.IsLeitstelle)
            {
                _ = _leitstelleMessagesInboxViewModel.RefreshAsync();
                _leitstelleMessagesInboxViewModel.MarkMailAsRead();
                UpdateLeitstelleMessagesBadge();
            }
            else
            {
                _messagesViewModel.RefreshFromEditorIfNeeded();
            }
        }
        else if (value.Title == "Nachricht senden")
        {
            _messageSendViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Anzeigen & Hinweise")
        {
            _displaysOperationsViewModel.RefreshFromEditorIfNeeded();
        }
        else if (value.Title == "Fahrzeuge")
        {
            _vehicleTrackingViewModel.OnViewActivated();
        }
        else if (value.Title == "Zeitwirtschaft")
        {
            _zeitwirtschaftPlannerViewModel.RefreshFromEditor();
            _zeitwirtschaftPlannerViewModel.RefreshHint();
        }
        else if (value.Title == "SEV-Schilder")
        {
            _sevSignEditorViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Dienstvorlagen")
        {
            _dienstvorlagenViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Vorlagen-Bibliothek")
        {
            _dienstvorlagenLibraryViewModel.RefreshFromEditor();
        }
        else
        {
            _vehicleTrackingViewModel.OnViewDeactivated();
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SelectedNavigationItem = NavigationItems.First(i => i.Title == "Einstellungen");
    }

    /// <summary>Speichert alle Planer-Bereiche vor Abmeldung, Programmende und Dropbox-Export.</summary>
    public void CommitAllAreasBeforeExport()
    {
        if (_profile.IsLeitstelle)
        {
            return;
        }

        _routesViewModel.CommitChangesIfDirty();
        _routePathEditorViewModel.CommitDraftIfDirty();
        _employeesViewModel.CommitChangesIfDirty();
        _stopsLibraryViewModel.CommitChangesIfDirty();
        _announcementsLibraryViewModel.CommitChangesIfDirty();
        _vehicleManagementViewModel.CommitChangesIfDirty();
        _messagesViewModel.CommitChangesIfDirty();
        _displaysOperationsViewModel.CommitChangesIfDirty();
        _fahrzeugdispoViewModel.CommitChangesIfDirty();
        _fahrerdispoViewModel.CommitChangesIfDirty();
        _dienstvorlagenViewModel.FlushBeforeExport();
        _sevSignEditorViewModel.FlushBeforeExport();
        _settingsViewModel.PersistFolderPath();
        _settingsViewModel.PersistBriefingPasswords();
    }

    private bool TryCommitLeavingArea(NavigationItem leaving)
    {
        var wasPending = HasPendingChangesForArea(leaving.Title);
        CommitPendingAreaChanges(leaving);
        if (!wasPending || !HasPendingChangesForArea(leaving.Title))
        {
            return true;
        }

        var detail = GetAreaStatusMessage(leaving.Title);
        MessageBox.Show(
            string.IsNullOrWhiteSpace(detail)
                ? "Die Änderungen konnten nicht gespeichert werden. Bitte korrigieren Sie die Eingaben oder speichern Sie manuell, bevor Sie die Seite verlassen."
                : $"{detail}\n\nBitte korrigieren Sie die Eingaben oder speichern Sie manuell, bevor Sie die Seite verlassen.",
            "Speichern fehlgeschlagen",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private bool HasPendingChangesForArea(string? title) => title switch
    {
        "Routen" => _routesViewModel.HasPendingChanges,
        "Personalverwaltung" => _employeesViewModel.HasPendingChanges,
        "Haltestellen" => _stopsLibraryViewModel.HasPendingChanges,
        "Ansagen" => _announcementsLibraryViewModel.HasPendingChanges,
        "Fahrzeugverwaltung" => _vehicleManagementViewModel.HasPendingChanges,
        "Nachrichten" when !_profile.IsLeitstelle => _messagesViewModel.HasPendingChanges,
        "Anzeigen & Hinweise" => _displaysOperationsViewModel.HasPendingChanges,
        "Fahrzeugdisposition" => _fahrzeugdispoViewModel.HasPendingChanges,
        "Fahrerdisposition" => _fahrerdispoViewModel.HasPendingChanges,
        _ => false
    };

    private string? GetAreaStatusMessage(string? title) => title switch
    {
        "Routen" => _routesViewModel.StatusMessage,
        "Personalverwaltung" => _employeesViewModel.StatusMessage,
        "Haltestellen" => _stopsLibraryViewModel.StatusMessage,
        "Ansagen" => _announcementsLibraryViewModel.StatusMessage,
        "Fahrzeugverwaltung" => _vehicleManagementViewModel.StatusMessage,
        "Nachrichten" when !_profile.IsLeitstelle => _messagesViewModel.StatusMessage,
        "Anzeigen & Hinweise" => _displaysOperationsViewModel.StatusMessage,
        "Fahrzeugdisposition" => _fahrzeugdispoViewModel.StatusMessage,
        "Fahrerdisposition" => _fahrerdispoViewModel.StatusMessage,
        _ => null
    };

    private void CommitPendingAreaChanges(NavigationItem? leaving)
    {
        if (leaving is null)
        {
            return;
        }

        switch (leaving.Title)
        {
            case "Routen":
                _routesViewModel.CommitChangesIfDirty();
                break;
            case "Navidaten":
                _routePathEditorViewModel.CommitDraftIfDirty();
                break;
            case "Personalverwaltung":
                _employeesViewModel.CommitChangesIfDirty();
                _dataTransferViewModel.RefreshDriverCredentialWarnings();
                _dataTransferViewModel.RefreshDocumentCheckWarnings();
                UpdatePersonalverwaltungBadge();
                break;
            case "Haltestellen":
                _stopsLibraryViewModel.CommitChangesIfDirty();
                break;
            case "Ansagen":
                _announcementsLibraryViewModel.CommitChangesIfDirty();
                break;
            case "Fahrzeugverwaltung":
                _vehicleManagementViewModel.CommitChangesIfDirty();
                _dataTransferViewModel.RefreshInspectionWarnings();
                break;
            case "Nachrichten":
                if (!_profile.IsLeitstelle)
                {
                    _messagesViewModel.CommitChangesIfDirty();
                }
                break;
            case "Anzeigen & Hinweise":
                _displaysOperationsViewModel.CommitChangesIfDirty();
                break;
            case "Einstellungen":
                _settingsViewModel.PersistFolderPath();
                _settingsViewModel.PersistBriefingPasswords();
                break;
            case "Fahrzeugdisposition":
                _fahrzeugdispoViewModel.CommitChangesIfDirty();
                break;
            case "Fahrerdisposition":
                _fahrerdispoViewModel.CommitChangesIfDirty();
                break;
            case "Dienstvorlagen":
                _dienstvorlagenViewModel.FlushBeforeExport();
                break;
        }
    }

    private bool LoadLocalWorkspaceOnStartup(string? packageJson = null)
    {
        var json = packageJson ?? AppServices.Workspace.TryLoadPackageJson();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            AppServices.Routes.LoadFromJson(json, persistLocally: false, source: "local-restore");
            OnRoutePackageLoaded();
            return true;
        }
        catch (Exception ex)
        {
            _dataTransferViewModel.LastActionMessage =
                $"Lokales Paket konnte nicht geladen werden: {ex.Message}";
            return false;
        }
    }

    private string BuildLocalStatusText(string prefix)
    {
        return $"{prefix} – {_dataTransferViewModel.RouteCount} Routen, {_dataTransferViewModel.StopCount} Haltestellen";
    }

    /// <summary>Planer: Nach Anmeldung planer_workspace.json mit lokalem Arbeitsstand vergleichen.</summary>
    public async Task<PlanerDropboxWorkspaceSync.ImportResult?> SyncDropboxAfterLoginAsync(
        IProgress<DropboxTransferProgress>? transferProgress = null)
    {
        if (_profile.IsLeitstelle || !AppServices.IsPlannerApp)
        {
            return null;
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            if (!AppServices.Routes.HasPackage)
            {
                StatusText = "Bereit – Dropbox unter Einstellungen verbinden oder lokal importieren.";
            }

            return null;
        }

        _dataTransferViewModel.IsBusy = true;
        try
        {
            var result = await PlanerDropboxWorkspaceSync.TryImportIfRemoteNewerAsync(transferProgress)
                .ConfigureAwait(true);
            if (result.Imported)
            {
                OnRoutePackageLoaded();
                _fahrerdispoViewModel.RefreshFromEditor();
                _dienstvorlagenViewModel.RefreshFromEditor();
                _dienstvorlagenLibraryViewModel.RefreshFromEditor();
                StatusText = AppServices.Routes.HasPackage
                    ? BuildLocalStatusText("Dropbox synchronisiert")
                    : "Planer-Arbeitsstand aus Dropbox übernommen.";
            }
            else if (AppServices.Routes.HasPackage)
            {
                StatusText = BuildLocalStatusText(result.RemoteTimestamp < result.LocalTimestamp
                    ? "Lokal (aktueller als Dropbox)"
                    : "Lokal");
            }
            else
            {
                StatusText = result.Message;
            }

            _dataTransferViewModel.LastActionMessage = result.Message;
            return result;
        }
        finally
        {
            _dataTransferViewModel.IsBusy = false;
            _dataTransferViewModel.RefreshStats();
            _dataTransferViewModel.RefreshPackageVersions();
            _sevSignEditorViewModel.RefreshFromEditor();
            await TryProcessDeviceRegistrationsFromDropboxAsync().ConfigureAwait(true);
        }
    }

    private async Task SyncDropboxOnStartupAsync()
    {
        if (!_profile.AutoLoadDropboxOnStartup)
        {
            return;
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            if (!AppServices.Routes.HasPackage)
            {
                StatusText = "Bereit – Dropbox unter Einstellungen verbinden oder lokal importieren.";
            }

            return;
        }

        _dataTransferViewModel.IsBusy = true;
        try
        {
            var localTimestamp = AppServices.Routes.Stats.Timestamp ?? 0;
            var hadLocal = AppServices.Routes.HasPackage;

            var remoteJson = await AppServices.Dropbox.DownloadRouteFileAsync().ConfigureAwait(true);
            var remoteTimestamp = LocalWorkspaceStore.ExtractPackageTimestamp(remoteJson);

            if (!hadLocal || remoteTimestamp > localTimestamp)
            {
                AppServices.Routes.LoadFromJson(remoteJson, persistLocally: true, source: "dropbox-startup");
                OnRoutePackageLoaded();
                StatusText = BuildLocalStatusText("Dropbox synchronisiert");
                _dataTransferViewModel.LastActionMessage =
                    $"Dropbox-Stand übernommen ({AppServices.Dropbox.GetRouteFilePath()}).";
            }
            else
            {
                StatusText = BuildLocalStatusText("Lokal (aktueller als Dropbox)");
                _dataTransferViewModel.LastActionMessage = "Lokaler Arbeitsstand ist neuer – Dropbox unverändert.";
            }
        }
        catch (Exception ex)
        {
            if (AppServices.Routes.HasPackage)
            {
                StatusText = BuildLocalStatusText("Lokal (Dropbox-Sync fehlgeschlagen)");
                _dataTransferViewModel.LastActionMessage = $"Dropbox-Sync: {ex.Message}";
            }
            else
            {
                StatusText = $"Dropbox-Laden fehlgeschlagen: {ex.Message}";
                _dataTransferViewModel.LastActionMessage = StatusText;
            }
        }
        finally
        {
            _dataTransferViewModel.IsBusy = false;
            _dataTransferViewModel.RefreshStats();
            await TryProcessDeviceRegistrationsFromDropboxAsync().ConfigureAwait(true);

            if (_profile.IsLeitstelle && AppServices.Routes.HasPackage)
            {
                var standResult = await LeitstelleStandDropboxSync.TryMergeFromDropboxAsync().ConfigureAwait(true);
                if (standResult.Imported)
                {
                    OnRoutePackageLoaded();
                    _dataTransferViewModel.LastActionMessage = string.IsNullOrWhiteSpace(_dataTransferViewModel.LastActionMessage)
                        ? standResult.Message
                        : $"{_dataTransferViewModel.LastActionMessage} {standResult.Message}";
                }
            }
        }
    }

    private void OnNavigateToVehicleManagementRequested(string? phoneNormalizedDigitsOnly)
    {
        var navItem = NavigationItems.FirstOrDefault(i => i.Title == "Fahrzeugverwaltung");
        if (navItem is null)
        {
            return;
        }

        SelectedNavigationItem = navItem;
        _vehicleManagementViewModel.TrySelectVehicleByNormalizedPhone(phoneNormalizedDigitsOnly);
    }

    private void OnNavigateToEmployeeManagementRequested(string? personnelNumberNormalized)
    {
        var navItem = NavigationItems.FirstOrDefault(i => i.Title == "Personalverwaltung");
        if (navItem is null)
        {
            return;
        }

        SelectedNavigationItem = navItem;
        _employeesViewModel.TrySelectEmployeeByPersonnelNumber(personnelNumberNormalized);
    }

    private void OnNavigateToEmployeeManagementFromDispoRequested(string? personnelNumberNormalized, string? driverKey)
    {
        var navItem = NavigationItems.FirstOrDefault(i => i.Title == "Personalverwaltung");
        if (navItem is null)
        {
            return;
        }

        SelectedNavigationItem = navItem;
        _employeesViewModel.RefreshFromEditorIfNeeded();
        _employeesViewModel.TrySelectEmployeeByPersonnelNumber(personnelNumberNormalized);
        if (_employeesViewModel.SelectedEmployee is null)
        {
            _employeesViewModel.TrySelectEmployeeByDispoKey(driverKey);
        }
    }

    private void OnRoutePackageLoaded()
    {
        _ = TryProcessDeviceRegistrationsFromDropboxAsync();
        _dataTransferViewModel.RefreshStats();
        _dataTransferViewModel.RefreshPackageVersions();
        _routesViewModel.RefreshFromEditor();
        _routePathEditorViewModel.RefreshRoutes();
        _employeesViewModel.RefreshFromEditor();
        _stopsLibraryViewModel.RefreshFromEditor();
        _announcementsLibraryViewModel.RefreshFromEditor();
        _vehicleManagementViewModel.RefreshFromEditor();
        _messagesViewModel.RefreshFromEditor();
        _displaysOperationsViewModel.RefreshFromEditor();
        _sevSignEditorViewModel.RefreshFromEditor();
        _dienstvorlagenViewModel.RefreshFromEditor();
        _dienstvorlagenLibraryViewModel.RefreshFromEditor();
        _fahrzeugdispoViewModel.RefreshFromEditor();
        _fahrerdispoViewModel.RefreshFromEditor();
        if (_profile.IsLeitstelle)
        {
            _messageSendViewModel.RefreshFromEditor();
            _leitstelleMessagesInboxViewModel.RefreshFromEditor();
            _ = _leitstelleMessagesInboxViewModel.RefreshAsync();
            UpdateLeitstelleMessagesBadge();
        }

        UpdatePersonalverwaltungBadge();
    }

    private async Task TryProcessDeviceRegistrationsFromDropboxAsync()
    {
        if (!AppServices.IsPlannerApp || AppServices.DeviceRegistration is null)
        {
            return;
        }

        try
        {
            var result = await AppServices.DeviceRegistration.TryProcessPendingAsync().ConfigureAwait(true);
            if (result.AnyAdded)
            {
                _vehicleManagementViewModel.RefreshFromEditor();
                _dataTransferViewModel.RefreshStats();
                _dataTransferViewModel.LastActionMessage =
                    "Geräte registriert: " + string.Join(", ", result.AddedVehicles);
            }
        }
        catch
        {
            // optional
        }
    }

    private IReadOnlyList<NavigationItem> CreateNavigationItems()
    {
        _fahrzeugverwaltungNavItem = new NavigationItem
        {
            Title = "Fahrzeugverwaltung",
            Icon = PackIconKind.CellphoneLink,
            Description = "KOM-Fahrzeuge und Mängelkarte (registeredVehicles / maengelkarte.json)",
            CreateContent = () => new VehicleManagementView { DataContext = _vehicleManagementViewModel }
        };
        _personalverwaltungNavItem = new NavigationItem
        {
            Title = "Personalverwaltung",
            Icon = PackIconKind.AccountGroup,
            Description = "Mitarbeiterregister (employeeRoster)",
            CreateContent = () => new EmployeesView { DataContext = _employeesViewModel }
        };

        var items = new List<NavigationItem>
        {
            new()
            {
                Title = "Übersicht",
                Icon = PackIconKind.ViewDashboard,
                Description = "Dashboard, Import und Export",
                CreateContent = () => new DashboardView { DataContext = _dataTransferViewModel }
            },
            _personalverwaltungNavItem,
            new()
            {
                Title = _profile.IsLeitstelle ? "Nachricht senden" : "Nachrichten",
                Icon = PackIconKind.MessageText,
                Description = _profile.IsLeitstelle
                    ? "Vorlagen wählen und per Dropbox an Fahrzeuge senden (zbl_message)"
                    : "KOM- und Mail-Vorlagen (messageTemplates / mailTemplates)",
                CreateContent = () => _profile.IsLeitstelle
                    ? new MessageSendView { DataContext = _messageSendViewModel }
                    : new MessagesView { DataContext = _messagesViewModel }
            },
            new()
            {
                Title = "Versand",
                Icon = PackIconKind.Send,
                Description = "JSON Import/Export und Dropbox",
                CreateContent = () => new DataTransferView { DataContext = _dataTransferViewModel }
            },
            new()
            {
                Title = "Einstellungen",
                Icon = PackIconKind.Cog,
                Description = "Dropbox, Ordnerpfad, Verbindungstest",
                CreateContent = () => new SettingsView { DataContext = _settingsViewModel }
            }
        };

        if (!_profile.IsLeitstelle)
        {
            var personalIdx = items.FindIndex(i => i.Title == "Personalverwaltung");
            if (personalIdx >= 0)
            {
                items.Insert(personalIdx + 1, new NavigationItem
                {
                    Title = "Fahrerdisposition",
                    Icon = PackIconKind.CalendarAccount,
                    Description = "Fahrer den Linien und Fahrten zuordnen",
                    CreateContent = () => new FahrerdispoView { DataContext = _fahrerdispoViewModel }
                });
                items.Insert(personalIdx + 2, new NavigationItem
                {
                    Title = "Fahrzeugdisposition",
                    Icon = PackIconKind.BusMultiple,
                    Description = "Fahrzeuge den Linien und Fahrten zuordnen",
                    CreateContent = () => new FahrzeugdispoView { DataContext = _fahrzeugdispoViewModel }
                });
                items.Insert(personalIdx + 3, _fahrzeugverwaltungNavItem);
                items.Insert(personalIdx + 4, new NavigationItem
                {
                    Title = "Dienstvorlagen",
                    Icon = PackIconKind.CalendarClock,
                    Description = "Dienstschablonen erstellen, aus Fahrplan importieren und als PDF exportieren",
                    CreateContent = () => new DienstvorlagenView { DataContext = _dienstvorlagenViewModel }
                });
                items.Insert(personalIdx + 5, new NavigationItem
                {
                    Title = "Vorlagen-Bibliothek",
                    Icon = PackIconKind.BookOpenPageVariant,
                    Description = "Gespeicherte Dienstvorlagen anzeigen und als PDF exportieren (301, 302, …)",
                    CreateContent = () => new DienstvorlagenLibraryView { DataContext = _dienstvorlagenLibraryViewModel }
                });
            }

            items.Insert(1, new NavigationItem
            {
                Title = "Routen",
                Icon = PackIconKind.SignDirection,
                Description = "Routen und Haltestellen bearbeiten",
                CreateContent = () => new RoutesView { DataContext = _routesViewModel }
            });
            items.Insert(2, new NavigationItem
            {
                Title = "Haltestellen",
                Icon = PackIconKind.BusMarker,
                Description = "Haltestellenbibliothek und Vorlagen (managedStopTemplates)",
                CreateContent = () => new StopsLibraryView { DataContext = _stopsLibraryViewModel }
            });
            items.Insert(3, new NavigationItem
            {
                Title = "Ansagen",
                Icon = PackIconKind.VolumeHigh,
                Description = "Nur Ansagen: 4-stellige ID, Ton, ★ Sonder mit „S“",
                CreateContent = () => new AnnouncementsLibraryView { DataContext = _announcementsLibraryViewModel }
            });
            items.Insert(4, new NavigationItem
            {
                Title = "Navidaten",
                Icon = PackIconKind.MapMarkerPath,
                Description = "Fahrweg auf Karte planen (Handy-kompatibel)",
                CreateContent = () => new RoutePathEditorView { DataContext = _routePathEditorViewModel }
            });
            items.Insert(items.Count - 2, new NavigationItem
            {
                Title = "Anzeigen & Hinweise",
                Icon = PackIconKind.Billboard,
                Description = "Zielliste und datumgesteuerte Hinweise",
                CreateContent = () => new DisplaysOperationsView { DataContext = _displaysOperationsViewModel }
            });
            items.Insert(items.Count - 2, new NavigationItem
            {
                Title = "SEV-Schilder",
                Icon = PackIconKind.FilePdfBox,
                Description = "NRW-SEV-Schild A3 quer als PDF (Linie, Ziel, Haltestellen, Betreiber)",
                CreateContent = () => new SevSignEditorView { DataContext = _sevSignEditorViewModel }
            });
            items.Insert(items.Count - 2, new NavigationItem
            {
                Title = "Zeitwirtschaft",
                Icon = PackIconKind.ClockOutline,
                Description = "Zeitstempel aus Tablets zusammenführen (Dropbox JSON)",
                CreateContent = () => new ZeitwirtschaftPlannerView { DataContext = _zeitwirtschaftPlannerViewModel }
            });
        }

        if (_profile.IsLeitstelle)
        {
            var personalIdx = items.FindIndex(i => i.Title == "Personalverwaltung");
            if (personalIdx >= 0)
            {
                items.Insert(personalIdx + 1, _fahrzeugverwaltungNavItem);
            }

            _leitstelleMessagesNavItem = new NavigationItem
            {
                Title = "Nachrichten",
                Icon = PackIconKind.MessageBadge,
                Description = "MailChat / SOS aus Dropbox (SOS → Karte)",
                CreateContent = () => new LeitstelleMessagesInboxView { DataContext = _leitstelleMessagesInboxViewModel }
            };
            items.Insert(3, _leitstelleMessagesNavItem);
            items.Insert(1, new NavigationItem
            {
                Title = "Fahrzeuge",
                Icon = PackIconKind.MapMarkerRadius,
                Description = "Live-Karte – Wagennummer und Linie/Kurs aus Dropbox",
                CreateContent = () => new VehicleTrackingView { DataContext = _vehicleTrackingViewModel }
            });
        }

        return items;
    }

    private void OnLeitstelleSosAlertRaised(string phoneNormalized)
    {
        if (!_profile.IsLeitstelle || string.IsNullOrWhiteSpace(phoneNormalized))
        {
            return;
        }

        var fahrzeugeNav = NavigationItems.FirstOrDefault(i => i.Title == "Fahrzeuge");
        if (fahrzeugeNav is null)
        {
            return;
        }

        if (SelectedNavigationItem != fahrzeugeNav)
        {
            return;
        }

        _vehicleTrackingViewModel.ShowVehicleDetailForPhone(phoneNormalized);
    }

    private void UpdateLeitstelleMessagesBadge()
    {
        if (!_profile.IsLeitstelle || _leitstelleMessagesNavItem is null)
        {
            return;
        }

        _leitstelleMessagesNavItem.BadgeText =
            _leitstelleMessagesInboxViewModel.HasUnreadMail ? "1" : string.Empty;
    }

    private void UpdateFahrzeugverwaltungBadge()
    {
        if (_profile.IsLeitstelle || _fahrzeugverwaltungNavItem is null)
        {
            return;
        }

        var count = _vehicleManagementViewModel.Maengelkarte.NewEntryCount;
        _fahrzeugverwaltungNavItem.BadgeText = count > 0 ? $"+{count}" : string.Empty;
    }

    private void UpdatePersonalverwaltungBadge()
    {
        if (_personalverwaltungNavItem is null)
        {
            return;
        }

        var employees = AppServices.Routes.Editor?.Employees;
        var count = employees is null
            ? 0
            : EmployeeDocumentCheckWarningEvaluator.CountDueChecks(employees);
        _personalverwaltungNavItem.BadgeText = count > 0 ? $"+{count}" : string.Empty;
    }

    private static string ResolveDisplayedAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(MainViewModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var cut = informational.IndexOf('+', StringComparison.Ordinal);
            return cut > 0 ? informational[..cut] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "0.3.0" : version.ToString(3);
    }
}
