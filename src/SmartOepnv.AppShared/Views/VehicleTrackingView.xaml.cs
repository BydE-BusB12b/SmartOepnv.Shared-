using System.ComponentModel;
using System.IO;

using System.Text.Json.Nodes;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Threading;

using Microsoft.Web.WebView2.Core;

using SmartOepnv.AppShared.Kom;

using SmartOepnv.AppShared.ViewModels;

using SmartOepnv.Core;

using SmartOepnv.Core.VehicleTracking;



namespace SmartOepnv.AppShared.Views;



public partial class VehicleTrackingView : UserControl

{

    private VehicleTrackingViewModel? _viewModel;

    private bool _mapReady;

    private bool _pageLoaded;

    private string? _pendingMapJson;

    private VehicleListItemViewModel? _detailVehicle;



    public VehicleTrackingView()

    {

        InitializeComponent();

        Loaded += OnLoaded;

        Unloaded += OnUnloaded;

        SizeChanged += OnSizeChanged;

        MapHost.SizeChanged += OnMapHostSizeChanged;

        IsVisibleChanged += OnIsVisibleChanged;

        DataContextChanged += OnDataContextChanged;

    }



    private void VehicleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VehicleDetailOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.AddedItems.Count > 0 && e.RemovedItems.Count > 0)
        {
            CloseVehicleDetail();
        }
    }

    private void VehicleList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetVehicleFromListEvent(sender, e, out var vehicle))
        {
            return;
        }

        e.Handled = true;

        if (_viewModel is not null && !ReferenceEquals(_viewModel.SelectedVehicle, vehicle))
        {
            _viewModel.SelectedVehicle = vehicle;
        }

        // Kartenfokus zuerst, Detail-Overlay danach – vermeidet Freeze beim Fahrzeugwechsel.
        Dispatcher.BeginInvoke(() => OpenVehicleDetail(vehicle), DispatcherPriority.Background);
    }



    private void VehicleList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)

    {

        if (!TryGetVehicleFromListEvent(sender, e, out var vehicle))

        {

            return;

        }



        if (_viewModel is not null)

        {

            _viewModel.SelectedVehicle = vehicle;

        }



        var owner = System.Windows.Window.GetWindow(this);

        if (owner is not null)

        {

            new VehicleRemoteActionsDialog(vehicle, owner).ShowDialog();

        }



        e.Handled = true;
    }

    private void VehicleDetailClose_Click(object sender, RoutedEventArgs e) => CloseVehicleDetail();

    private static bool TryGetVehicleFromListEvent(
        object sender,
        MouseEventArgs e,
        out VehicleListItemViewModel vehicle)
    {
        vehicle = null!;
        if (sender is not ListBox listBox)
        {
            return false;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return false;
        }

        if (listBox.ContainerFromElement(source) is not ListBoxItem item)
        {
            return false;
        }

        if (item.Content is not VehicleListItemViewModel vm)
        {
            return false;
        }

        vehicle = vm;
        return true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject

    {

        while (current is not null)

        {

            if (current is T match)

            {

                return match;

            }



            current = GetParentSafe(current);

        }



        return null;

    }

    private static DependencyObject? GetParentSafe(DependencyObject current) =>
        current switch
        {
            Visual => VisualTreeHelper.GetParent(current),
            FrameworkContentElement fce => fce.Parent as DependencyObject,
            _ => LogicalTreeHelper.GetParent(current)
        };



    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)

    {

        if (_viewModel is not null)

        {

            _viewModel.PushVehiclesToMapRequested -= OnPushVehiclesToMap;

            _viewModel.FocusVehicleOnMapRequested -= OnFocusVehicleOnMap;

            _viewModel.HighlightRouteOnMapRequested -= OnHighlightRouteOnMap;

            _viewModel.ShowVehicleDetailRequested -= OnShowVehicleDetailRequested;

            _viewModel.OnViewDeactivated();

        }



        _viewModel = e.NewValue as VehicleTrackingViewModel;

        if (_viewModel is not null)

        {

            _viewModel.PushVehiclesToMapRequested += OnPushVehiclesToMap;

            _viewModel.FocusVehicleOnMapRequested += OnFocusVehicleOnMap;

            _viewModel.HighlightRouteOnMapRequested += OnHighlightRouteOnMap;

            _viewModel.ShowVehicleDetailRequested += OnShowVehicleDetailRequested;

            _viewModel.OnViewActivated();

        }

    }



    private void OnShowVehicleDetailRequested(VehicleListItemViewModel vehicle) =>
        Dispatcher.BeginInvoke(() => OpenVehicleDetail(vehicle), DispatcherPriority.Background);

    private void OpenVehicleDetail(VehicleListItemViewModel vehicle)
    {
        try
        {
            UnsubscribeDetailVehicle();
            _detailVehicle = vehicle;
            _detailVehicle.PropertyChanged += OnDetailVehiclePropertyChanged;
            RefreshVehicleDetailContent(vehicle);
            VehicleDetailOverlay.Visibility = Visibility.Visible;
            VehicleDetailCloseButton.Focus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Fahrzeug-Detail konnte nicht geöffnet werden: {ex}");
        }
    }

    private void RefreshVehicleDetailContent(VehicleListItemViewModel vehicle)
    {
        VehicleDetailTitle.Text = VehicleDetailContentBuilder.BuildTitle(vehicle);
        VehicleDetailContentBuilder.Populate(VehicleDetailContent, vehicle);
    }

    private void OnDetailVehiclePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VehicleListItemViewModel vehicle
            || !ReferenceEquals(vehicle, _detailVehicle)
            || VehicleDetailOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.PropertyName is nameof(VehicleListItemViewModel.StreetAddress)
            or nameof(VehicleListItemViewModel.StreetDisplay))
        {
            RefreshVehicleDetailContent(vehicle);
        }
    }

    private void UnsubscribeDetailVehicle()
    {
        if (_detailVehicle is null)
        {
            return;
        }

        _detailVehicle.PropertyChanged -= OnDetailVehiclePropertyChanged;
        _detailVehicle = null;
    }

    private void CloseVehicleDetail()
    {
        UnsubscribeDetailVehicle();
        VehicleDetailOverlay.Visibility = Visibility.Collapsed;
        VehicleDetailContent.Children.Clear();
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        CloseVehicleDetail();
        _viewModel?.OnViewDeactivated();
    }



    private void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)

    {

        if (_mapReady && e.NewSize.Height > 0 && !double.IsPositiveInfinity(e.NewSize.Height))

        {

            _ = InvalidateMapSizeAsync();

        }

    }



    private void OnMapHostSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)

    {

        if (_mapReady && e.NewSize.Height > 0 && !double.IsPositiveInfinity(e.NewSize.Height))

        {

            _ = InvalidateMapSizeAsync();

        }

    }



    private void OnIsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)

    {

        if (!IsVisible || !_mapReady)

        {

            return;

        }



        _viewModel?.OnMapReady();

        if (_pageLoaded)

        {

            _ = InvalidateMapSizeAsync();

        }

    }



    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)

    {

        if (MapWebView.CoreWebView2 is not null)

        {

            return;

        }



        var userDataFolder = AppPaths.GetWebView2UserDataDirectory(AppServices.SettingsSubfolder);

        Directory.CreateDirectory(userDataFolder);

        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(userDataFolder, "VehicleMap"));

        await MapWebView.EnsureCoreWebView2Async(env);



        var core = MapWebView.CoreWebView2!;

        core.Settings.IsWebMessageEnabled = true;

        core.WebMessageReceived += OnWebMessageReceived;

        core.NavigationCompleted += OnNavigationCompleted;

        var mapPath = Path.Combine(AppContext.BaseDirectory, "Assets", "vehicle_tracking_map.html");

        MapWebView.Source = new Uri(mapPath);

    }



    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)

    {

        if (!e.IsSuccess || MapWebView.CoreWebView2 is null)

        {

            return;

        }



        _pageLoaded = true;

        _mapReady = true;

        SendSavedMapViewToMap();

        _ = FlushPendingMapDataAsync();

    }



    private void OnPushVehiclesToMap(string json)

    {

        if (string.IsNullOrWhiteSpace(json))

        {

            return;

        }



        _pendingMapJson = json;

        if (!_mapReady || !_pageLoaded || MapWebView.CoreWebView2 is null)

        {

            return;

        }



        _ = FlushPendingMapDataAsync();

    }



    private async Task FlushPendingMapDataAsync()

    {

        if (MapWebView.CoreWebView2 is null || !_pageLoaded || string.IsNullOrWhiteSpace(_pendingMapJson))

        {

            return;

        }



        var json = _pendingMapJson;

        _pendingMapJson = null;

        await PushMapJsonAsync(json);

        await InvalidateMapSizeAsync();

    }



    private Task PushMapJsonAsync(string json)

    {

        var core = MapWebView.CoreWebView2 ?? throw new InvalidOperationException("Karte nicht bereit.");

        var trimmed = json.Trim();

        if (!trimmed.StartsWith('{'))

        {

            throw new InvalidOperationException("Ungültige Fahrzeugdaten für die Karte.");

        }



        var envelope = new JsonObject

        {

            ["type"] = "loadVehicles",

            ["payload"] = JsonNode.Parse(trimmed)

        };



        var savedView = _viewModel?.LoadSavedMapViewForMap();

        if (savedView is not null)

        {

            envelope["mapView"] = new JsonObject

            {

                ["lat"] = savedView.Lat,

                ["lon"] = savedView.Lon,

                ["zoom"] = savedView.Zoom

            };

        }



        core.PostWebMessageAsJson(envelope.ToJsonString());

        return Task.CompletedTask;

    }



    private void SendSavedMapViewToMap()

    {

        var core = MapWebView.CoreWebView2;

        var savedView = _viewModel?.LoadSavedMapViewForMap();

        if (core is null || savedView is null)

        {

            return;

        }



        var envelope = new JsonObject

        {

            ["type"] = "setMapView",

            ["mapView"] = new JsonObject

            {

                ["lat"] = savedView.Lat,

                ["lon"] = savedView.Lon,

                ["zoom"] = savedView.Zoom

            }

        };

        core.PostWebMessageAsJson(envelope.ToJsonString());

    }



    private async Task InvalidateMapSizeAsync()

    {

        if (!_mapReady || MapWebView.CoreWebView2 is null)

        {

            return;

        }



        try

        {

            await MapWebView.CoreWebView2.ExecuteScriptAsync(

                "if (window.invalidateMapSize) window.invalidateMapSize();");

        }

        catch

        {

            // ignore resize errors

        }

    }



    private void OnFocusVehicleOnMap(string id)

    {

        if (!_mapReady || MapWebView.CoreWebView2 is null || string.IsNullOrWhiteSpace(id))

        {

            return;

        }



        try

        {

            var envelope = new JsonObject

            {

                ["type"] = "focusVehicle",

                ["id"] = id

            };

            MapWebView.CoreWebView2.PostWebMessageAsJson(envelope.ToJsonString());

        }

        catch

        {

            // WebView kann beim Tab-Wechsel kurz nicht erreichbar sein

        }

    }



    private void OnHighlightRouteOnMap(string? routeKey)

    {

        if (!_mapReady || MapWebView.CoreWebView2 is null)

        {

            return;

        }



        try

        {

            var envelope = new JsonObject

            {

                ["type"] = "highlightRoute",

                ["routeKey"] = routeKey ?? string.Empty

            };

            MapWebView.CoreWebView2.PostWebMessageAsJson(envelope.ToJsonString());

        }

        catch

        {

            // ignore transient WebView errors

        }

    }



    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string messageJson;
        try
        {
            messageJson = e.WebMessageAsJson;
        }
        catch
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => HandleWebMessage(messageJson));
            return;
        }

        HandleWebMessage(messageJson);
    }

    private void HandleWebMessage(string messageJson)
    {
        try
        {
            var root = JsonNode.Parse(messageJson)?.AsObject();

            if (root is null)
            {
                return;
            }

            switch (root["type"]?.GetValue<string>())
            {
                case "ready":
                    _mapReady = true;
                    _viewModel?.OnMapReady();
                    if (_pendingMapJson is not null)
                    {
                        _ = FlushPendingMapDataAsync();
                    }
                    break;

                case "vehicleSelected":
                    _viewModel?.SelectVehicleFromMap(root["id"]?.GetValue<string>());
                    break;

                case "mapViewChanged":
                    _viewModel?.OnMapViewChangedFromMap(
                        root["lat"]?.GetValue<double>() ?? double.NaN,
                        root["lon"]?.GetValue<double>() ?? double.NaN,
                        root["zoom"]?.GetValue<double>() ?? double.NaN);
                    break;
            }
        }
        catch
        {
            // ignore malformed map messages
        }
    }

}


