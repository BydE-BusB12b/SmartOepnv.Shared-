using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.Geo;
using SmartOepnv.Core.RoutePath;
using SmartOepnv.Core.VehicleTracking;
using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.ViewModels;

public partial class VehicleTrackingViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(8);
    private static readonly NominatimReverseGeocoder ReverseGeocoder = new();
    private readonly VehicleTrackingService _tracking = AppServices.VehicleTracking;
    private readonly VehicleTrackingMapViewStore _mapViewStore = new(AppServices.SettingsSubfolder);
    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _geocodeCts;
    private IReadOnlyList<VehicleLiveState> _vehicles = [];
    private string? _pendingDetailPhone;

    public event Action<string>? PushVehiclesToMapRequested;
    public event Action<string>? FocusVehicleOnMapRequested;
    public event Action<string?>? HighlightRouteOnMapRequested;
    public event Action<VehicleListItemViewModel>? ShowVehicleDetailRequested;

    public ObservableCollection<VehicleListItemViewModel> Vehicles { get; } = [];

    [ObservableProperty] private string statusMessage = "Dropbox-Verbindung erforderlich – Fahrzeuge werden alle 8 s aktualisiert.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private VehicleListItemViewModel? selectedVehicle;

    partial void OnSelectedVehicleChanged(VehicleListItemViewModel? value)
    {
        if (_vehicles.Count > 0)
        {
            PushVehiclesToMap();
        }

        if (value is null)
        {
            return;
        }

        HighlightRouteOnMapRequested?.Invoke(ResolveHighlightRouteKey(value));
        FocusVehicleOnMapRequested?.Invoke(value.Id);
    }

    /// <summary>Nach Paket-Import Karten-Fahrwege neu laden.</summary>
    public void NotifyRoutePackageChanged()
    {
        if (_vehicles.Count > 0)
        {
            PushVehiclesToMap();
        }
    }

    public void OnViewActivated()
    {
        StartPolling();
        _ = RefreshAsync();
    }

    /// <summary>Karte ist bereit – vorhandene Fahrzeuge erneut auf die Karte schieben (Race beim ersten Laden).</summary>
    public void OnMapReady()
    {
        if (_vehicles.Count > 0)
        {
            PushVehiclesToMap();
        }
        else
        {
            _ = RefreshAsync();
        }
    }

    public void OnViewDeactivated()
    {
        StopPolling();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

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
                FlushPendingDetailPhoneIfNeeded();
                var online = _vehicles.Count(v => v.Status == VehicleOnlineStatus.Online);
                StatusMessage = _vehicles.Count == 0
                    ? "Keine Fahrzeug-Standorte in Dropbox gefunden (location_chat_*.json)."
                    : $"{_vehicles.Count} Fahrzeuge – {online} online (grün), veraltet rot, offline lila.";
            });

            StartStreetResolution();
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => StatusMessage = $"Aktualisierung fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectVehicleFromMap(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        _ = RunOnUiAsync(() =>
        {
            SelectedVehicle = Vehicles.FirstOrDefault(v => v.Id == id);
        });
    }

    public VehicleTrackingMapView? GetSavedMapView() => _mapViewStore.Load();

    public void OnMapViewChangedFromMap(double lat, double lon, double zoom)
    {
        if (!double.IsFinite(lat) || !double.IsFinite(lon) || !double.IsFinite(zoom))
        {
            return;
        }

        _mapViewStore.Save(new VehicleTrackingMapView
        {
            Lat = lat,
            Lon = lon,
            Zoom = zoom
        });
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
        return true;
    }

    public VehicleListItemViewModel? TryGetVehicleByPhone(string? normalizedPhone)
    {
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return null;
        }

        return Vehicles.FirstOrDefault(v =>
            VoipPhone.Match(v.PhoneNormalized, normalizedPhone));
    }

    public bool ShowVehicleDetailForPhone(string? normalizedPhone)
    {
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return false;
        }

        if (!FocusVehicleByPhone(normalizedPhone))
        {
            _pendingDetailPhone = normalizedPhone;
            return false;
        }

        var target = Vehicles.FirstOrDefault(v =>
            string.Equals(v.PhoneNormalized, normalizedPhone, StringComparison.Ordinal));
        if (target is null)
        {
            _pendingDetailPhone = normalizedPhone;
            return false;
        }

        _pendingDetailPhone = null;
        ShowVehicleDetailRequested?.Invoke(target);
        return true;
    }

    private void FlushPendingDetailPhoneIfNeeded()
    {
        var phone = _pendingDetailPhone;
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        ShowVehicleDetailForPhone(phone);
    }

    private string? ResolveHighlightRouteKey(VehicleListItemViewModel vehicle)
    {
        if (string.IsNullOrWhiteSpace(vehicle.RouteName))
        {
            return null;
        }

        var root = AppServices.Routes.Editor?.PackageRoot;
        return root is null
            ? null
            : LeitstelleRoutePathOverview.ResolveRouteKey(root, vehicle.RouteName, vehicle.LineCourse);
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
        var previous = Vehicles.ToDictionary(v => v.Id, v => v);
        var selectedId = SelectedVehicle?.Id;
        Vehicles.Clear();
        foreach (var v in _vehicles)
        {
            if (previous.TryGetValue(v.Id, out var existing))
            {
                existing.ApplyLiveState(v);
                Vehicles.Add(existing);
            }
            else
            {
                Vehicles.Add(VehicleListItemViewModel.From(v));
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            SelectedVehicle = Vehicles.FirstOrDefault(x => x.Id == selectedId);
        }
    }

    private void StartStreetResolution()
    {
        _geocodeCts?.Cancel();
        _geocodeCts?.Dispose();
        _geocodeCts = new CancellationTokenSource();
        var token = _geocodeCts.Token;
        _ = ResolveStreetsAsync(token);
    }

    private async Task ResolveStreetsAsync(CancellationToken ct)
    {
        List<(string Id, double Lat, double Lon)> pending = [];
        await RunOnUiAsync(() =>
        {
            pending = Vehicles
                .Where(v => HasResolvableCoordinates(v) && string.IsNullOrWhiteSpace(v.StreetAddress))
                .Select(v => (v.Id, v.Latitude, v.Longitude))
                .ToList();
        });

        foreach (var (id, lat, lon) in pending)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            string? street;
            try
            {
                street = await ReverseGeocoder.TryResolveStreetAsync(lat, lon, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(street))
            {
                continue;
            }

            await RunOnUiAsync(() =>
            {
                var current = Vehicles.FirstOrDefault(v => v.Id == id);
                if (current is null || !CoordinatesMatch(current.Latitude, current.Longitude, lat, lon))
                {
                    return;
                }

                current.StreetAddress = street;
            });
        }
    }

    private static bool HasResolvableCoordinates(VehicleListItemViewModel vehicle) =>
        double.IsFinite(vehicle.Latitude)
        && double.IsFinite(vehicle.Longitude)
        && Math.Abs(vehicle.Latitude) <= 90
        && Math.Abs(vehicle.Longitude) <= 180
        && !(vehicle.Latitude == 0 && vehicle.Longitude == 0);

    private static bool CoordinatesMatch(double aLat, double aLon, double bLat, double bLon) =>
        Math.Abs(aLat - bLat) < 0.00001 && Math.Abs(aLon - bLon) < 0.00001;

    private void PushVehiclesToMap()
    {
        var payload = BuildMapPayload();
        PushVehiclesToMapRequested?.Invoke(payload.ToJsonString());
    }

    private JsonObject BuildMapPayload()
    {
        var root = AppServices.Routes.Editor?.PackageRoot;
        var payload = new JsonObject
        {
            ["vehicles"] = JsonSerializer.SerializeToNode(BuildMapVehicles())
        };

        var routePaths = new JsonArray();
        foreach (var key in CollectRouteKeysForMap(root))
        {
            var overviewJson = LeitstelleRoutePathOverview.TryGetOverviewJson(root, key);
            if (string.IsNullOrWhiteSpace(overviewJson))
            {
                continue;
            }

            try
            {
                routePaths.Add(JsonNode.Parse(overviewJson));
            }
            catch
            {
                // defektes Overview überspringen
            }
        }

        payload["routePaths"] = routePaths;

        if (!string.IsNullOrWhiteSpace(SelectedVehicle?.RouteName) && root is not null)
        {
            var highlightKey = LeitstelleRoutePathOverview.ResolveRouteKey(
                root,
                SelectedVehicle.RouteName,
                SelectedVehicle.LineCourse);
            if (highlightKey is not null)
            {
                payload["highlightRouteKey"] = highlightKey;
            }
        }

        return payload;
    }

    private HashSet<string> CollectRouteKeysForMap(JsonObject? root)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root is null)
        {
            return keys;
        }

        if (SelectedVehicle is not null && !string.IsNullOrWhiteSpace(SelectedVehicle.RouteName))
        {
            AddResolvedRouteKey(keys, root, SelectedVehicle.RouteName, SelectedVehicle.LineCourse);
            return keys;
        }

        foreach (var vehicle in _vehicles.Where(v =>
                     v.Status is VehicleOnlineStatus.Online or VehicleOnlineStatus.Stale))
        {
            if (string.IsNullOrWhiteSpace(vehicle.RouteName))
            {
                continue;
            }

            AddResolvedRouteKey(keys, root, vehicle.RouteName, vehicle.LineCourse);
        }

        return keys;
    }

    private static void AddResolvedRouteKey(
        HashSet<string> keys,
        JsonObject root,
        string routeName,
        string? lineCourse = null)
    {
        var key = LeitstelleRoutePathOverview.ResolveRouteKey(root, routeName, lineCourse);
        if (key is not null)
        {
            keys.Add(key);
        }
    }

    internal VehicleTrackingMapView? LoadSavedMapViewForMap() => GetSavedMapView();

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

    public void Dispose()
    {
        StopPolling();
        _geocodeCts?.Cancel();
        _geocodeCts?.Dispose();
        _geocodeCts = null;
    }
}

public sealed class VehicleListItemViewModel : INotifyPropertyChanged
{
    private string? _streetAddress;
    private string? _phoneRaw;
    private string? _phoneNormalized;
    private string? _lineCourse;
    private string? _routeName;
    private string? _stopName;
    private string? _destination;
    private string? _driverName;
    private string? _driverPersonnelNumber;
    private int? _batteryLevel;
    private int? _delaySeconds;
    private string? _appVersion;
    private long? _routesExportPackageVersion;
    private long? _routesUpdatePackageVersion;
    private int _speedKmh;
    private double _latitude;
    private double _longitude;
    private double _accuracyM;
    private VehicleOnlineStatus _onlineStatus;
    private string _statusLabel = string.Empty;
    private string _lastUpdateLabel = string.Empty;
    private string _detailLine = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? PhoneNormalized
    {
        get => _phoneNormalized;
        private set => SetField(ref _phoneNormalized, value);
    }

    public string? PhoneRaw
    {
        get => _phoneRaw;
        private set => SetField(ref _phoneRaw, value);
    }

    public string? LineCourse
    {
        get => _lineCourse;
        private set => SetField(ref _lineCourse, value);
    }

    public string? RouteName
    {
        get => _routeName;
        private set => SetField(ref _routeName, value);
    }

    public string? StopName
    {
        get => _stopName;
        private set => SetField(ref _stopName, value);
    }

    public string? Destination
    {
        get => _destination;
        private set => SetField(ref _destination, value);
    }

    public string? DriverName
    {
        get => _driverName;
        private set => SetField(ref _driverName, value);
    }

    public string? DriverPersonnelNumber
    {
        get => _driverPersonnelNumber;
        private set => SetField(ref _driverPersonnelNumber, value);
    }

    public int? BatteryLevel
    {
        get => _batteryLevel;
        private set => SetField(ref _batteryLevel, value);
    }

    public int? DelaySeconds
    {
        get => _delaySeconds;
        private set => SetField(ref _delaySeconds, value);
    }

    public string? AppVersion
    {
        get => _appVersion;
        private set => SetField(ref _appVersion, value, nameof(AppVersionDisplay));
    }

    public long? RoutesExportPackageVersion
    {
        get => _routesExportPackageVersion;
        private set => SetField(ref _routesExportPackageVersion, value, nameof(RoutesExportVersionDisplay));
    }

    public long? RoutesUpdatePackageVersion
    {
        get => _routesUpdatePackageVersion;
        private set => SetField(ref _routesUpdatePackageVersion, value, nameof(RoutesUpdateVersionDisplay));
    }

    public int SpeedKmh
    {
        get => _speedKmh;
        private set => SetField(ref _speedKmh, value);
    }

    public double Latitude
    {
        get => _latitude;
        private set => SetField(ref _latitude, value);
    }

    public double Longitude
    {
        get => _longitude;
        private set => SetField(ref _longitude, value);
    }

    public double AccuracyM
    {
        get => _accuracyM;
        private set => SetField(ref _accuracyM, value);
    }

    public VehicleOnlineStatus OnlineStatus
    {
        get => _onlineStatus;
        private set => SetField(ref _onlineStatus, value);
    }

    public string StatusLabel
    {
        get => _statusLabel;
        private set => SetField(ref _statusLabel, value);
    }

    public string LastUpdateLabel
    {
        get => _lastUpdateLabel;
        private set => SetField(ref _lastUpdateLabel, value);
    }

    public string DetailLine
    {
        get => _detailLine;
        private set => SetField(ref _detailLine, value);
    }

    public static VehicleListItemViewModel From(VehicleLiveState v)
    {
        var item = new VehicleListItemViewModel
        {
            Id = v.Id,
            DisplayName = v.DisplayName
        };
        item.ApplyLiveState(v);
        return item;
    }

    public static VehicleListItemViewModel ForVoip(string phoneNormalized, string displayName) =>
        From(new VehicleLiveState
        {
            Id = phoneNormalized,
            DisplayName = displayName,
            PhoneNumber = phoneNormalized,
            Status = VehicleOnlineStatus.Online,
            TimestampEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

    /// <summary>Marker für gebündelte Live-Updates (Detail-Panel).</summary>
    public bool LiveState => true;

    public void ApplyLiveState(VehicleLiveState v)
    {
        var updated = DateTimeOffset.FromUnixTimeMilliseconds(v.TimestampEpochMs).ToLocalTime();
        var status = v.Status switch
        {
            VehicleOnlineStatus.Stale => "Veraltet",
            VehicleOnlineStatus.Offline => "Offline",
            _ => "Online"
        };

        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(v.RouteName))
        {
            detailParts.Add(v.RouteName);
        }

        if (!string.IsNullOrWhiteSpace(v.StopName))
        {
            detailParts.Add(v.StopName);
        }

        if (v.SpeedKmh > 0)
        {
            detailParts.Add($"{v.SpeedKmh} km/h");
        }

        var coordsChanged = !CoordinatesMatch(Latitude, Longitude, v.Latitude, v.Longitude);

        PhoneRaw = v.PhoneNumber;
        PhoneNormalized = string.IsNullOrWhiteSpace(v.PhoneNumber)
            ? null
            : new string(v.PhoneNumber.Where(char.IsDigit).ToArray());
        LineCourse = string.IsNullOrWhiteSpace(v.LineCourse) ? "–" : v.LineCourse;
        RouteName = v.RouteName;
        StopName = v.StopName;
        Destination = v.Destination;
        DriverName = v.DriverName;
        DriverPersonnelNumber = v.DriverPersonnelNumber;
        BatteryLevel = v.BatteryLevel;
        DelaySeconds = v.DelaySeconds;
        AppVersion = v.AppVersion;
        RoutesExportPackageVersion = v.RoutesExportPackageVersion;
        RoutesUpdatePackageVersion = v.RoutesUpdatePackageVersion;
        SpeedKmh = v.SpeedKmh;
        Latitude = v.Latitude;
        Longitude = v.Longitude;
        AccuracyM = v.AccuracyM;
        OnlineStatus = v.Status;
        StatusLabel = status;
        LastUpdateLabel = updated.ToString("dd.MM. HH:mm:ss");
        DetailLine = detailParts.Count > 0 ? string.Join(" · ", detailParts) : "Keine Zusatzinfos";

        if (coordsChanged)
        {
            StreetAddress = null;
        }

        OnPropertyChanged(nameof(LiveState));
    }

    private static bool CoordinatesMatch(double aLat, double aLon, double bLat, double bLon) =>
        Math.Abs(aLat - bLat) < 0.00001 && Math.Abs(aLon - bLon) < 0.00001;

    public string? ResolvePhoneNumber()
    {
        if (!string.IsNullOrWhiteSpace(PhoneRaw))
        {
            return PhoneRaw;
        }

        if (!string.IsNullOrWhiteSpace(PhoneNormalized))
        {
            return PhoneNormalized;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return null;
        }

        var match = editor.RegisteredVehicles.FirstOrDefault(v =>
            string.Equals(v.Name.Trim(), DisplayName.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.PhoneNumber;
    }

    public string DriverDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DriverName) && string.IsNullOrWhiteSpace(DriverPersonnelNumber))
            {
                return "nicht angemeldet";
            }

            if (!string.IsNullOrWhiteSpace(DriverName) && !string.IsNullOrWhiteSpace(DriverPersonnelNumber))
            {
                return $"{DriverName} (PN {DriverPersonnelNumber})";
            }

            return DriverName ?? $"PN {DriverPersonnelNumber}";
        }
    }

    public string SpeedDisplay => SpeedKmh > 0 ? $"{SpeedKmh} km/h" : "–";

    public string DelayDisplay
    {
        get
        {
            if (DelaySeconds is not int seconds)
            {
                return "–";
            }

            if (seconds == 0)
            {
                return "pünktlich";
            }

            var minutes = seconds / 60;
            if (minutes == 0)
            {
                return seconds > 0 ? $"+{seconds} s" : $"{seconds} s";
            }

            return seconds > 0 ? $"+{minutes} min" : $"{minutes} min";
        }
    }

    public string BatteryDisplay => BatteryLevel is >= 0 and <= 100 ? $"{BatteryLevel} %" : "–";

    public string AppVersionDisplay => string.IsNullOrWhiteSpace(AppVersion) ? "–" : AppVersion;

    public string RoutesExportVersionDisplay =>
        RoutesExportPackageVersion is > 0 ? RoutesExportPackageVersion.Value.ToString() : "–";

    public string RoutesUpdateVersionDisplay =>
        RoutesUpdatePackageVersion is > 0 ? RoutesUpdatePackageVersion.Value.ToString() : "–";

    public string? StreetAddress
    {
        get => _streetAddress;
        set => SetField(ref _streetAddress, value, nameof(StreetDisplay));
    }

    public string StreetDisplay => string.IsNullOrWhiteSpace(StreetAddress) ? "–" : StreetAddress;

    public string PositionDisplay => $"{Latitude:F5}°, {Longitude:F5}°";

    public string AccuracyDisplay =>
        double.IsFinite(AccuracyM) && AccuracyM > 0 ? $"{AccuracyM:0} m" : "–";

    public string DestinationDisplay => string.IsNullOrWhiteSpace(Destination) ? "–" : Destination;

    public string RouteDisplay => string.IsNullOrWhiteSpace(RouteName) ? "–" : RouteName;

    public string StopDisplay => string.IsNullOrWhiteSpace(StopName) ? "–" : StopName;

    public string PhoneDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(PhoneRaw))
            {
                return PhoneRaw;
            }

            return string.IsNullOrWhiteSpace(PhoneNormalized) ? "–" : PhoneNormalized;
        }
    }

    private bool SetField<T>(ref T field, T value, params string[] additionalProperties)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged();
        foreach (var name in additionalProperties)
        {
            OnPropertyChanged(name);
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
