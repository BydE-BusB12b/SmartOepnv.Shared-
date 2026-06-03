using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using SmartOepnv.AppShared.Models;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
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

    private NavigationItem? _previousNavigationItem;
    private NavigationItem? _leitstelleMessagesNavItem;

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
        _leitstelleMessagesInboxViewModel.SosAlertRaised += OnLeitstelleSosAlertRaised;
        _leitstelleMessagesInboxViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LeitstelleMessagesInboxViewModel.UnreadMailCount) or
                nameof(LeitstelleMessagesInboxViewModel.HasUnreadMail))
            {
                UpdateLeitstelleMessagesBadge();
            }
        };

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

        if (_profile.IsLeitstelle)
        {
            _leitstelleMessagesInboxViewModel.StartMonitoring();
            UpdateLeitstelleMessagesBadge();
        }
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
        CommitPendingAreaChanges(_previousNavigationItem);
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
            _routesViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Navidaten")
        {
            _routePathEditorViewModel.RefreshRoutes();
        }
        else if (value.Title == "Fahrer")
        {
            _employeesViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Haltestellen")
        {
            _stopsLibraryViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Ansagen")
        {
            _announcementsLibraryViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Fahrzeugverwaltung")
        {
            _vehicleManagementViewModel.RefreshFromEditor();
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
                _messagesViewModel.RefreshFromEditor();
            }
        }
        else if (value.Title == "Nachricht senden")
        {
            _messageSendViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Anzeigen & Hinweise")
        {
            _displaysOperationsViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Fahrzeuge")
        {
            _vehicleTrackingViewModel.OnViewActivated();
        }
        else if (value.Title == "Zeitwirtschaft")
        {
            _zeitwirtschaftPlannerViewModel.RefreshHint();
        }
        else if (value.Title == "SEV-Schilder")
        {
            _sevSignEditorViewModel.RefreshFromEditor();
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

    private void CommitPendingAreaChanges(NavigationItem? leaving)
    {
        if (leaving is null)
        {
            return;
        }

        switch (leaving.Title)
        {
            case "Routen":
                _routesViewModel.CommitChanges();
                break;
            case "Navidaten":
                _routePathEditorViewModel.CommitDraftToWorkspace();
                break;
            case "Fahrer":
                _employeesViewModel.CommitChanges();
                _dataTransferViewModel.RefreshDriverCredentialWarnings();
                break;
            case "Haltestellen":
                _stopsLibraryViewModel.CommitChanges();
                break;
            case "Ansagen":
                _announcementsLibraryViewModel.CommitChanges();
                break;
            case "Fahrzeugverwaltung":
                _vehicleManagementViewModel.CommitChanges();
                _dataTransferViewModel.RefreshInspectionWarnings();
                break;
            case "Nachrichten":
                if (!_profile.IsLeitstelle)
                {
                    _messagesViewModel.CommitChanges();
                }
                break;
            case "Anzeigen & Hinweise":
                _displaysOperationsViewModel.CommitChanges();
                break;
        }
    }

    private bool LoadLocalWorkspaceOnStartup()
    {
        var json = AppServices.Workspace.TryLoadPackageJson();
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
        var navItem = NavigationItems.FirstOrDefault(i => i.Title == "Fahrer");
        if (navItem is null)
        {
            return;
        }

        SelectedNavigationItem = navItem;
        _employeesViewModel.TrySelectEmployeeByPersonnelNumber(personnelNumberNormalized);
    }

    private void OnRoutePackageLoaded()
    {
        _ = TryProcessDeviceRegistrationsFromDropboxAsync();
        _dataTransferViewModel.RefreshStats();
        _routesViewModel.RefreshFromEditor();
        _routePathEditorViewModel.RefreshRoutes();
        _employeesViewModel.RefreshFromEditor();
        _stopsLibraryViewModel.RefreshFromEditor();
        _announcementsLibraryViewModel.RefreshFromEditor();
        _vehicleManagementViewModel.RefreshFromEditor();
        _messagesViewModel.RefreshFromEditor();
        _displaysOperationsViewModel.RefreshFromEditor();
        _sevSignEditorViewModel.RefreshFromEditor();
        if (_profile.IsLeitstelle)
        {
            _messageSendViewModel.RefreshFromEditor();
            _leitstelleMessagesInboxViewModel.RefreshFromEditor();
            _ = _leitstelleMessagesInboxViewModel.RefreshAsync();
            UpdateLeitstelleMessagesBadge();
        }
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
        var dashboard = new DashboardView { DataContext = _dataTransferViewModel };
        var versand = new DataTransferView { DataContext = _dataTransferViewModel };
        var settings = new SettingsView { DataContext = _settingsViewModel };
        var routes = new RoutesView { DataContext = _routesViewModel };
        var employees = new EmployeesView { DataContext = _employeesViewModel };
        var stopsLibrary = new StopsLibraryView { DataContext = _stopsLibraryViewModel };
        var announcementsLibrary = new AnnouncementsLibraryView { DataContext = _announcementsLibraryViewModel };
        var vehicleManagement = new VehicleManagementView { DataContext = _vehicleManagementViewModel };
        var messages = new MessagesView { DataContext = _messagesViewModel };
        var messageSend = new MessageSendView { DataContext = _messageSendViewModel };
        var leitstelleMessages = new LeitstelleMessagesInboxView { DataContext = _leitstelleMessagesInboxViewModel };
        var displaysOperations = new DisplaysOperationsView { DataContext = _displaysOperationsViewModel };
        var zeitwirtschaft = new ZeitwirtschaftPlannerView { DataContext = _zeitwirtschaftPlannerViewModel };
        var sevSignEditor = new SevSignEditorView { DataContext = _sevSignEditorViewModel };

        var items = new List<NavigationItem>
        {
            new()
            {
                Title = "Übersicht",
                Icon = PackIconKind.ViewDashboard,
                Description = "Dashboard, Import und Export",
                Content = dashboard
            },
            new()
            {
                Title = "Fahrer",
                Icon = PackIconKind.AccountGroup,
                Description = "Mitarbeiterregister (employeeRoster)",
                Content = employees
            },
            new()
            {
                Title = "Fahrzeugverwaltung",
                Icon = PackIconKind.CellphoneLink,
                Description = "KOM-Fahrzeuge (registeredVehicles) für Routenversand",
                Content = vehicleManagement
            },
            new()
            {
                Title = _profile.IsLeitstelle ? "Nachricht senden" : "Nachrichten",
                Icon = PackIconKind.MessageText,
                Description = _profile.IsLeitstelle
                    ? "Vorlagen wählen und per Dropbox an Fahrzeuge senden (zbl_message)"
                    : "KOM- und Mail-Vorlagen (messageTemplates / mailTemplates)",
                Content = _profile.IsLeitstelle ? messageSend : messages
            },
            new()
            {
                Title = "Versand",
                Icon = PackIconKind.Send,
                Description = "JSON Import/Export und Dropbox",
                Content = versand
            },
            new()
            {
                Title = "Einstellungen",
                Icon = PackIconKind.Cog,
                Description = "Dropbox, Ordnerpfad, Verbindungstest",
                Content = settings
            }
        };

        if (!_profile.IsLeitstelle)
        {
            items.Insert(1, new NavigationItem
            {
                Title = "Routen",
                Icon = PackIconKind.SignDirection,
                Description = "Routen und Haltestellen bearbeiten",
                Content = routes
            });
            items.Insert(2, new NavigationItem
            {
                Title = "Haltestellen",
                Icon = PackIconKind.BusMarker,
                Description = "Haltestellenbibliothek und Vorlagen (managedStopTemplates)",
                Content = stopsLibrary
            });
            items.Insert(3, new NavigationItem
            {
                Title = "Ansagen",
                Icon = PackIconKind.VolumeHigh,
                Description = "Nur Ansagen: 4-stellige ID, Ton, ★ Sonder mit „S“",
                Content = announcementsLibrary
            });
            items.Insert(4, new NavigationItem
            {
                Title = "Navidaten",
                Icon = PackIconKind.MapMarkerPath,
                Description = "Fahrweg auf Karte planen (Handy-kompatibel)",
                Content = new RoutePathEditorView { DataContext = _routePathEditorViewModel }
            });
            items.Insert(items.Count - 2, new NavigationItem
            {
                Title = "Anzeigen & Hinweise",
                Icon = PackIconKind.Billboard,
                Description = "Zielliste und datumgesteuerte Hinweise",
                Content = displaysOperations
            });
            items.Insert(items.Count - 2, new NavigationItem
            {
                Title = "SEV-Schilder",
                Icon = PackIconKind.FilePdfBox,
                Description = "NRW-SEV-Schild A3 quer als PDF (Linie, Ziel, Haltestellen, Betreiber)",
                Content = sevSignEditor
            });
            items.Insert(items.Count - 2, new NavigationItem
            {
                Title = "Zeitwirtschaft",
                Icon = PackIconKind.ClockOutline,
                Description = "Zeitstempel aus Tablets zusammenführen (Dropbox JSON)",
                Content = zeitwirtschaft
            });
        }

        if (_profile.IsLeitstelle)
        {
            _leitstelleMessagesNavItem = new NavigationItem
            {
                Title = "Nachrichten",
                Icon = PackIconKind.MessageBadge,
                Description = "MailChat / SOS aus Dropbox (SOS → Karte)",
                Content = leitstelleMessages
            };
            items.Insert(3, _leitstelleMessagesNavItem);
            items.Insert(1, new NavigationItem
            {
                Title = "Fahrzeuge",
                Icon = PackIconKind.MapMarkerRadius,
                Description = "Live-Karte – Wagennummer und Linie/Kurs aus Dropbox",
                Content = new VehicleTrackingView { DataContext = _vehicleTrackingViewModel }
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
            SelectedNavigationItem = fahrzeugeNav;
        }

        _vehicleTrackingViewModel.FocusVehicleByPhone(phoneNormalized);
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
