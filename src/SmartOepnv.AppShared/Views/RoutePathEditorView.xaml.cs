using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.Views;

public partial class RoutePathEditorView : UserControl
{
    private RoutePathEditorViewModel? _viewModel;
    private bool _mapReady;
    private bool _pageLoaded;
    private string? _pendingDraftJson;
    private string? _pendingBoundsJson;
    private bool _pendingResetMapView;

    public RoutePathEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        RootDock.SizeChanged += OnRootDockSizeChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (_mapReady && e.NewSize.Height > 0 && !double.IsPositiveInfinity(e.NewSize.Height))
        {
            _ = RefitMapAsync();
        }
    }

    private void OnRootDockSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (_mapReady && e.NewSize.Height > 0 && !double.IsPositiveInfinity(e.NewSize.Height))
        {
            _ = RefitMapAsync();
        }
    }

    private void OnIsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible || !_mapReady) return;
        _viewModel?.ReloadMapFromDraft();
        if (_pageLoaded)
        {
            _ = RefitMapAsync();
        }
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PushDraftToMapRequested -= OnPushDraftToMapHandler;
        }

        _viewModel = e.NewValue as RoutePathEditorViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PushDraftToMapRequested += OnPushDraftToMapHandler;
            _viewModel.RefreshRoutes();
        }
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (MapWebView.CoreWebView2 is not null) return;

        var userDataFolder = AppPaths.GetWebView2UserDataDirectory(AppServices.SettingsSubfolder);
        Directory.CreateDirectory(userDataFolder);
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(userDataFolder, "RouteMap"));
        await MapWebView.EnsureCoreWebView2Async(env);

        var core = MapWebView.CoreWebView2!;
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += OnNavigationCompleted;
        var mapPath = Path.Combine(AppContext.BaseDirectory, "Assets", "route_path_map.html");
        MapWebView.Source = new Uri(mapPath);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        _pageLoaded = true;
        _mapReady = true;
        _ = FlushPendingMapDataAsync();
    }

    private void OnPushDraftToMapHandler(string json, string? boundsJson, bool resetMapView) =>
        OnPushDraftToMap(json, boundsJson, resetMapView);

    private async void OnPushDraftToMap(string json, string? boundsJson = null, bool resetMapView = false)
    {
        _pendingDraftJson = json;
        _pendingResetMapView = resetMapView;
        if (boundsJson is not null)
        {
            _pendingBoundsJson = boundsJson;
        }

        if (!_mapReady || !_pageLoaded || MapWebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await FlushPendingMapDataAsync();
        }
        catch (Exception ex)
        {
            _viewModel?.NotifyMapStatus($"Karte konnte nicht geladen werden: {ex.Message}");
        }
    }

    private async Task FlushPendingMapDataAsync()
    {
        if (MapWebView.CoreWebView2 is null || !_pageLoaded) return;

        var resetView = _pendingResetMapView;
        if (!string.IsNullOrEmpty(_pendingDraftJson))
        {
            await PushDraftAsync(_pendingDraftJson, resetView);
            _pendingDraftJson = null;
        }

        if (resetView && !string.IsNullOrEmpty(_pendingBoundsJson))
        {
            await FitBoundsAsync(_pendingBoundsJson);
            _pendingBoundsJson = null;
        }

        await InvalidateMapSizeAsync(fitRoute: false);
    }

    private Task PushDraftAsync(string json, bool resetMapView = false)
    {
        var core = MapWebView.CoreWebView2 ?? throw new InvalidOperationException("Karte nicht bereit.");
        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{'))
        {
            throw new InvalidOperationException("Ungültiger Karten-Entwurf.");
        }

        var envelope = new JsonObject
        {
            ["type"] = "loadDraft",
            ["payload"] = JsonNode.Parse(trimmed),
            ["options"] = new JsonObject { ["resetView"] = resetMapView }
        };
        core.PostWebMessageAsJson(envelope.ToJsonString());
        return Task.CompletedTask;
    }

    private async Task FitBoundsAsync(string boundsArrayJson)
    {
        var core = MapWebView.CoreWebView2;
        if (core is null) return;
        await core.ExecuteScriptAsync($"window.fitBoundsPoints({boundsArrayJson});");
    }

    private Task RefitMapAsync() => InvalidateMapSizeAsync(fitRoute: false);

    private async Task InvalidateMapSizeAsync(bool fitRoute = false)
    {
        if (!_mapReady || MapWebView.CoreWebView2 is null) return;
        try
        {
            var script = fitRoute
                ? "if (window.invalidateMapSize) invalidateMapSize(true);"
                : "if (window.invalidateMapSize) invalidateMapSize();";
            await MapWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // ignore resize errors
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var root = JsonNode.Parse(e.WebMessageAsJson)?.AsObject();
            if (root is null) return;
            var type = root["type"]?.GetValue<string>();
            switch (type)
            {
                case "ready":
                    _mapReady = true;
                    _pageLoaded = true;
                    _ = FlushPendingMapDataAsync();
                    if (string.IsNullOrEmpty(_pendingDraftJson))
                    {
                        _viewModel?.RefreshRoutes();
                    }
                    else
                    {
                        _viewModel?.ReloadMapFromDraft();
                    }
                    break;
                case "draftLoaded":
                    _viewModel?.NotifyMapStatus(root["message"]?.GetValue<string>() ?? string.Empty);
                    _ = InvalidateMapSizeAsync(fitRoute: false);
                    break;
                case "draftChanged":
                    var draftNode = root["draft"];
                    if (draftNode is not null)
                    {
                        var recordUndo = root["recordUndo"]?.GetValue<bool>() == true;
                        _viewModel?.ApplyDraftJsonFromMap(draftNode.ToJsonString(), recordUndo);
                    }
                    break;
                case "segmentDeleteRequested":
                    _viewModel?.DeleteSegmentFromMap(
                        root["from"]?.GetValue<string>(),
                        root["to"]?.GetValue<string>());
                    break;
                case "segmentSelected":
                    _viewModel?.SetSelectedSegment(
                        root["from"]?.GetValue<string>(),
                        root["to"]?.GetValue<string>(),
                        root["maneuverIndex"]?.GetValue<int>());
                    break;
                case "segmentAdded":
                    _viewModel?.OnSegmentAddedFromMap(
                        root["from"]?.GetValue<string>(),
                        root["to"]?.GetValue<string>());
                    break;
                case "nodeMoved":
                    _viewModel?.SchedulePreviewSnapForNode(root["nodeId"]?.GetValue<string>());
                    break;
            }
        }
        catch
        {
            // ignore malformed messages from map
        }
    }
}
