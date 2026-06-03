using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.AppShared.ViewModels;

public partial class VehicleTrackingViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private readonly VehicleTrackingService _tracking = AppServices.VehicleTracking;
    private CancellationTokenSource? _pollCts;
    private IReadOnlyList<VehicleLiveState> _vehicles = [];

    public event Action<string>? PushVehiclesToMapRequested;
    public event Action<string>? FocusVehicleOnMapRequested;

    public ObservableCollection<VehicleListItemViewModel> Vehicles { get; } = [];

    [ObservableProperty] private string statusMessage = "Dropbox-Verbindung erforderlich – Fahrzeuge werden alle 30 s aktualisiert.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private VehicleListItemViewModel? selectedVehicle;

    partial void OnSelectedVehicleChanged(VehicleListItemViewModel? value)
    {
        if (value is not null)
        {
            FocusVehicleOnMap(value.Id);
        }
    }

    public void OnViewActivated()
    {
        _ = RefreshAsync();
        StartPolling();
    }

    public void OnViewDeactivated()
    {
        StopPolling();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var json = AppServices.Routes.CurrentJson;
            var vehicles = await _tracking.SyncAsync(json);

            await RunOnUiAsync(() =>
            {
                _vehicles = vehicles;
                RebuildList();
                PushVehiclesToMap();
                var online = _vehicles.Count(v => v.Status == VehicleOnlineStatus.Online);
                StatusMessage = _vehicles.Count == 0
                    ? "Keine Fahrzeug-Standorte in Dropbox gefunden (location_chat_*.json)."
                    : $"{_vehicles.Count} Fahrzeuge – {online} online (grün), veraltet rot, offline lila.";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Aktualisierung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectVehicleFromMap(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        SelectedVehicle = Vehicles.FirstOrDefault(v => v.Id == id);
    }

    public bool FocusVehicleByPhone(string? normalizedPhone)
    {
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return false;
        }

        var target = Vehicles.FirstOrDefault(v =>
            string.Equals(v.PhoneNormalized, normalizedPhone, StringComparison.Ordinal));
        if (target is null)
        {
            return false;
        }

        SelectedVehicle = target;
        FocusVehicleOnMap(target.Id);
        return true;
    }

    private void FocusVehicleOnMap(string id)
    {
        PushVehiclesToMap();
        FocusVehicleOnMapRequested?.Invoke(id);
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    private void RebuildList()
    {
        Vehicles.Clear();
        foreach (var v in _vehicles)
        {
            Vehicles.Add(VehicleListItemViewModel.From(v));
        }
    }

    private void PushVehiclesToMap()
    {
        var payload = JsonSerializer.Serialize(new { vehicles = BuildMapVehicles() });
        PushVehiclesToMapRequested?.Invoke(payload);
    }

    private IEnumerable<object> BuildMapVehicles() =>
        _vehicles.Select(v => new
        {
            id = v.Id,
            displayName = v.DisplayName,
            lineCourse = v.LineCourse,
            route = v.RouteName,
            stop = v.StopName,
            destination = v.Destination,
            lat = v.Latitude,
            lon = v.Longitude,
            speedKmh = v.SpeedKmh,
            status = v.Status switch
            {
                VehicleOnlineStatus.Stale => "stale",
                VehicleOnlineStatus.Offline => "offline",
                _ => "online"
            },
            timestampMs = v.TimestampEpochMs
        });

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PollInterval, token);
                    await RefreshAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Nächster Poll-Zyklus
                }
            }
        }, token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    public void Dispose() => StopPolling();
}

public sealed class VehicleListItemViewModel
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? PhoneNormalized { get; init; }
    public string? LineCourse { get; init; }
    public string? RouteName { get; init; }
    public string? StopName { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public string LastUpdateLabel { get; init; } = string.Empty;
    public string DetailLine { get; init; } = string.Empty;

    public static VehicleListItemViewModel From(VehicleLiveState v)
    {
        var updated = DateTimeOffset.FromUnixTimeMilliseconds(v.TimestampEpochMs).ToLocalTime();
        var status = v.Status switch
        {
            VehicleOnlineStatus.Stale => "Veraltet",
            VehicleOnlineStatus.Offline => "Offline",
            _ => "Online"
        };

        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(v.RouteName)) detailParts.Add(v.RouteName);
        if (!string.IsNullOrWhiteSpace(v.StopName)) detailParts.Add(v.StopName);
        if (v.SpeedKmh > 0) detailParts.Add($"{v.SpeedKmh} km/h");

        return new VehicleListItemViewModel
        {
            Id = v.Id,
            DisplayName = v.DisplayName,
            PhoneNormalized = string.IsNullOrWhiteSpace(v.PhoneNumber)
                ? null
                : new string(v.PhoneNumber.Where(char.IsDigit).ToArray()),
            LineCourse = string.IsNullOrWhiteSpace(v.LineCourse) ? "–" : v.LineCourse,
            RouteName = v.RouteName,
            StopName = v.StopName,
            StatusLabel = status,
            LastUpdateLabel = updated.ToString("dd.MM. HH:mm:ss"),
            DetailLine = detailParts.Count > 0 ? string.Join(" · ", detailParts) : "Keine Zusatzinfos"
        };
    }
}
