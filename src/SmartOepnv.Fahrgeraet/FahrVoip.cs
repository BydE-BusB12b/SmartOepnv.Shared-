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
    public VoipBlick Blick => _blick ??= Baue();
    public event Action? Changed;

    private VoipBlick? _blick;
    private string _hinweis = "";
    private string _configDatei = "";
    private string? _signalingAktiv;
    private DateTime? _letzterAbruf;
    private bool _registered;
    private int _neu;
    private int _uebernommen;

    public async Task ConnectAsync(string devicePhone)
    {
        _phone = new string(devicePhone.Where(char.IsDigit).ToArray());
        _stopped = false;
        await ConnectOnceAsync();
    }

    public Task NeuLadenAsync(string devicePhone)
    {
        Interlocked.Increment(ref _neu);
        return ConnectAsync(devicePhone);
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
            var erzwingen = Volatile.Read(ref _neu) != _uebernommen;
            if (!erzwingen && _socket is { State: WebSocketState.Open })
            {
                return;
            }

            _uebernommen = Volatile.Read(ref _neu);
            if (_socket is not null)
            {
                _generation++;
                _registered = false;
                await CloseCallAsync("Config wird neu geladen.");
                await CloseSocketAsync();
            }

            if (_phone.Length == 0)
            {
                _hinweis = "Keine KOM-Telefonnummer – Gerät muss registriert sein.";
                _config = null;
                _configDatei = "";
                Set("Gerätetelefon fehlt.");
                return;
            }

            _letzterAbruf = DateTime.Now;
            var namen = ConfigNamen(_phone);
            _configDatei = namen[0];
            var client = OpenDropbox();
            if (client is null)
            {
                _config = null;
                _hinweis = "Dropbox ist nicht verbunden.";
                Set("Dropbox ist nicht verbunden.");
                return;
            }

            string? json = null;
            foreach (var name in namen)
            {
                json = await client.TryDownloadNamedFileAsync(name);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    _configDatei = name;
                    break;
                }
            }

            _config = json is null
                ? null
                : JsonSerializer.Deserialize<VoipFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (_config is null || string.IsNullOrWhiteSpace(_config.SignalingUrl) || string.IsNullOrWhiteSpace(_config.PeerId))
            {
                _hinweis = json is null
                    ? "Keine VoIP-Config in Dropbox gefunden.\nGesucht: " + string.Join(", ", namen) + "\nLeitstelle: VoIP-Config nach Dropbox."
                    : _configDatei + " ungültig (signalingUrl/peerId fehlt).";
                _config = null;
                Set(_hinweis);
                return;
            }

            _hinweis = "Config geladen, verbinde Signaling …";

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
                    _signalingAktiv = url;
                    _registered = false;
                    await SendAsync(new
                    {
                        type = "register",
                        peerId = _config.PeerId,
                        displayName = _config.DisplayName ?? _config.PeerId,
                        role = _config.Role ?? "vehicle"
                    });
                    _hinweis = "Signaling verbunden, warte auf Registrierung …";
                    Set("Signaling verbunden, warte auf Registrierung.");
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

            _hinweis = last?.Message ?? "Offline – Verbindung zur Leitstelle fehlgeschlagen.";
            Set(_hinweis);
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
            _hinweis = ex.Message;
            Set(ex.Message);
        }

        _registered = false;
        if (!_stopped && generation == _generation)
        {
            _hinweis = "Signaling getrennt – Leitstelle erreichbar?";
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
                _registered = true;
                _hinweis = _config?.PeerId is { Length: > 0 } peer
                    ? "Registriert bei Leitstelle (" + peer + ")"
                    : "Registriert bei Leitstelle";
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
        _ = Task.Run(IvuTon.Funk);
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
        _blick = Baue();
        Changed?.Invoke();
    }

    private VoipBlick Baue()
    {
        var telefon = _phone.Length == 0 ? "—" : _phone;
        var datei = _configDatei.Length > 0
            ? _configDatei
            : _phone.Length == 0 ? "—" : "voip_config_" + _phone + ".json";
        string farbe;
        string zustand;
        if (_phone.Length == 0)
        {
            farbe = "#F44336";
            zustand = "Keine KOM-Telefonnummer.\nGerät muss in der Leitstelle registriert sein.";
        }
        else if (_config is null || string.IsNullOrWhiteSpace(_config.PeerId))
        {
            farbe = "#FF9800";
            zustand = "Warte auf voip_config_" + _phone + ".json aus Dropbox.\n" +
                (_hinweis.Length == 0 ? "Leitstelle: VoIP-Einstellungen speichern." : _hinweis);
        }
        else if (_call is not null)
        {
            farbe = "#4CAF50";
            var name = string.IsNullOrWhiteSpace(_config.DisplayName) ? _config.PeerId : _config.DisplayName;
            zustand = "Im Gespräch – " + name;
        }
        else if (_socket is { State: WebSocketState.Open } && _registered)
        {
            farbe = "#4CAF50";
            zustand = "Online – bereit für Anruf von der Leitstelle.\n" + (_signalingAktiv ?? _config.SignalingUrl);
        }
        else if (_socket is { State: WebSocketState.Open })
        {
            farbe = "#FF9800";
            zustand = "Signaling verbunden, Registrierung läuft …";
        }
        else
        {
            farbe = "#F44336";
            zustand = _hinweis.Length == 0 ? "Offline – Verbindung zur Leitstelle fehlgeschlagen." : _hinweis;
        }

        return new VoipBlick(
            farbe,
            zustand,
            telefon,
            datei,
            Text(_config?.DisplayName),
            Text(_config?.PeerId),
            Text(_config?.SignalingUrl),
            _letzterAbruf?.ToString("dd.MM.yyyy HH:mm:ss"),
            _config?.UpdatedAt > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(_config.UpdatedAt).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
                : null,
            Text(_hinweis));
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<string> ConfigNamen(string phone)
    {
        var varianten = new List<string>();
        void Dazu(string wert)
        {
            if (wert.Length > 0 && !varianten.Contains(wert))
            {
                varianten.Add(wert);
            }
        }

        Dazu(phone);
        if (phone.StartsWith('0') && phone.Length > 1)
        {
            Dazu("49" + phone[1..]);
        }

        if (phone.StartsWith("49") && phone.Length > 2)
        {
            Dazu("0" + phone[2..]);
        }

        var norm = phone;
        if (norm.StartsWith("00"))
        {
            norm = norm[2..];
        }

        if (norm.StartsWith('0') && norm.Length > 1)
        {
            norm = "49" + norm[1..];
        }

        Dazu(norm);
        return varianten.Select(wert => "voip_config_" + wert + ".json").ToList();
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
        public long UpdatedAt { get; set; }
    }
}

public readonly record struct VoipBlick(
    string Farbe,
    string Zustand,
    string Telefon,
    string ConfigDatei,
    string? Geraetename,
    string? Peer,
    string? Signaling,
    string? LetzterAbruf,
    string? ConfigStand,
    string? Details);
