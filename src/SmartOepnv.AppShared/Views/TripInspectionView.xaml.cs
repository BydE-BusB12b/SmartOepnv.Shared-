using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.Views;

public partial class TripInspectionView : UserControl
{
    private TripInspectionViewModel? _viewModel;
    private bool _mapReady;
    private bool _pageLoaded;
    private string? _pendingMapJson;
    private bool _coreWired;

    public TripInspectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => _ = InvalidateMapSizeAsync();
        MapHost.SizeChanged += (_, _) => _ = InvalidateMapSizeAsync();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PushTraceToMapRequested -= OnPushTraceToMap;
        }

        _viewModel = e.NewValue as TripInspectionViewModel;
        EnsureViewModelSubscription();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureViewModelSubscription();

        try
        {
            if (MapWebView.CoreWebView2 is null)
            {
                var userDataFolder = AppPaths.GetWebView2UserDataDirectory(AppServices.SettingsSubfolder);
                Directory.CreateDirectory(userDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(userDataFolder, "TripInspectionMap"));
                await MapWebView.EnsureCoreWebView2Async(env);
            }

            WireCoreIfNeeded();

            if (!_pageLoaded || MapWebView.Source is null)
            {
                var mapPath = Path.Combine(AppContext.BaseDirectory, "Assets", "trip_inspection_map.html");
                _mapReady = false;
                _pageLoaded = false;
                MapWebView.Source = new Uri(mapPath);
                return;
            }

            // Gecachte View: aktuelle Auswahl erneut an die Karte schicken.
            _viewModel?.RefreshMapForCurrentSelection();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("TripInspection map init: " + ex.Message);
        }
    }

    private void WireCoreIfNeeded()
    {
        var core = MapWebView.CoreWebView2;
        if (core is null || _coreWired)
        {
            return;
        }

        core.Settings.IsWebMessageEnabled = true;
        core.NavigationCompleted -= OnNavigationCompleted;
        core.NavigationCompleted += OnNavigationCompleted;
        _coreWired = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // View bleibt im NavigationItem gecacht — Event nicht abmelden, sonst
        // bleiben Klicks auf Zeiten ohne Wirkung beim erneuten Öffnen.
    }

    private void EnsureViewModelSubscription()
    {
        if (_viewModel is null)
        {
            _viewModel = DataContext as TripInspectionViewModel;
        }

        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PushTraceToMapRequested -= OnPushTraceToMap;
        _viewModel.PushTraceToMapRequested += OnPushTraceToMap;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        _pageLoaded = true;
        _mapReady = true;
        _ = FlushPendingMapDataAsync();
    }

    private void OnPushTraceToMap(string json)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnPushTraceToMap(json));
            return;
        }

        _pendingMapJson = json;
        if (_mapReady && _pageLoaded)
        {
            _ = FlushPendingMapDataAsync();
        }
    }

    private async Task FlushPendingMapDataAsync()
    {
        if (MapWebView.CoreWebView2 is null || !_pageLoaded || string.IsNullOrWhiteSpace(_pendingMapJson))
        {
            return;
        }

        var json = _pendingMapJson;
        _pendingMapJson = null;
        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return;
        }

        try
        {
            // Primär: ExecuteScriptAsync — zuverlässig auch bei gecachten WebViews.
            await MapWebView.CoreWebView2.ExecuteScriptAsync(
                $"if (window.applyTracePayload) window.applyTracePayload({trimmed});");

            // Zusätzlich PostWebMessage (falls Script-Pfad fehlschlägt / ältere HTML-Version).
            var envelope = new JsonObject
            {
                ["type"] = "loadTrace",
                ["payload"] = JsonNode.Parse(trimmed)
            };
            MapWebView.CoreWebView2.PostWebMessageAsJson(envelope.ToJsonString());
            await InvalidateMapSizeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("TripInspection map push: " + ex.Message);
        }
    }

    private async Task InvalidateMapSizeAsync()
    {
        if (!_mapReady || !_pageLoaded || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await MapWebView.CoreWebView2.ExecuteScriptAsync(
                "if (window.invalidateMapSize) window.invalidateMapSize();");
            MapWebView.CoreWebView2.PostWebMessageAsJson("""{"type":"invalidateSize"}""");
        }
        catch
        {
            // ignore resize errors
        }
    }
}
