using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using SmartOepnv.AppShared.Kom;
using SmartOepnv.AppShared.Models;
using SmartOepnv.AppShared.Views;
using SmartOepnv.AppShared.Voip;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SmartOepnvAppProfile _profile;
    private readonly DataTransferViewModel _dataTransferViewModel;
    private readonly SettingsViewModel _settingsViewModel = new();
    private RoutesViewModel? _routesViewModel;
    private RoutePathEditorViewModel? _routePathEditorViewModel;
    private readonly EmployeesViewModel _employeesViewModel = new();
    private StopsLibraryViewModel? _stopsLibraryViewModel;
    private AnnouncementsLibraryViewModel? _announcementsLibraryViewModel;
    private readonly VehicleManagementViewModel _vehicleManagementViewModel = new();
    private MessagesViewModel? _messagesViewModel;
    private readonly MessageSendViewModel _messageSendViewModel = new();
    private readonly LeitstelleMessagesInboxViewModel _leitstelleMessagesInboxViewModel = new();
    private DisplaysOperationsViewModel? _displaysOperationsViewModel;
    private readonly VehicleTrackingViewModel _vehicleTrackingViewModel = new();
    private ZeitwirtschaftPlannerViewModel? _zeitwirtschaftPlannerViewModel;
    private SevSignEditorViewModel? _sevSignEditorViewModel;
    private FahrerdispoViewModel? _fahrerdispoViewModel;
    private FahrzeugdispoViewModel? _fahrzeugdispoViewModel;
    private DienstvorlagenViewModel? _dienstvorlagenViewModel;
    private DienstvorlagenLibraryViewModel? _dienstvorlagenLibraryViewModel;
    private MitteilungViewModel? _mitteilungViewModel;

    private RoutesViewModel RoutesViewModel => _routesViewModel ??= new();
    private RoutePathEditorViewModel RoutePathEditorViewModel => _routePathEditorViewModel ??= new();
    private StopsLibraryViewModel StopsLibraryViewModel => _stopsLibraryViewModel ??= new();
    private AnnouncementsLibraryViewModel AnnouncementsLibraryViewModel => _announcementsLibraryViewModel ??= new();
    private MessagesViewModel MessagesViewModel => _messagesViewModel ??= new();
    private DisplaysOperationsViewModel DisplaysOperationsViewModel => _displaysOperationsViewModel ??= new();
    private ZeitwirtschaftPlannerViewModel ZeitwirtschaftPlannerViewModel => _zeitwirtschaftPlannerViewModel ??= new();
    private SevSignEditorViewModel SevSignEditorViewModel => _sevSignEditorViewModel ??= new();
    private FahrerdispoViewModel FahrerdispoViewModel => _fahrerdispoViewModel ??= new();
    private FahrzeugdispoViewModel FahrzeugdispoViewModel => _fahrzeugdispoViewModel ??= new();
    private DienstvorlagenViewModel DienstvorlagenViewModel => _dienstvorlagenViewModel ??= new();
    private DienstvorlagenLibraryViewModel DienstvorlagenLibraryViewModel => _dienstvorlagenLibraryViewModel ??= new();
    private MitteilungViewModel MitteilungViewModel => _mitteilungViewModel ??= new();

    private NavigationItem? _previousNavigationItem;
    private bool _suppressNavigationCommit;
    private NavigationItem? _leitstelleMessagesNavItem;
    private NavigationItem? _fahrzeugverwaltungNavItem;
    private NavigationItem? _personalverwaltungNavItem;

    private static readonly TimeSpan LeitstelleDropboxSyncInterval = TimeSpan.FromMinutes(15);
    private DispatcherTimer? _leitstelleDropboxSyncTimer;
    private int _leitstelleDropboxSyncRunning;
    private bool _voipPortAutoFixAttempted;

    public VoipLeitstelleHost VoipHost { get; } = new();

    public VehicleTrackingViewModel VehicleTracking => _vehicleTrackingViewModel;

    public MainViewModel(SmartOepnvAppProfile profile)
    {
        _profile = profile;
        ProductName = profile.ProductName;
        ProductSubtitle = profile.ProductSubtitle;
        DashboardHint = profile.DashboardHint;
        AppVersion = ResolveDisplayedAppVersion();
        _dataTransferViewModel = new DataTransferViewModel(profile);

        NavigationMenu = new ObservableCollection<object>();
        NavigationItems = new ObservableCollection<NavigationItem>();
        BuildNavigation();
        SelectedNavigationItem = NavigationItems[0];
        _previousNavigationItem = SelectedNavigationItem;
        if (!profile.IsLeitstelle)
        {
            CurrentPage = SelectedNavigationItem.Content;
        }

        _dataTransferViewModel.RoutePackageImported += OnRoutePackageLoaded;
        _dataTransferViewModel.NavigateToVehicleManagementRequested += OnNavigateToVehicleManagementRequested;
        _dataTransferViewModel.NavigateToEmployeeManagementRequested += OnNavigateToEmployeeManagementRequested;
        if (!profile.IsLeitstelle)
        {
            FahrerdispoViewModel.NavigateToEmployeeManagementRequested += OnNavigateToEmployeeManagementFromDispoRequested;
        }
        _leitstelleMessagesInboxViewModel.SosAlertRaised += OnLeitstelleSosAlertRaised;
        _leitstelleMessagesInboxViewModel.OpenVehicleOnMapRequested += OnLeitstelleOpenVehicleOnMapRequested;
        _leitstelleMessagesInboxViewModel.SprechwunschAnswerRequested += OnLeitstelleSprechwunschAnswerRequested;
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

        IsLeitstelleApp = profile.IsLeitstelle;

        if (!profile.IsLeitstelle)
        {
            AppServices.RegisterFlushBeforeExport(CommitAllAreasBeforeExport);
        }

        if (profile.IsLeitstelle)
        {
            StatusText = "Starte…";
            VoipHost.CallStatusChanged += OnVoipCallStatusChanged;
            _settingsViewModel.VoipHost = VoipHost;
            _settingsViewModel.DropboxConnectionEstablished += (_, _) =>
            {
                _ = SyncLeitstelleFromDropboxAsync(isBackground: true);
                _ = StartVoipHostSafeAsync();
            };
        }
        else
        {
            StatusText = "Bereit.";
        }
    }

    /// <summary>Leitstelle: schwere Initialisierung nach Fensteranzeige (schnellerer Programmstart).</summary>
    public async Task InitializeLeitstelleAfterShowAsync()
    {
        if (!_profile.IsLeitstelle)
        {
            return;
        }

        try
        {
            var localJson = await Task.Run(AppServices.Workspace.TryLoadPackageJson).ConfigureAwait(true);
            var localLoaded = !string.IsNullOrWhiteSpace(localJson) && LoadLocalWorkspaceOnStartup(localJson);

            CurrentPage = SelectedNavigationItem?.Content;

            if (_profile.AutoLoadDropboxOnStartup && AppServices.Dropbox.Settings.IsConnected)
            {
                StatusText = localLoaded
                    ? BuildLocalStatusText("Lokal – synchronisiere Dropbox…")
                    : "Lade Daten von Dropbox…";
                _ = SyncLeitstelleFromDropboxAsync(isBackground: true);
            }
            else if (localLoaded)
            {
                StatusText = BuildLocalStatusText("Lokal wiederhergestellt");
            }
            else
            {
                StatusText = "Bereit – Änderungen werden automatisch lokal gespeichert.";
            }

            StartLeitstelleDropboxPeriodicSync();
            _leitstelleMessagesInboxViewModel.StartMonitoring();
            UpdateLeitstelleMessagesBadge();
            _ = StartVoipHostSafeAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Start fehlgeschlagen: {ex.Message}";
            CurrentPage ??= SelectedNavigationItem?.Content;
        }
    }

    /// <summary>Planer: Arbeitsstand und Dropbox-Sync nach Anmeldung im Hintergrund laden.</summary>
    public async Task InitializeAfterLoginAsync(IProgress<DropboxTransferProgress>? transferProgress = null)
    {
        if (_profile.IsLeitstelle)
        {
            return;
        }

        StatusText = "Lade Planer-Arbeitsstand aus Dropbox…";
        PlanerWorkspaceSaveCoordinator.Reset();
        try
        {
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
                    FahrerdispoViewModel.RefreshFromEditor();
                    DienstvorlagenViewModel.RefreshFromEditor();
                    DienstvorlagenLibraryViewModel.RefreshFromEditor();
                    StatusText = AppServices.Routes.HasPackage
                        ? BuildLocalStatusText("Dropbox übernommen (mehr Inhalt)")
                        : "Planer-Arbeitsstand aus Dropbox übernommen.";
                    _dataTransferViewModel.LastActionMessage = forced.Message;
                    return;
                }
            }

            if (!AppServices.Routes.HasPackage && await TryLoadLocalPlanerWorkspaceAsync().ConfigureAwait(true))
            {
                StatusText = BuildLocalStatusText("Planer-Arbeitsstand lokal geladen");
                return;
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

            // Neuer Betrieb / leerer Workspace: leeres Paket, damit Routen & Haltestellen angelegt werden können.
            if (!AppServices.Routes.HasPackage && AppServices.Routes.EnsureEmptyPackageIfNeeded("empty-betrieb"))
            {
                OnRoutePackageLoaded();
                StatusText = "Leeres Route-Paket angelegt – Routen und Haltestellen können jetzt erstellt werden.";
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

    public ObservableCollection<object> NavigationMenu { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem? selectedNavigationItem;

    [ObservableProperty]
    private FrameworkElement? currentPage;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string appVersion = "0.3.0";

    public bool IsLeitstelleApp { get; }

    public LeitstelleMessagesInboxViewModel LeitstelleMessagesInbox => _leitstelleMessagesInboxViewModel;

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = ReferenceEquals(item, value);
        }

        foreach (var entry in NavigationMenu.OfType<NavigationGroup>())
        {
            entry.SyncSelection(value);
        }

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

    [RelayCommand]
    private void SelectNavigation(NavigationItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedNavigationItem = item;
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
        else if (value.Title is "\u00DCbersicht" or "Übersicht" or "Versand")
        {
            _dataTransferViewModel.RefreshStats();
        }
        else if (value.Title == "Routen")
        {
            RoutesViewModel.RefreshFromEditorIfNeeded();
        }
        else if (value.Title == "Navidaten")
        {
            RoutePathEditorViewModel.RefreshRoutes();
        }
        else if (value.Title == "Personalverwaltung")
        {
            _employeesViewModel.RefreshFromEditorIfNeeded();
            UpdatePersonalverwaltungBadge();
        }
        else if (value.Title == "Fahrerdisposition")
        {
            ScheduleDispositionRefresh(FahrerdispoViewModel.RefreshFromEditorIfNeeded);
        }
        else if (value.Title == "Fahrzeugdisposition")
        {
            ScheduleDispositionRefresh(FahrzeugdispoViewModel.RefreshFromEditorIfNeeded);
        }
        else if (value.Title == "Haltestellen")
        {
            StopsLibraryViewModel.RefreshFromEditorIfNeeded();
        }
        else if (value.Title == "Ansagen")
        {
            AnnouncementsLibraryViewModel.RefreshFromEditorIfNeeded();
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
                MessagesViewModel.RefreshFromEditorIfNeeded();
            }
        }
        else if (value.Title == "Nachricht senden")
        {
            _messageSendViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Anzeigen & Hinweise")
        {
            ScheduleDispositionRefresh(DisplaysOperationsViewModel.RefreshFromEditorIfNeeded);
        }
        else if (value.Title == "Fahrzeuge")
        {
            _vehicleTrackingViewModel.OnViewActivated();
        }
        else if (value.Title == "Zeitwirtschaft")
        {
            ZeitwirtschaftPlannerViewModel.RefreshFromEditor();
            ZeitwirtschaftPlannerViewModel.RefreshHint();
        }
        else if (value.Title == "SEV-Schilder")
        {
            SevSignEditorViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Dienstvorlagen")
        {
            DienstvorlagenViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Vorlagen-Bibliothek")
        {
            DienstvorlagenLibraryViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Mitteilung")
        {
            MitteilungViewModel.RefreshFromEditor();
        }
        else
        {
            _vehicleTrackingViewModel.OnViewDeactivated();
        }
    }

    private static void ScheduleDispositionRefresh(Action refresh)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            refresh();
            return;
        }

        dispatcher.BeginInvoke(refresh, DispatcherPriority.Loaded);
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

        RoutesViewModel.CommitChangesIfDirty();
        RoutePathEditorViewModel.CommitDraftIfDirty();
        _employeesViewModel.CommitChangesIfDirty();
        StopsLibraryViewModel.CommitChangesIfDirty();
        AnnouncementsLibraryViewModel.CommitChangesIfDirty();
        _vehicleManagementViewModel.CommitChangesIfDirty();
        MessagesViewModel.CommitChangesIfDirty();
        DisplaysOperationsViewModel.CommitChangesIfDirty();
        FahrzeugdispoViewModel.CommitChangesIfDirty();
        FahrerdispoViewModel.CommitChangesIfDirty();
        DienstvorlagenViewModel.FlushBeforeExport();
        SevSignEditorViewModel.FlushBeforeExport();
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
        "Routen" => RoutesViewModel.HasPendingChanges,
        "Personalverwaltung" => _employeesViewModel.HasPendingChanges,
        "Haltestellen" => StopsLibraryViewModel.HasPendingChanges,
        "Ansagen" => AnnouncementsLibraryViewModel.HasPendingChanges,
        "Fahrzeugverwaltung" => _vehicleManagementViewModel.HasPendingChanges,
        "Nachrichten" when !_profile.IsLeitstelle => MessagesViewModel.HasPendingChanges,
        "Anzeigen & Hinweise" => DisplaysOperationsViewModel.HasPendingChanges,
        "Fahrzeugdisposition" => FahrzeugdispoViewModel.HasPendingChanges,
        "Fahrerdisposition" => FahrerdispoViewModel.HasPendingChanges,
        _ => false
    };

    private string? GetAreaStatusMessage(string? title) => title switch
    {
        "Routen" => RoutesViewModel.StatusMessage,
        "Personalverwaltung" => _employeesViewModel.StatusMessage,
        "Haltestellen" => StopsLibraryViewModel.StatusMessage,
        "Ansagen" => AnnouncementsLibraryViewModel.StatusMessage,
        "Fahrzeugverwaltung" => _vehicleManagementViewModel.StatusMessage,
        "Nachrichten" when !_profile.IsLeitstelle => MessagesViewModel.StatusMessage,
        "Anzeigen & Hinweise" => DisplaysOperationsViewModel.StatusMessage,
        "Fahrzeugdisposition" => FahrzeugdispoViewModel.StatusMessage,
        "Fahrerdisposition" => FahrerdispoViewModel.StatusMessage,
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
                RoutesViewModel.CommitChangesIfDirty();
                break;
            case "Navidaten":
                RoutePathEditorViewModel.CommitDraftIfDirty();
                break;
            case "Personalverwaltung":
                _employeesViewModel.CommitChangesIfDirty();
                _dataTransferViewModel.RefreshDriverCredentialWarnings();
                _dataTransferViewModel.RefreshDocumentCheckWarnings();
                UpdatePersonalverwaltungBadge();
                break;
            case "Haltestellen":
                StopsLibraryViewModel.CommitChangesIfDirty();
                break;
            case "Ansagen":
                AnnouncementsLibraryViewModel.CommitChangesIfDirty();
                break;
            case "Fahrzeugverwaltung":
                _vehicleManagementViewModel.CommitChangesIfDirty();
                _dataTransferViewModel.RefreshInspectionWarnings();
                break;
            case "Nachrichten":
                if (!_profile.IsLeitstelle)
                {
                    MessagesViewModel.CommitChangesIfDirty();
                }
                break;
            case "Anzeigen & Hinweise":
                DisplaysOperationsViewModel.CommitChangesIfDirty();
                break;
            case "Einstellungen":
                _settingsViewModel.PersistFolderPath();
                _settingsViewModel.PersistBriefingPasswords();
                break;
            case "Fahrzeugdisposition":
                FahrzeugdispoViewModel.CommitChangesIfDirty();
                break;
            case "Fahrerdisposition":
                FahrerdispoViewModel.CommitChangesIfDirty();
                break;
            case "Dienstvorlagen":
                DienstvorlagenViewModel.FlushBeforeExport();
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
            var result = await PlanerDropboxWorkspaceSync.TryImportFromDropboxAsync(
                    forceOverwrite: true,
                    transferProgress)
                .ConfigureAwait(true);
            if (result.Imported)
            {
                OnRoutePackageLoaded();
                FahrerdispoViewModel.RefreshFromEditor();
                DienstvorlagenViewModel.RefreshFromEditor();
                DienstvorlagenLibraryViewModel.RefreshFromEditor();
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
            SevSignEditorViewModel.RefreshFromEditor();
            await TryProcessDeviceRegistrationsFromDropboxAsync().ConfigureAwait(true);
        }
    }

    private void StartLeitstelleDropboxPeriodicSync()
    {
        _leitstelleDropboxSyncTimer = new DispatcherTimer
        {
            Interval = LeitstelleDropboxSyncInterval
        };
        _leitstelleDropboxSyncTimer.Tick += OnLeitstelleDropboxPeriodicSyncTick;
        _leitstelleDropboxSyncTimer.Start();
    }

    private async void OnLeitstelleDropboxPeriodicSyncTick(object? sender, EventArgs e)
    {
        await SyncLeitstelleFromDropboxAsync(isBackground: true).ConfigureAwait(true);
    }

    private async Task SyncLeitstelleFromDropboxAsync(bool isBackground)
    {
        if (!_profile.IsLeitstelle)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _leitstelleDropboxSyncRunning, 1, 0) != 0)
        {
            return;
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            if (!isBackground && !AppServices.Routes.HasPackage)
            {
                StatusText = "Bereit – Dropbox unter Einstellungen verbinden oder lokal importieren.";
            }

            Interlocked.Exchange(ref _leitstelleDropboxSyncRunning, 0);
            return;
        }

        if (!isBackground)
        {
            _dataTransferViewModel.IsBusy = true;
        }

        try
        {
            var localTimestamp = AppServices.Routes.Stats.Timestamp ?? 0;
            var hadLocal = AppServices.Routes.HasPackage;
            var importedRoutePackage = false;

            var remoteJson = await AppServices.Dropbox.DownloadRouteFileAsync().ConfigureAwait(true);
            var remoteTimestamp = LocalWorkspaceStore.ExtractPackageTimestamp(remoteJson);

            if (!hadLocal || remoteTimestamp > localTimestamp)
            {
                AppServices.Routes.LoadFromJson(remoteJson, persistLocally: true, source: "dropbox-startup");
                OnRoutePackageLoaded();
                importedRoutePackage = true;

                if (isBackground)
                {
                    _dataTransferViewModel.LastActionMessage =
                        $"Dropbox-Hintergrundsync ({DateTime.Now:HH:mm}): Route-Paket übernommen.";
                }
                else
                {
                    StatusText = BuildLocalStatusText("Dropbox synchronisiert");
                    _dataTransferViewModel.LastActionMessage =
                        $"Dropbox-Stand übernommen ({AppServices.Dropbox.GetRouteFilePath()}).";
                }
            }
            else if (!isBackground)
            {
                StatusText = BuildLocalStatusText("Lokal (aktueller als Dropbox)");
                _dataTransferViewModel.LastActionMessage = "Lokaler Arbeitsstand ist neuer – Dropbox unverändert.";
            }

            await TryProcessDeviceRegistrationsFromDropboxAsync().ConfigureAwait(true);

            if (AppServices.Routes.HasPackage)
            {
                var standResult = await LeitstelleStandDropboxSync.TryMergeFromDropboxAsync().ConfigureAwait(true);
                if (standResult.Imported)
                {
                    OnRoutePackageLoaded();
                    importedRoutePackage = true;
                    var prefix = isBackground ? $"Dropbox-Hintergrundsync ({DateTime.Now:HH:mm})" : "Dropbox-Stand";
                    _dataTransferViewModel.LastActionMessage = string.IsNullOrWhiteSpace(_dataTransferViewModel.LastActionMessage)
                        ? $"{prefix}: {standResult.Message}"
                        : $"{_dataTransferViewModel.LastActionMessage} {standResult.Message}";
                }
            }

            var liteResult = await LiteRouteUpdateDropboxSync.TryMergeFromDropboxAsync().ConfigureAwait(true);
            if (liteResult.Imported)
            {
                OnRoutePackageLoaded();
                importedRoutePackage = true;
                var prefix = isBackground ? $"Dropbox-Hintergrundsync ({DateTime.Now:HH:mm})" : "Dropbox-Stand";
                _dataTransferViewModel.LastActionMessage = string.IsNullOrWhiteSpace(_dataTransferViewModel.LastActionMessage)
                    ? $"{prefix}: {liteResult.Message}"
                    : $"{_dataTransferViewModel.LastActionMessage} {liteResult.Message}";
            }

            if (isBackground && importedRoutePackage)
            {
                StatusText = BuildLocalStatusText("Dropbox synchronisiert");
            }

            if (isBackground)
            {
                _ = _leitstelleMessagesInboxViewModel.RefreshAsync();
            }

            if (AppServices.Dropbox.Settings.IsConnected)
            {
                await VoipHost.PublishConfigsAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            if (isBackground)
            {
                _dataTransferViewModel.LastActionMessage = $"Dropbox-Hintergrundsync fehlgeschlagen: {ex.Message}";
            }
            else if (AppServices.Routes.HasPackage)
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
            if (!isBackground)
            {
                _dataTransferViewModel.IsBusy = false;
            }

            _dataTransferViewModel.RefreshStats();
            Interlocked.Exchange(ref _leitstelleDropboxSyncRunning, 0);
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
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(OnRoutePackageLoaded);
            return;
        }

        _ = TryProcessDeviceRegistrationsFromDropboxAsync();
        _dataTransferViewModel.RefreshStats();
        _employeesViewModel.RefreshFromEditor();
        _vehicleManagementViewModel.RefreshFromEditor();
        if (!_profile.IsLeitstelle)
        {
            _dataTransferViewModel.RefreshPackageVersions();
            RoutesViewModel.RefreshFromEditor();
            RoutePathEditorViewModel.RefreshRoutes();
            StopsLibraryViewModel.RefreshFromEditor();
            AnnouncementsLibraryViewModel.RefreshFromEditor();
            MessagesViewModel.RefreshFromEditor();
            DisplaysOperationsViewModel.RefreshFromEditor();
            SevSignEditorViewModel.RefreshFromEditor();
            DienstvorlagenViewModel.RefreshFromEditor();
            DienstvorlagenLibraryViewModel.RefreshFromEditor();
            FahrzeugdispoViewModel.RefreshFromEditor();
            FahrerdispoViewModel.RefreshFromEditor();
        }
        else
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

    private void BuildNavigation()
    {
        NavigationMenu.Clear();
        NavigationItems.Clear();

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

        void AddLeaf(NavigationItem item)
        {
            NavigationMenu.Add(item);
            NavigationItems.Add(item);
        }

        void AddGroup(NavigationGroup group)
        {
            NavigationMenu.Add(group);
            foreach (var child in group.Children)
            {
                NavigationItems.Add(child);
            }
        }

        AddLeaf(new NavigationItem
        {
            Title = "\u00DCbersicht",
            Icon = PackIconKind.ViewDashboard,
            Description = "Dashboard, Import und Export",
            CreateContent = () => new DashboardView { DataContext = _dataTransferViewModel }
        });

        if (_profile.IsLeitstelle)
        {
            AddLeaf(new NavigationItem
            {
                Title = "Fahrzeuge",
                Icon = PackIconKind.MapMarkerRadius,
                Description = "Live-Karte – Wagennummer und Linie/Kurs aus Dropbox",
                CreateContent = () => new VehicleTrackingView { DataContext = _vehicleTrackingViewModel }
            });
            AddLeaf(_personalverwaltungNavItem);
            AddLeaf(_fahrzeugverwaltungNavItem);
            _leitstelleMessagesNavItem = new NavigationItem
            {
                Title = "Nachrichten",
                Icon = PackIconKind.MessageBadge,
                Description = "MailChat / SOS aus Dropbox (SOS → Karte)",
                CreateContent = () => new LeitstelleMessagesInboxView { DataContext = _leitstelleMessagesInboxViewModel }
            };
            AddLeaf(_leitstelleMessagesNavItem);
            AddLeaf(new NavigationItem
            {
                Title = "Nachricht senden",
                Icon = PackIconKind.MessageText,
                Description = "Vorlagen wählen und per Dropbox an Fahrzeuge senden (zbl_message)",
                CreateContent = () => new MessageSendView { DataContext = _messageSendViewModel }
            });
        }
        else
        {
            var fahrerdispo = new NavigationItem
            {
                Title = "Fahrerdisposition",
                Icon = PackIconKind.CalendarAccount,
                Description = "Fahrer den Linien und Fahrten zuordnen",
                CreateContent = () => new FahrerdispoView { DataContext = FahrerdispoViewModel }
            };
            var fahrzeugdispo = new NavigationItem
            {
                Title = "Fahrzeugdisposition",
                Icon = PackIconKind.BusMultiple,
                Description = "Fahrzeuge den Linien und Fahrten zuordnen",
                CreateContent = () => new FahrzeugdispoView { DataContext = FahrzeugdispoViewModel }
            };
            var routen = new NavigationItem
            {
                Title = "Routen",
                Icon = PackIconKind.SignDirection,
                Description = "Routen und Haltestellen bearbeiten",
                CreateContent = () => new RoutesView { DataContext = RoutesViewModel }
            };
            var haltestellen = new NavigationItem
            {
                Title = "Haltestellen",
                Icon = PackIconKind.BusMarker,
                Description = "Haltestellenbibliothek und Vorlagen (managedStopTemplates)",
                CreateContent = () => new StopsLibraryView { DataContext = StopsLibraryViewModel }
            };
            var ansagen = new NavigationItem
            {
                Title = "Ansagen",
                Icon = PackIconKind.VolumeHigh,
                Description = "Nur Ansagen: 4-stellige ID, Ton, ★ Sonder mit „S“",
                CreateContent = () => new AnnouncementsLibraryView { DataContext = AnnouncementsLibraryViewModel }
            };
            var navidaten = new NavigationItem
            {
                Title = "Navidaten",
                Icon = PackIconKind.MapMarkerPath,
                Description = "Fahrweg auf Karte planen (Handy-kompatibel)",
                CreateContent = () => new RoutePathEditorView { DataContext = RoutePathEditorViewModel }
            };
            var anzeigen = new NavigationItem
            {
                Title = "Anzeigen & Hinweise",
                Icon = PackIconKind.Billboard,
                Description = "Zielliste und datumgesteuerte Hinweise",
                CreateContent = () => new DisplaysOperationsView { DataContext = DisplaysOperationsViewModel }
            };
            var nachrichten = new NavigationItem
            {
                Title = "Nachrichten",
                Icon = PackIconKind.MessageText,
                Description = "KOM- und Mail-Vorlagen (messageTemplates / mailTemplates)",
                CreateContent = () => new MessagesView { DataContext = MessagesViewModel }
            };
            var zeitwirtschaft = new NavigationItem
            {
                Title = "Zeitwirtschaft",
                Icon = PackIconKind.ClockOutline,
                Description = "Zeitstempel aus Tablets zusammenführen (Dropbox JSON)",
                CreateContent = () => new ZeitwirtschaftPlannerView { DataContext = ZeitwirtschaftPlannerViewModel }
            };
            var dienstvorlagen = new NavigationItem
            {
                Title = "Dienstvorlagen",
                Icon = PackIconKind.CalendarClock,
                Description = "Dienstschablonen erstellen, aus Fahrplan importieren und als PDF exportieren",
                CreateContent = () => new DienstvorlagenView { DataContext = DienstvorlagenViewModel }
            };
            var vorlagenBibliothek = new NavigationItem
            {
                Title = "Vorlagen-Bibliothek",
                Icon = PackIconKind.BookOpenPageVariant,
                Description = "Gespeicherte Dienstvorlagen anzeigen und als PDF exportieren (301, 302, …)",
                CreateContent = () => new DienstvorlagenLibraryView { DataContext = DienstvorlagenLibraryViewModel }
            };

            AddGroup(NavigationGroup.Create(
                "ITCS",
                PackIconKind.TransitConnectionVariant,
                routen,
                haltestellen,
                ansagen,
                navidaten,
                anzeigen,
                nachrichten));

            AddGroup(NavigationGroup.Create(
                "Personal",
                PackIconKind.AccountGroup,
                _personalverwaltungNavItem,
                fahrerdispo,
                zeitwirtschaft));

            AddGroup(NavigationGroup.Create(
                "Fahrzeug",
                PackIconKind.Bus,
                fahrzeugdispo,
                _fahrzeugverwaltungNavItem));

            AddGroup(NavigationGroup.Create(
                "Dienstvorlagen",
                PackIconKind.CalendarClock,
                dienstvorlagen,
                vorlagenBibliothek));

            AddLeaf(new NavigationItem
            {
                Title = "Mitteilung",
                Icon = PackIconKind.FileDocumentEditOutline,
                Description = "Mitteilung als PDF erstellen (Gültigkeit, Logos, Unterschrift)",
                CreateContent = () => new MitteilungView { DataContext = MitteilungViewModel }
            });
            AddLeaf(new NavigationItem
            {
                Title = "SEV-Schilder",
                Icon = PackIconKind.FilePdfBox,
                Description = "NRW-SEV-Schild A3 quer als PDF (Linie, Ziel, Haltestellen, Betreiber)",
                CreateContent = () => new SevSignEditorView { DataContext = SevSignEditorViewModel }
            });
        }

        AddLeaf(new NavigationItem
        {
            Title = "Versand",
            Icon = PackIconKind.Send,
            Description = "JSON Import/Export und Dropbox",
            CreateContent = () => new DataTransferView { DataContext = _dataTransferViewModel }
        });
        AddLeaf(new NavigationItem
        {
            Title = "Einstellungen",
            Icon = PackIconKind.Cog,
            Description = "Dropbox, Ordnerpfad, Verbindungstest",
            CreateContent = () => new SettingsView { DataContext = _settingsViewModel }
        });
    }

    private void OnLeitstelleSosAlertRaised(string phoneNormalized) =>
        OpenLeitstelleVehicleLiveMap(phoneNormalized);

    private void OnLeitstelleOpenVehicleOnMapRequested(string phoneNormalized) =>
        OpenLeitstelleVehicleLiveMap(phoneNormalized);

    private void OnLeitstelleSprechwunschAnswerRequested(string phoneNormalized, string displayName) =>
        _ = StartSprechwunschFunkCallSafeAsync(phoneNormalized, displayName);

    private void OnVoipCallStatusChanged()
    {
        var callStatus = VoipHost.CallStatus;
        if (string.IsNullOrWhiteSpace(callStatus.StatusText))
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            StatusText = callStatus.StatusText;
        });
    }

    private async Task StartSprechwunschFunkCallSafeAsync(string phoneNormalized, string displayName)
    {
        if (!_profile.IsLeitstelle || string.IsNullOrWhiteSpace(phoneNormalized))
        {
            return;
        }

        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                OpenVoipFunkDialog(phoneNormalized, displayName));
            await VoipHost.CallVehicleAsync(phoneNormalized, displayName).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Funk (Sprechwunsch) fehlgeschlagen: {ex.Message}";
        }
    }

    private void OpenVoipFunkDialog(string phoneNormalized, string displayName)
    {
        var vehicle = _vehicleTrackingViewModel.TryGetVehicleByPhone(phoneNormalized)
            ?? VehicleListItemViewModel.ForVoip(phoneNormalized, displayName);
        var owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            return;
        }

        new VoipFunkDialog(
            vehicle,
            VoipHost,
            owner,
            phone => _vehicleTrackingViewModel.TryGetVehicleByPhone(phone)?.DisplayName).Show();
    }

    private void OpenLeitstelleVehicleLiveMap(string? phoneNormalized)
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

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (SelectedNavigationItem != fahrzeugeNav)
            {
                SelectedNavigationItem = fahrzeugeNav;
            }

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _vehicleTrackingViewModel.ShowVehicleDetailForPhone(phoneNormalized);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }, System.Windows.Threading.DispatcherPriority.Normal);
    }

    private void UpdateLeitstelleMessagesBadge()
    {
        if (!_profile.IsLeitstelle || _leitstelleMessagesNavItem is null)
        {
            return;
        }

        var count = _leitstelleMessagesInboxViewModel.UnreadMailCount;
        _leitstelleMessagesNavItem.BadgeText = count > 0 ? $"+{count}" : string.Empty;
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

    private async Task StartVoipHostSafeAsync()
    {
        if (!_profile.IsLeitstelle)
        {
            return;
        }

        try
        {
            await EnsureVoipPortAndStartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            VoipHost.Signaling.Stop();
            System.Diagnostics.Debug.WriteLine($"VoIP-Start: {ex}");
        }
    }

    private async Task EnsureVoipPortAndStartAsync()
    {
        await VoipHost.EnsurePortAndStartAsync().ConfigureAwait(true);
        if (VoipHost.Signaling.IsRunning)
        {
            VoipWindowsPortSetup.MarkSetupCompleted(VoipHost.Settings);
            return;
        }

        if (VoipWindowsPortSetup.LooksLikeAccessDenied(VoipHost.StatusMessage) ||
            VoipWindowsPortSetup.IsPortReservationMissing(VoipHost.Settings))
        {
            if (_voipPortAutoFixAttempted)
            {
                return;
            }

            _voipPortAutoFixAttempted = true;
            StatusText = "VoIP: Port wird automatisch freigegeben – bitte Windows-Administrator mit „Ja“ bestätigen…";
            var progress = new Progress<string>(msg => StatusText = msg);
            await VoipWindowsPortSetup.TryEnsurePortReadyAsync(VoipHost.Settings, progress).ConfigureAwait(true);
            await VoipHost.EnsurePortAndStartAsync().ConfigureAwait(true);
            if (VoipHost.Signaling.IsRunning)
            {
                VoipWindowsPortSetup.MarkSetupCompleted(VoipHost.Settings);
            }
        }
    }

    public void ShutdownVoip()
    {
        if (!_profile.IsLeitstelle)
        {
            return;
        }

        VoipHost.Dispose();
    }
}
