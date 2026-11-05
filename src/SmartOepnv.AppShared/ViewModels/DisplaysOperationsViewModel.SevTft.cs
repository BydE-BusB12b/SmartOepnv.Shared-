using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Sev;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Sev;

namespace SmartOepnv.AppShared.ViewModels;

public partial class DisplaysOperationsViewModel
{
    public ObservableCollection<string> SevPreviewStops { get; } = [];

    [ObservableProperty] private bool sevEnabled;
    [ObservableProperty] private bool sevAutoSend = true;
    [ObservableProperty] private string sevHost = string.Empty;
    [ObservableProperty] private string sevPortText = SevTftEndpoint.DefaultPort.ToString();
    [ObservableProperty] private string sevApiToken = string.Empty;
    [ObservableProperty] private string sevStatusMessage =
        "SEV-TFT: IP-Adresse und Port des Raspberry Pi eintragen. Der eingerichtete Dienst nutzt Port 8080.";
    [ObservableProperty] private string sevPreviewRoute = string.Empty;
    [ObservableProperty] private string sevPreviewLine = string.Empty;
    [ObservableProperty] private string sevPreviewDestination = string.Empty;
    [ObservableProperty] private string sevPreviewMessage =
        "Noch keine Route geöffnet. Unter Routen eine Fahrt wählen, zum Beispiel 004/95.";
    [ObservableProperty] private string sevOpenedRouteKey = string.Empty;
    [ObservableProperty] private bool isSevBusy;

    public string FooterStatusMessage => StatusMessage;

    private void InitializeSevTft()
    {
        LoadSevSettingsFromStore();
        SevTftCoordinator.OpenedRouteChanged += OnSevRouteOpened;
        SevTftCoordinator.StatusChanged += OnSevCoordinatorStatus;
        RefreshSevPreview(SevTftCoordinator.OpenedRouteKey);
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(FooterStatusMessage));
        if (value == 2)
        {
            RefreshSevPreview(SevTftCoordinator.OpenedRouteKey);
        }
    }

    partial void OnStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(FooterStatusMessage));

    partial void OnSevStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(FooterStatusMessage));

    partial void OnIsSevBusyChanged(bool value)
    {
        SaveSevSettingsCommand.NotifyCanExecuteChanged();
        TestSevConnectionCommand.NotifyCanExecuteChanged();
        SendOpenedSevRouteCommand.NotifyCanExecuteChanged();
    }

    private void OnSevRouteOpened(string? routeKey) =>
        RunOnUi(() => RefreshSevPreview(routeKey));

    private void OnSevCoordinatorStatus(string message) =>
        RunOnUi(() => SevStatusMessage = message);

    private static void RunOnUi(Action work)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            work();
            return;
        }

        dispatcher.BeginInvoke(work);
    }

    private void LoadSevSettingsFromStore()
    {
        if (!AppServices.IsInitialized || AppServices.PlanerAppSettings is null)
        {
            return;
        }

        var settings = AppServices.PlanerAppSettings.Load();
        SevHost = settings.SevTftHost;
        SevPortText = (settings.SevTftPort is >= 1 and <= 65535
            ? settings.SevTftPort
            : SevTftEndpoint.DefaultPort).ToString();
        SevEnabled = settings.SevTftEnabled;
        SevAutoSend = settings.SevTftAutoSend;
        SevApiToken = new SevTftSecretStore(AppServices.SettingsSubfolder).LoadToken() ?? string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanUseSevCommands))]
    private void SaveSevSettings()
    {
        if (!SevTftEndpoint.TryCreate(SevHost, SevPortText, SevApiToken, out var endpoint, out var error) ||
            endpoint is null)
        {
            SevStatusMessage = error;
            return;
        }

        if (!AppServices.IsInitialized || AppServices.PlanerAppSettings is null)
        {
            SevStatusMessage = "Einstellungen sind noch nicht verfügbar.";
            return;
        }

        var settings = AppServices.PlanerAppSettings.Load();
        settings.SevTftHost = endpoint.Host;
        settings.SevTftPort = endpoint.Port;
        settings.SevTftEnabled = SevEnabled;
        settings.SevTftAutoSend = SevAutoSend;
        AppServices.PlanerAppSettings.Save(settings);
        new SevTftSecretStore(AppServices.SettingsSubfolder).SaveToken(endpoint.ApiToken);
        SevHost = endpoint.Host;
        SevPortText = endpoint.Port.ToString();
        SevStatusMessage = endpoint.ApiToken is null
            ? $"Gespeichert. SEV-TFT {(SevEnabled ? "aktiv" : "aus")} · {endpoint.Host}:{endpoint.Port} · ohne API-Token."
            : $"Gespeichert. SEV-TFT {(SevEnabled ? "aktiv" : "aus")} · {endpoint.Host}:{endpoint.Port}.";
    }

    [RelayCommand(CanExecute = nameof(CanUseSevCommands))]
    private async Task TestSevConnectionAsync()
    {
        if (!SevTftEndpoint.TryCreate(SevHost, SevPortText, SevApiToken, out var endpoint, out var error) ||
            endpoint is null)
        {
            SevStatusMessage = error;
            return;
        }

        IsSevBusy = true;
        try
        {
            var result = await new SevTftClient().GetDataAsync(endpoint).ConfigureAwait(true);
            SevStatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            SevStatusMessage = "Verbindungstest fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsSevBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSevCommands))]
    private async Task SendOpenedSevRouteAsync()
    {
        if (string.IsNullOrWhiteSpace(SevOpenedRouteKey))
        {
            SevStatusMessage = "Keine Route geöffnet. Unter Routen eine Fahrt wählen, zum Beispiel 004/95.";
            return;
        }

        if (!SevTftEndpoint.TryCreate(SevHost, SevPortText, SevApiToken, out var endpoint, out var error) ||
            endpoint is null)
        {
            SevStatusMessage = error;
            return;
        }

        IsSevBusy = true;
        try
        {
            var result = await SevTftCoordinator.SendRouteAsync(SevOpenedRouteKey, endpoint).ConfigureAwait(true);
            SevStatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            SevStatusMessage = "Senden fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsSevBusy = false;
        }
    }

    private bool CanUseSevCommands() => !IsSevBusy;

    private void RefreshSevPreview(string? routeKey)
    {
        SevOpenedRouteKey = routeKey ?? string.Empty;
        SevPreviewStops.Clear();
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            SevPreviewRoute = string.Empty;
            SevPreviewLine = string.Empty;
            SevPreviewDestination = string.Empty;
            SevPreviewMessage = "Noch keine Route geöffnet. Unter Routen eine Fahrt wählen, zum Beispiel 004/95.";
            return;
        }

        var days = AppServices.IsInitialized ? AppServices.Routes.Editor?.GetRouteOperatingDays(routeKey) : null;
        SevPreviewRoute = RouteDisplayHelper.ToCompactRouteListLabel(routeKey, days);
        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            SevPreviewLine = string.Empty;
            SevPreviewDestination = string.Empty;
            SevPreviewMessage = "Kein Routenpaket geladen.";
            return;
        }

        var stops = AppServices.Routes.Editor.GetStops(routeKey);
        if (!SevTftRouteBuilder.TryBuild(routeKey, stops, betrieb: null, out var payload, out var buildError) ||
            payload is null)
        {
            SevPreviewLine = string.Empty;
            SevPreviewDestination = string.Empty;
            SevPreviewMessage = buildError;
            return;
        }

        SevPreviewLine = payload.Linie;
        SevPreviewDestination = payload.Ziel;
        for (var i = 0; i < payload.Haltestellen.Count; i++)
        {
            SevPreviewStops.Add($"{i + 1}. {payload.Haltestellen[i]}");
        }

        SevPreviewMessage = $"{payload.Haltestellen.Count} Haltestellen aus dem Verlauf.";
    }
}
