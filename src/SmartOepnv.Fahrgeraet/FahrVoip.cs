using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Fahrgeraet;

public sealed class FahrVoip : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _loop;
    private VoipFile? _config;
    private FahrVoipCall? _call;
    private string? _pendingOffer;
    private string _phone = "";
    private bool _stopped;
    private int _generation;

    private static readonly JsonSerializerOptions SignalJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Status { get; private set; } = "Offline";
    public string? IncomingFrom { get; private set; }
    public event Action? Changed;

    public async Task ConnectAsync(string devicePhone)
    {
        _phone = new string(devicePhone.Where(char.IsDigit).ToArray());
        _stopped = false;
        await ConnectOnceAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _stopped = true;
        _loop?.Cancel();
        await CloseCallAsync("Getrennt");
        await CloseSocketAsync();
    }

    private async Task ConnectOnceAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_socket is { State: WebSocketState.Open })
            {
                return;
            }

            if (_phone.Length == 0)
            {
                Set("Gerätetelefon fehlt.");
                return;
            }

            var client = OpenDropbox();
            if (client is null)
            {
                Set("Dropbox ist nicht verbunden.");
                return;
            }

            string? json = null;
            foreach (var name in new[] { $"voip_config_{_phone}.json", $"voip_config_0{_phone}.json" })
            {
                json = await client.TryDownloadNamedFileAsync(name);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    break;
                }
            }

            _config = json is null
                ? null
                : JsonSerializer.Deserialize<VoipFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (_config is null || string.IsNullOrWhiteSpace(_config.SignalingUrl) || string.IsNullOrWhiteSpace(_config.PeerId))
            {
                Set($"voip_config_{_phone}.json fehlt.");
                return;
            }

            var urls = new[] { _config.SignalingUrl, _config.SignalingUrlFallback }
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url!.Trim().TrimEnd('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Exception? last = null;
            foreach (var url in urls)
            {
                try
                {
                    await CloseSocketAsync();
                    _socket = new ClientWebSocket();
                    _loop = new CancellationTokenSource();
                    await _socket.ConnectAsync(new Uri(url), _loop.Token);
                    await SendAsync(new
                    {
                        type = "register",
                        peerId = _config.PeerId,
                        displayName = _config.DisplayName ?? _config.PeerId,
                        role = _config.Role ?? "vehicle"
                    });
                    Set("Angemeldet, warte auf die Leitstelle.");
                    var token = _loop.Token;
                    var generation = ++_generation;
                    _ = ReceiveLoop(token, generation);
                    _ = KeepAlive(token);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            Set(last?.Message ?? "Signalisierung nicht erreichbar.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReceiveLoop(CancellationToken ct, int generation)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (_socket is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
            {
                var text = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                await HandleAsync(text.ToString());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Set(ex.Message);
        }

        if (!_stopped && generation == _generation)
        {
            Set("Signalisierung getrennt, verbinde neu.");
            await Task.Delay(5000, CancellationToken.None);
            if (!_stopped)
            {
                await ConnectOnceAsync();
            }
        }
    }

    private async Task KeepAlive(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                await SendAsync(new { type = "ping", peerId = _config?.PeerId });
            }
        }
        catch (OperationCanceledException)
        {
            // Ende
        }
    }

    private async Task HandleAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : "";
        switch (type)
        {
            case "registered":
                Set("Angemeldet, warte auf die Leitstelle.");
                break;
            case "ping":
                await SendAsync(new { type = "pong", peerId = _config?.PeerId });
                break;
            case "incoming_call":
                await OnIncomingAsync(Read(root, "callId"), Read(root, "from"), Read(root, "fromName"));
                break;
            case "sdp-offer":
                await OnOfferAsync(Read(root, "sdp"));
                break;
            case "ice-candidate":
                _call?.AddIce(Read(root, "candidate") ?? "", Read(root, "sdpMid"), ReadInt(root, "sdpMLineIndex"));
                break;
            case "ptt-down":
                _call?.SetMicEnabled(false);
                Set("Leitstelle spricht.");
                break;
            case "ptt-up":
                _call?.SetMicEnabled(true);
                Set("Sprechverbindung aktiv");
                break;
            case "hangup":
            case "call_ended":
                await CloseCallAsync("Leitstelle hat aufgelegt. Warte auf den nächsten Anruf.");
                break;
            case "error":
                Set(Read(root, "message") ?? "Signalisierungsfehler");
                break;
        }
    }

    private async Task OnIncomingAsync(string? callId, string? from, string? fromName)
    {
        if (_config is null || string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(from))
        {
            return;
        }

        await CloseCallAsync("Neuer Anruf");
        IncomingFrom = string.IsNullOrWhiteSpace(fromName) ? from : fromName;
        Set($"Anruf von {IncomingFrom}");
        await SendAsync(new { type = "accept", callId, to = from, from = _config.PeerId });
        var call = new FahrVoipCall(
            callId,
            from,
            _config.PeerId!,
            _config.TurnUrl,
            _config.TurnUsername,
            _config.TurnPassword,
            SendSignalAsync);
        call.StateChanged += Set;
        _call = call;
        await call.StartAsync();
        if (_pendingOffer is not null)
        {
            var offer = _pendingOffer;
            _pendingOffer = null;
            await call.AcceptOfferAsync(offer);
        }
    }

    private async Task OnOfferAsync(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
        {
            return;
        }

        if (_call is null)
        {
            _pendingOffer = sdp;
            return;
        }

        await _call.AcceptOfferAsync(sdp);
        Set("Verbindung wird aufgebaut.");
    }

    private Task SendSignalAsync(VoipSignal signal) =>
        SendAsync(new
        {
            type = signal.Type,
            callId = signal.CallId,
            from = signal.From,
            to = signal.To,
            sdp = signal.Sdp,
            candidate = signal.Candidate,
            sdpMid = signal.SdpMid,
            sdpMLineIndex = signal.SdpMLineIndex
        });

    private async Task CloseCallAsync(string status)
    {
        var call = _call;
        _call = null;
        _pendingOffer = null;
        IncomingFrom = null;
        if (call is not null)
        {
            await call.DisposeAsync();
        }

        Set(status);
    }

    private void Set(string status)
    {
        Status = status;
        Changed?.Invoke();
    }

    private async Task SendAsync(object payload)
    {
        if (_socket is not { State: WebSocketState.Open })
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SignalJson);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task CloseSocketAsync()
    {
        _loop?.Cancel();
        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch
            {
                // schon zu
            }
        }

        _socket?.Dispose();
        _socket = null;
    }

    private static string? Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var node) && node.TryGetInt32(out var value) ? value : 0;

    private static DropboxApiClient? OpenDropbox()
    {
        foreach (var folder in new[] { FahrgeraetRouteSync.AppFolder, "Planer" })
        {
            var store = new DropboxSettingsStore(folder);
            if (store.Load().IsConnected)
            {
                return new DropboxApiClient(store);
            }
        }

        return null;
    }

    private sealed class VoipFile
    {
        public string? PeerId { get; set; }
        public string? DisplayName { get; set; }
        public string? Role { get; set; }
        public string? SignalingUrl { get; set; }
        public string? SignalingUrlFallback { get; set; }
        public string? TurnUrl { get; set; }
        public string? TurnUsername { get; set; }
        public string? TurnPassword { get; set; }
    }
}
