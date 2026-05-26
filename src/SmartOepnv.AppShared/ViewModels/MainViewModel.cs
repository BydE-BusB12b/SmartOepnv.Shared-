using System.Collections.ObjectModel;
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
    private readonly DataTransferViewModel _dataTransferViewModel = new();
    private readonly SettingsViewModel _settingsViewModel = new();
    private readonly RoutesViewModel _routesViewModel = new();
    private readonly RoutePathEditorViewModel _routePathEditorViewModel = new();
    private readonly EmployeesViewModel _employeesViewModel = new();
    private readonly StopsLibraryViewModel _stopsLibraryViewModel = new();
    private readonly AnnouncementsLibraryViewModel _announcementsLibraryViewModel = new();
    private readonly VehicleManagementViewModel _vehicleManagementViewModel = new();
    private readonly MessagesViewModel _messagesViewModel = new();
    private readonly DisplaysOperationsViewModel _displaysOperationsViewModel = new();
    private readonly VehicleTrackingViewModel _vehicleTrackingViewModel = new();

    private NavigationItem? _previousNavigationItem;

    public MainViewModel(SmartOepnvAppProfile profile)
    {
        _profile = profile;
        ProductName = profile.ProductName;
        ProductSubtitle = profile.ProductSubtitle;
        DashboardHint = profile.DashboardHint;

        NavigationItems = new ObservableCollection<NavigationItem>(CreateNavigationItems());
        SelectedNavigationItem = NavigationItems[0];
        CurrentPage = SelectedNavigationItem.Content;
        _previousNavigationItem = SelectedNavigationItem;

        _dataTransferViewModel.RoutePackageImported += OnRoutePackageLoaded;
        _dataTransferViewModel.NavigateToVehicleManagementRequested += OnNavigateToVehicleManagementRequested;
        _dataTransferViewModel.NavigateToEmployeeManagementRequested += OnNavigateToEmployeeManagementRequested;

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
            _messagesViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Anzeigen & Hinweise")
        {
            _displaysOperationsViewModel.RefreshFromEditor();
        }
        else if (value.Title == "Fahrzeuge")
        {
            _vehicleTrackingViewModel.OnViewActivated();
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
                _messagesViewModel.CommitChanges();
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
        _dataTransferViewModel.RefreshStats();
        _routesViewModel.RefreshFromEditor();
        _routePathEditorViewModel.RefreshRoutes();
        _employeesViewModel.RefreshFromEditor();
        _stopsLibraryViewModel.RefreshFromEditor();
        _announcementsLibraryViewModel.RefreshFromEditor();
        _vehicleManagementViewModel.RefreshFromEditor();
        _messagesViewModel.RefreshFromEditor();
        _displaysOperationsViewModel.RefreshFromEditor();
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
        var displaysOperations = new DisplaysOperationsView { DataContext = _displaysOperationsViewModel };

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
                Title = "Routen",
                Icon = PackIconKind.SignDirection,
                Description = "Routen und Haltestellen bearbeiten",
                Content = routes
            },
            new()
            {
                Title = "Haltestellen",
                Icon = PackIconKind.BusMarker,
                Description = "Haltestellenbibliothek und Vorlagen (managedStopTemplates)",
                Content = stopsLibrary
            },
            new()
            {
                Title = "Ansagen",
                Icon = PackIconKind.VolumeHigh,
                Description = "Haltestellen-Kartei mit 5-stelliger ID – Ansagen pro Haltestelle",
                Content = announcementsLibrary
            },
            new()
            {
                Title = "Navidaten",
                Icon = PackIconKind.MapMarkerPath,
                Description = "Fahrweg auf Karte planen (Handy-kompatibel)",
                Content = new RoutePathEditorView { DataContext = _routePathEditorViewModel }
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
                Title = "Nachrichten",
                Icon = PackIconKind.MessageText,
                Description = "KOM- und Mail-Vorlagen (messageTemplates / mailTemplates)",
                Content = messages
            },
            new()
            {
                Title = "Anzeigen & Hinweise",
                Icon = PackIconKind.Billboard,
                Description = "Zielliste und datumgesteuerte Hinweise",
                Content = displaysOperations
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

        if (_profile.IsLeitstelle)
        {
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
}
