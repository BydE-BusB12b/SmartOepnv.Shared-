using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.Views;

public partial class VehicleTrackingView : UserControl
{
    private VehicleTrackingViewModel? _viewModel;
    private bool _mapReady;

    public VehicleTrackingView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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
            if (_mapReady)
            {
                _viewModel.OnViewActivated();
            }
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel?.OnViewDeactivated();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (MapWebView.CoreWebView2 is not null) return;

        var userDataFolder = AppPaths.GetWebView2UserDataDirectory(AppServices.SettingsSubfolder);
        Directory.CreateDirectory(userDataFolder);
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(userDataFolder, "VehicleMap"));
        await MapWebView.EnsureCoreWebView2Async(env);

        MapWebView.CoreWebView2!.WebMessageReceived += OnWebMessageReceived;
        var mapPath = Path.Combine(AppContext.BaseDirectory, "Assets", "vehicle_tracking_map.html");
        MapWebView.Source = new Uri(mapPath);
    }

    private async void OnPushVehiclesToMap(string json)
    {
        if (!_mapReady || MapWebView.CoreWebView2 is null) return;
        var escaped = JsonSerializer.Serialize(json);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"loadVehicles({escaped})");
    }

    private async void OnFocusVehicleOnMap(string id)
    {
        if (!_mapReady || MapWebView.CoreWebView2 is null) return;
        var escapedId = JsonSerializer.Serialize(id);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"focusVehicle({escapedId})");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var root = JsonNode.Parse(e.WebMessageAsJson)?.AsObject();
            if (root is null) return;

            switch (root["type"]?.GetValue<string>())
            {
                case "ready":
                    _mapReady = true;
                    _viewModel?.OnViewActivated();
                    break;
                case "vehicleSelected":
                    _viewModel?.SelectVehicleFromMap(root["id"]?.GetValue<string>());
                    break;
            }
        }
        catch
        {
            // ignore malformed map messages
        }
    }
}
