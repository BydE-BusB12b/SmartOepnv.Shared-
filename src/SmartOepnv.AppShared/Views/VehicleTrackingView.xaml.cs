using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
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

    public VehicleTrackingView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PushVehiclesToMapRequested -= OnPushVehiclesToMap;
            _viewModel.FocusVehicleOnMapRequested -= OnFocusVehicleOnMap;
            _viewModel.OnViewDeactivated();
        }

        _viewModel = e.NewValue as VehicleTrackingViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PushVehiclesToMapRequested += OnPushVehiclesToMap;
            _viewModel.FocusVehicleOnMapRequested += OnFocusVehicleOnMap;
            _viewModel.OnViewActivated();
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel?.OnViewDeactivated();
    }

    private void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
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

        var envelope = new JsonObject
        {
            ["type"] = "focusVehicle",
            ["id"] = id
        };
        MapWebView.CoreWebView2.PostWebMessageAsJson(envelope.ToJsonString());
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var root = JsonNode.Parse(e.WebMessageAsJson)?.AsObject();
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
