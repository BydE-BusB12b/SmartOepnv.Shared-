using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Voip;
using System.Windows;
using System.Windows.Threading;

namespace SmartOepnv.AppShared.Voip;

/// <summary>Startet Signaling auf der Leitstelle und synchronisiert VoIP-Konfiguration nach Dropbox.</summary>
public sealed class VoipLeitstelleHost : IDisposable
{
    private readonly VoipSettingsStore _settingsStore = new("Leitstelle");
    private readonly VoipConfigPublisher _publisher = new();
    private readonly VoipSignalingServer _signaling = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private VoipDispatchClient? _dispatchClient;
    private VoipWebRtcCoordinator? _webrtc;
    private bool _funnelManagedByHost;

    public VoipSettings Settings { get; private set; } = new();
    public VoipSignalingServer Signaling => _signaling;
    public string? StatusMessage { get; private set; }

    public VoipCallStatus CallStatus => _webrtc?.CallStatus ?? new();

    public VoipLteReachabilityStatus LteReachability => Settings.EvaluateLteReachability();

    public bool IsSignalingReady =>
        Settings.UsesCloudSignaling()
            ? _dispatchClient?.IsConnected == true
            : _signaling.IsRunning;

    public IReadOnlyCollection<VoipOnlinePeerInfo> OnlinePeers =>
        Settings.UsesCloudSignaling()
            ? _dispatchClient?.OnlinePeers ?? Array.Empty<VoipOnlinePeerInfo>()
            : _signaling.OnlinePeers
                .Select(p => new VoipOnlinePeerInfo
                {
                    PeerId = p.PeerId,
                    DisplayName = p.DisplayName,
                    Role = p.Role
                })
                .ToList();

    public event Action? StateChanged;
    public event Action? CallStatusChanged;

    public Task StartAsync(CancellationToken ct = default) => StartInternalAsync(ct);

    public async Task EnsurePortAndStartAsync(CancellationToken ct = default)
    {
        Settings = _settingsStore.Load();
        EnsureManagedCloudSettings();
        if (Settings.UsesLocalSignalingServer() &&
            VoipWindowsPortSetup.IsPortReservationMissing(Settings))
        {
            await VoipWindowsPortSetup.TryEnsurePortReadyAsync(Settings, null, ct).ConfigureAwait(false);
        }

        await StartInternalAsync(ct).ConfigureAwait(false);
    }

    private async Task StartInternalAsync(CancellationToken ct)
    {
        await _startGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Settings = _settingsStore.Load();
            EnsureManagedCloudSettings();
            if (Settings.ConnectivityMode != VoipConnectivityMode.TailscaleFunnel)
            {
                StopManagedFunnelIfNeeded();
            }

            if (string.IsNullOrWhiteSpace(Settings.DepotLanHost))
            {
                var lanHost = VoipReachability.TryGetRecommendedLanHost();
                if (!string.IsNullOrWhiteSpace(lanHost))
                {
                    Settings.DepotLanHost = lanHost;
                    _settingsStore.Save(Settings);
                }
            }

            await _signaling.StopAsync(ct).ConfigureAwait(false);
            StopManagedFunnelIfNeeded();
            _dispatchClient?.Dispose();
            _dispatchClient = null;
            _webrtc?.Dispose();
            _webrtc = null;

            if (!Settings.Enabled)
            {
                StatusMessage = "VoIP deaktiviert.";
                StateChanged?.Invoke();
                return;
            }

            if (!AppServices.Dropbox.Settings.IsConnected)
            {
                StatusMessage = "VoIP: Dropbox nicht verbunden – Konfiguration nicht veröffentlicht.";
            }

            if (Settings.UsesCloudSignaling())
            {
                await StartCloudAsync(ct).ConfigureAwait(false);
                return;
            }

            await StartLocalAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"VoIP-Start fehlgeschlagen: {ex.Message}";
            StateChanged?.Invoke();
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task StartCloudAsync(CancellationToken ct)
    {
        var cloudUrl = Settings.BuildDispatchSignalingUrl();
        if (string.IsNullOrWhiteSpace(cloudUrl))
        {
            StatusMessage = "VoIP Cloud: Server-Host fehlt in den Einstellungen.";
            StateChanged?.Invoke();
            return;
        }

        _webrtc = new VoipWebRtcCoordinator(Settings, SendSignalFromDispatch);
        _webrtc.CallStatusChanged += RaiseCallStatusChangedOnUiThread;
        _dispatchClient = new VoipDispatchClient(cloudUrl, _webrtc);
        _dispatchClient.OnlinePeersChanged += () => StateChanged?.Invoke();

        Exception? dispatchError = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await Task.Delay(attempt * 300, ct).ConfigureAwait(false);
                await _dispatchClient.ConnectAsync(ct).ConfigureAwait(false);
                dispatchError = null;
                break;
            }
            catch (Exception ex)
            {
                dispatchError = ex;
                _dispatchClient.Dispose();
                _dispatchClient = new VoipDispatchClient(cloudUrl, _webrtc);
                _dispatchClient.OnlinePeersChanged += () => StateChanged?.Invoke();
            }
        }

        if (dispatchError is not null)
        {
            StatusMessage = $"VoIP Cloud nicht verbunden ({cloudUrl}): {dispatchError.Message}";
            StateChanged?.Invoke();
            return;
        }

        if (AppServices.Dropbox.Settings.IsConnected)
        {
            _ = PublishConfigsInBackgroundAsync(ct);
        }

        StatusMessage = string.IsNullOrWhiteSpace(StatusMessage) ||
                        StatusMessage.StartsWith("VoIP: Dropbox nicht verbunden", StringComparison.Ordinal)
            ? $"VoIP aktiv – Cloud {Settings.BuildDispatchSignalingUrl()}"
            : StatusMessage;

        StateChanged?.Invoke();
    }

    private async Task StartLocalAsync(CancellationToken ct)
    {
        _signaling.Start(Settings);
        if (!_signaling.IsRunning)
        {
            StatusMessage = $"VoIP-Signaling nicht gestartet: {_signaling.LastError}";
            StateChanged?.Invoke();
            return;
        }

        await EnsureTailscaleFunnelAsync(ct).ConfigureAwait(false);

        _webrtc = new VoipWebRtcCoordinator(Settings, SendSignalFromDispatch);
        _webrtc.CallStatusChanged += RaiseCallStatusChangedOnUiThread;
        _dispatchClient = new VoipDispatchClient(Settings.BuildDispatchSignalingUrl(), _webrtc);

        Exception? dispatchError = null;
        for (var attempt = 1; attempt <= 5 && _dispatchClient?.IsConnected != true; attempt++)
        {
            try
            {
                await Task.Delay(attempt * 300, ct).ConfigureAwait(false);
                await _dispatchClient.ConnectAsync(ct).ConfigureAwait(false);
                dispatchError = null;
                break;
            }
            catch (Exception ex)
            {
                dispatchError = ex;
                _dispatchClient.Dispose();
                _dispatchClient = new VoipDispatchClient(Settings.BuildDispatchSignalingUrl(), _webrtc);
            }
        }

        if (dispatchError is not null)
        {
            StatusMessage =
                $"VoIP-Signaling läuft ({Settings.BuildSignalingUrl()}), " +
                $"interner Dispatch nicht verbunden: {dispatchError.Message}";
            StateChanged?.Invoke();
        }

        if (AppServices.Dropbox.Settings.IsConnected)
        {
            _ = PublishConfigsInBackgroundAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(StatusMessage) ||
            StatusMessage.StartsWith("VoIP: Dropbox nicht verbunden", StringComparison.Ordinal))
        {
            StatusMessage = _signaling.LastError is not null && _signaling.IsRunning
                ? $"VoIP aktiv – Signaling {Settings.BuildSignalingUrl()} ({_signaling.LastError})"
                : $"VoIP aktiv – Signaling {Settings.BuildSignalingUrl()}";
        }

        StateChanged?.Invoke();
    }

    private async Task PublishConfigsInBackgroundAsync(CancellationToken ct)
    {
        try
        {
            await PublishConfigsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var app = Application.Current;
            if (app is null)
            {
                StatusMessage =
                    $"VoIP aktiv – Signaling {Settings.BuildSignalingUrl()}\n" +
                    $"Dropbox-Config Fehler: {ex.Message}";
                StateChanged?.Invoke();
                return;
            }

            await app.Dispatcher.InvokeAsync(() =>
            {
                StatusMessage =
                    $"VoIP aktiv – Signaling {Settings.BuildSignalingUrl()}\n" +
                    $"Dropbox-Config Fehler: {ex.Message}";
                StateChanged?.Invoke();
            }).Task.ConfigureAwait(false);
        }
    }

    public async Task<VoipPublishResult> PublishConfigsAsync(CancellationToken ct = default)
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return new VoipPublishResult
            {
                Warning = "Dropbox nicht verbunden – keine VoIP-Config hochgeladen."
            };
        }

        await _publisher.PublishDispatchAsync(AppServices.Dropbox, Settings, ct).ConfigureAwait(false);

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return new VoipPublishResult
            {
                DispatchPublished = true,
                PublishedAt = DateTimeOffset.Now,
                Warning = "Nur voip_dispatch.json geschrieben – Fahrzeugpaket nicht geladen, keine voip_config_*.json."
            };
        }

        var template = _publisher.BuildVehicleTemplate(Settings);
        var vehicleCount = 0;
        foreach (var vehicle in await SnapshotRegisteredVehiclesAsync(editor, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(vehicle.PhoneNumber))
            {
                continue;
            }

            await _publisher.PublishVehicleAsync(
                AppServices.Dropbox,
                template,
                vehicle.Name,
                vehicle.PhoneNumber,
                ct).ConfigureAwait(false);
            vehicleCount++;
        }

        return new VoipPublishResult
        {
            DispatchPublished = true,
            VehicleCount = vehicleCount,
            PublishedAt = DateTimeOffset.Now
        };
    }

    private static async Task<IReadOnlyList<RegisteredVehicleItem>> SnapshotRegisteredVehiclesAsync(
        EditableRoutePackage editor,
        CancellationToken ct = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return editor.RegisteredVehicles.ToArray();
        }

        return await dispatcher.InvokeAsync(
            () => editor.RegisteredVehicles.ToArray(),
            DispatcherPriority.Background,
            ct).Task.ConfigureAwait(false);
    }

    public async Task PublishVehicleAsync(string displayName, string phoneRaw, CancellationToken ct = default)
    {
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            return;
        }

        var template = _publisher.BuildVehicleTemplate(Settings);
        await _publisher.PublishVehicleAsync(
            AppServices.Dropbox,
            template,
            displayName,
            phoneRaw,
            ct).ConfigureAwait(false);
    }

    public async Task CallVehicleAsync(string phoneRaw, string displayName, CancellationToken ct = default)
    {
        await EnsureDispatchRegisteredAsync(ct).ConfigureAwait(false);
        var peerId = ResolveOnlinePeerId(phoneRaw);
        if (string.IsNullOrEmpty(peerId))
        {
            throw new InvalidOperationException("Fahrzeug nicht online oder Telefonnummer unbekannt.");
        }

        var callId = Guid.NewGuid().ToString("N");
        _webrtc?.RegisterOutgoingCall(callId, peerId, displayName);

        if (Settings.UsesCloudSignaling())
        {
            if (_dispatchClient is null)
            {
                throw new InvalidOperationException("VoIP-Dispatch nicht initialisiert.");
            }

            await _dispatchClient.SendCallAsync(peerId, displayName, callId, ct).ConfigureAwait(false);
            return;
        }

        await _signaling.SendCallAsync(VoipConstants.RoleDispatch, peerId, displayName, callId, ct)
            .ConfigureAwait(false);
    }

    public void HangUpActiveCall() => _webrtc?.HangUpActiveCall();

    public void SetMicrophoneTransmitEnabled(bool enabled) =>
        _webrtc?.SetMicrophoneTransmitEnabled(enabled);

    public void SendDispatchPtt(bool transmitting) =>
        _webrtc?.SendDispatchPtt(transmitting);

    private void RaiseCallStatusChangedOnUiThread()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            CallStatusChanged?.Invoke();
            return;
        }

        dispatcher.BeginInvoke(CallStatusChanged);
    }

    private async Task EnsureDispatchRegisteredAsync(CancellationToken ct)
    {
        if (Settings.UsesCloudSignaling())
        {
            if (_dispatchClient?.IsConnected == true)
            {
                return;
            }
        }
        else if (_signaling.OnlinePeers.Any(p =>
                     string.Equals(p.PeerId, VoipConstants.RoleDispatch, StringComparison.Ordinal)))
        {
            return;
        }

        if (_dispatchClient is null || _webrtc is null)
        {
            throw new InvalidOperationException("VoIP-Dispatch nicht initialisiert – Leitstelle neu starten.");
        }

        _dispatchClient.Dispose();
        _dispatchClient = new VoipDispatchClient(Settings.BuildDispatchSignalingUrl(), _webrtc);
        if (Settings.UsesCloudSignaling())
        {
            _dispatchClient.OnlinePeersChanged += () => StateChanged?.Invoke();
        }

        await _dispatchClient.ConnectAsync(ct).ConfigureAwait(false);
    }

    private void SendSignalFromDispatch(VoipSignalMessage message) =>
        _ = _dispatchClient?.SendAsync(message);

    public void SaveSettings(VoipSettings settings)
    {
        Settings = settings;
        _settingsStore.Save(settings);
        if (AppServices.Dropbox.Settings.IsConnected)
        {
            _ = PublishConfigsInBackgroundAsync(CancellationToken.None);
        }
    }

    private async Task EnsureTailscaleFunnelAsync(CancellationToken ct)
    {
        if (Settings.ConnectivityMode != VoipConnectivityMode.TailscaleFunnel ||
            !Settings.AutoManageTailscaleFunnel)
        {
            return;
        }

        await Task.Run(() =>
        {
            if (!VoipTailscaleFunnel.TryEnsureStarted(
                    Settings.SignalingPort,
                    out var publicHostname,
                    out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    StatusMessage = string.IsNullOrWhiteSpace(StatusMessage)
                        ? $"Tailscale Funnel: {error}"
                        : $"{StatusMessage}\nTailscale Funnel: {error}";
                }

                return;
            }

            _funnelManagedByHost = true;
            if (string.IsNullOrWhiteSpace(publicHostname))
            {
                return;
            }

            var normalized = VoipTailscaleFunnel.NormalizeHostname(publicHostname);
            if (string.Equals(
                    Settings.TailscaleFunnelHost.Trim(),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Settings.TailscaleFunnelHost = normalized;
            _settingsStore.Save(Settings);
            if (AppServices.Dropbox.Settings.IsConnected)
            {
                _ = PublishConfigsInBackgroundAsync(ct);
            }
        }, ct).ConfigureAwait(false);
    }

    private void StopManagedFunnelIfNeeded()
    {
        if (!_funnelManagedByHost)
        {
            return;
        }

        VoipTailscaleFunnel.TryStop(out _);
        _funnelManagedByHost = false;
    }

    private void EnsureManagedCloudSettings()
    {
        if (!Settings.UsesCloudSignaling())
        {
            return;
        }

        var shouldApply = VoipManagedCloud.IsManagedMode(Settings) ||
                          string.Equals(
                              Settings.CloudSignalingHost?.Trim(),
                              VoipManagedCloud.TlsHost,
                              StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(
                              Settings.CloudSignalingHost?.Trim(),
                              VoipManagedCloud.DefaultHost,
                              StringComparison.OrdinalIgnoreCase);

        if (!shouldApply)
        {
            return;
        }

        var oldUrl = Settings.BuildDispatchSignalingUrl();
        var oldTurnHost = Settings.TurnHost?.Trim();
        VoipManagedCloud.ApplyTo(Settings, Settings.TurnPassword);
        if (!string.Equals(Settings.BuildDispatchSignalingUrl(), oldUrl, StringComparison.Ordinal) ||
            !string.Equals(Settings.TurnHost?.Trim(), oldTurnHost, StringComparison.Ordinal))
        {
            _settingsStore.Save(Settings);
        }
    }

    public string? ResolveOnlinePeerId(string? phoneRaw)
    {
        var normalized = VoipPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        foreach (var peer in OnlinePeers)
        {
            if (VoipPhone.Match(peer.PeerId, normalized))
            {
                return peer.PeerId;
            }
        }

        return normalized;
    }

    public void Dispose()
    {
        StopManagedFunnelIfNeeded();
        _webrtc?.Dispose();
        _dispatchClient?.Dispose();
        _signaling.Dispose();
        _startGate.Dispose();
    }
}
