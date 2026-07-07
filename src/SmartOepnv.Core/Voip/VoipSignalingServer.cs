using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SmartOepnv.Core.Voip;

public sealed class VoipSignalingServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConcurrentDictionary<string, VoipConnectedPeer> _peers = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public event Action<VoipConnectedPeer>? PeerRegistered;
    public event Action<string>? PeerDisconnected;
    public event Action<VoipSignalMessage>? CallReceived;

    public IReadOnlyCollection<VoipConnectedPeer> OnlinePeers =>
        _peers.Values.ToList();

    public bool IsRunning { get; private set; }

    public string? LastError { get; private set; }

    public void Start(VoipSettings settings)
    {
        Stop();
        if (!settings.Enabled)
        {
            return;
        }

        if (!TryStartListener(settings, out var startError))
        {
            LastError = startError;
            _listener = null;
            return;
        }

        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        IsRunning = true;
        LastError = null;
    }

    private bool TryStartListener(VoipSettings settings, out string? error)
    {
        error = null;
        var wantsAllAdapters = VoipHttpListenHelper.ResolveBindHost(settings.ListenHost) == "+";

        foreach (var group in VoipHttpListenHelper.BuildListenPrefixGroups(settings))
        {
            _listener?.Close();
            _listener = new HttpListener();
            foreach (var prefix in group)
            {
                _listener.Prefixes.Add(prefix);
            }

            try
            {
                _listener.Start();
                var usingLocalOnly = group.All(p =>
                    p.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase));
                if (wantsAllAdapters && usingLocalOnly)
                {
                    error =
                        "VoIP-Signaling nur lokal (127.0.0.1) – für Tablets im WLAN unter Funk Port freigeben.";
                }

                return true;
            }
            catch (Exception ex)
            {
                error = VoipHttpListenHelper.FormatStartFailure(settings, ex);
                _listener.Close();
                _listener = null;
            }
        }

        return false;
    }

    public void Stop() => _ = StopAsync();

    public async Task StopAsync(CancellationToken ct = default)
    {
        IsRunning = false;
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_listener?.IsListening == true)
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // ignore
            }
        }

        foreach (var peer in _peers.Values.ToList())
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await peer.Socket.CloseAsync(
                    WebSocketCloseStatus.EndpointUnavailable,
                    "server_stop",
                    timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _peers.Clear();
        _listener?.Close();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public Task<string> SendCallAsync(
        string fromPeerId,
        string toPeerId,
        string fromDisplayName,
        CancellationToken ct = default) =>
        SendCallAsync(fromPeerId, toPeerId, fromDisplayName, callId: null, ct);

    public Task<string> SendCallAsync(
        string fromPeerId,
        string toPeerId,
        string fromDisplayName,
        string? callId,
        CancellationToken ct = default)
    {
        fromPeerId = VoipPhone.NormalizePeerId(fromPeerId);
        toPeerId = VoipPhone.NormalizePeerId(toPeerId);
        if (!_peers.TryGetValue(fromPeerId, out var caller))
        {
            throw new InvalidOperationException($"Anrufer nicht verbunden: {fromPeerId}");
        }

        if (!_peers.TryGetValue(toPeerId, out var target))
        {
            throw new InvalidOperationException($"Ziel nicht online: {toPeerId}");
        }

        callId = string.IsNullOrWhiteSpace(callId) ? Guid.NewGuid().ToString("N") : callId;
        var outgoing = new VoipSignalMessage
        {
            Type = VoipSignalTypes.IncomingCall,
            CallId = callId,
            From = fromPeerId,
            To = toPeerId,
            FromName = fromDisplayName
        };
        return SendAndNotifyAsync(caller, target, outgoing, callId, ct);
    }

    private async Task<string> SendAndNotifyAsync(
        VoipConnectedPeer caller,
        VoipConnectedPeer target,
        VoipSignalMessage outgoing,
        string callId,
        CancellationToken ct)
    {
        await SendAsync(target, outgoing, ct).ConfigureAwait(false);

        CallReceived?.Invoke(new VoipSignalMessage
        {
            Type = VoipSignalTypes.Call,
            CallId = callId,
            From = outgoing.From,
            To = outgoing.To,
            FromName = outgoing.FromName
        });

        return callId;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener is { IsListening: true })
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException ex)
                {
                    LastError = $"Signaling-Server beendet: {ex.Message}";
                    break;
                }

                _ = Task.Run(() => HandleContextAsync(context, ct), CancellationToken.None);
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsRunning = false;
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (!IsSignalingPath(path))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                var body = Encoding.UTF8.GetBytes("Smart-OEPNV VoIP Signaling bereit.");
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            var wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
            var peer = new VoipConnectedPeer(wsContext.WebSocket);
            await ReceiveLoopAsync(peer, ct).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task ReceiveLoopAsync(VoipConnectedPeer peer, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (peer.Socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await peer.Socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await HandleMessageAsync(peer, json, ct).ConfigureAwait(false);
        }

        UnregisterPeer(peer);
    }

    private async Task HandleMessageAsync(VoipConnectedPeer peer, string json, CancellationToken ct)
    {
        VoipSignalMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<VoipSignalMessage>(json, JsonOptions);
        }
        catch
        {
            return;
        }

        if (msg is null || string.IsNullOrWhiteSpace(msg.Type))
        {
            return;
        }

        switch (msg.Type)
        {
            case VoipSignalTypes.Register:
                await RegisterPeerAsync(peer, msg, ct).ConfigureAwait(false);
                break;
            case VoipSignalTypes.Ping:
                await SendAsync(peer, new VoipSignalMessage { Type = VoipSignalTypes.Pong }, ct).ConfigureAwait(false);
                break;
            case VoipSignalTypes.Call:
                await ForwardCallAsync(peer, msg, ct).ConfigureAwait(false);
                break;
            case VoipSignalTypes.Accept:
            case VoipSignalTypes.Reject:
            case VoipSignalTypes.Hangup:
            case VoipSignalTypes.SdpOffer:
            case VoipSignalTypes.SdpAnswer:
            case VoipSignalTypes.IceCandidate:
                await ForwardToPeerAsync(msg, ct).ConfigureAwait(false);
                if (msg.Type == VoipSignalTypes.Accept)
                {
                    CallReceived?.Invoke(msg);
                }
                break;
        }
    }

    private async Task RegisterPeerAsync(VoipConnectedPeer peer, VoipSignalMessage msg, CancellationToken ct)
    {
        var peerId = VoipPhone.NormalizePeerId(msg.PeerId);
        if (string.IsNullOrEmpty(peerId))
        {
            await SendAsync(peer, new VoipSignalMessage
            {
                Type = VoipSignalTypes.Error,
                Message = "peerId fehlt"
            }, ct).ConfigureAwait(false);
            return;
        }

        UnregisterPeer(peer);
        peer.PeerId = peerId;
        peer.DisplayName = msg.DisplayName?.Trim() ?? peerId;
        peer.Role = msg.Role?.Trim() ?? VoipConstants.RoleVehicle;
        _peers[peerId] = peer;

        await SendAsync(peer, new VoipSignalMessage
        {
            Type = VoipSignalTypes.Registered,
            PeerId = peerId
        }, ct).ConfigureAwait(false);

        PeerRegistered?.Invoke(peer);
        await BroadcastPeerStateAsync(peerId, online: true, ct).ConfigureAwait(false);
        await SyncPeerDirectoryAsync(peer, ct).ConfigureAwait(false);
    }

    private async Task SyncPeerDirectoryAsync(VoipConnectedPeer peer, CancellationToken ct)
    {
        foreach (var other in _peers.Values)
        {
            if (ReferenceEquals(other, peer) ||
                string.IsNullOrWhiteSpace(other.PeerId) ||
                string.Equals(other.PeerId, peer.PeerId, StringComparison.Ordinal))
            {
                continue;
            }

            await SendAsync(peer, new VoipSignalMessage
            {
                Type = VoipSignalTypes.PeerOnline,
                PeerId = other.PeerId,
                DisplayName = other.DisplayName,
                Role = other.Role
            }, ct).ConfigureAwait(false);
        }
    }

    private async Task ForwardCallAsync(VoipConnectedPeer caller, VoipSignalMessage msg, CancellationToken ct)
    {
        var to = VoipPhone.NormalizePeerId(msg.To);
        var from = VoipPhone.NormalizePeerId(msg.From);
        if (string.IsNullOrEmpty(from))
        {
            from = caller.PeerId;
        }
        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(from))
        {
            return;
        }

        if (!TryResolveConnectedPeer(to, out var target))
        {
            await SendAsync(caller, new VoipSignalMessage
            {
                Type = VoipSignalTypes.Error,
                Message = "Ziel nicht online"
            }, ct).ConfigureAwait(false);
            return;
        }

        var callId = string.IsNullOrWhiteSpace(msg.CallId) ? Guid.NewGuid().ToString("N") : msg.CallId;
        await SendAsync(target, new VoipSignalMessage
        {
            Type = VoipSignalTypes.IncomingCall,
            CallId = callId,
            From = from,
            To = to,
            FromName = msg.FromName ?? caller.DisplayName
        }, ct).ConfigureAwait(false);

        CallReceived?.Invoke(new VoipSignalMessage
        {
            Type = VoipSignalTypes.Call,
            CallId = callId,
            From = from,
            To = to,
            FromName = msg.FromName ?? caller.DisplayName
        });
    }

    private async Task ForwardToPeerAsync(VoipSignalMessage msg, CancellationToken ct)
    {
        var to = VoipPhone.NormalizePeerId(msg.To);
        if (string.IsNullOrEmpty(to) || !TryResolveConnectedPeer(to, out var target))
        {
            var from = VoipPhone.NormalizePeerId(msg.From);
            if (!string.IsNullOrEmpty(from) && _peers.TryGetValue(from, out var caller))
            {
                await SendAsync(caller, new VoipSignalMessage
                {
                    Type = VoipSignalTypes.Error,
                    Message = $"Signaling-Ziel nicht online: {msg.To}"
                }, ct).ConfigureAwait(false);
            }

            return;
        }

        await SendAsync(target, msg, ct).ConfigureAwait(false);
        if (msg.Type == VoipSignalTypes.Hangup)
        {
            CallReceived?.Invoke(new VoipSignalMessage
            {
                Type = VoipSignalTypes.CallEnded,
                CallId = msg.CallId,
                From = msg.From,
                To = msg.To
            });
        }
    }

    private async Task BroadcastPeerStateAsync(string peerId, bool online, CancellationToken ct)
    {
        var payload = new VoipSignalMessage
        {
            Type = online ? VoipSignalTypes.PeerOnline : VoipSignalTypes.PeerOffline,
            PeerId = peerId
        };

        foreach (var peer in _peers.Values.Where(p => !string.Equals(p.PeerId, peerId, StringComparison.Ordinal)))
        {
            await SendAsync(peer, payload, ct).ConfigureAwait(false);
        }
    }

    private void UnregisterPeer(VoipConnectedPeer peer)
    {
        if (string.IsNullOrEmpty(peer.PeerId))
        {
            return;
        }

        if (_peers.TryRemove(peer.PeerId, out _))
        {
            PeerDisconnected?.Invoke(peer.PeerId);
            _ = BroadcastPeerStateAsync(peer.PeerId, online: false, CancellationToken.None);
        }
    }

    private bool TryResolveConnectedPeer(string toPeerId, out VoipConnectedPeer peer)
    {
        var to = VoipPhone.NormalizePeerId(toPeerId);
        if (!string.IsNullOrEmpty(to) && _peers.TryGetValue(to, out peer!))
        {
            return true;
        }

        foreach (var candidate in _peers)
        {
            if (VoipPhone.Match(candidate.Key, toPeerId))
            {
                peer = candidate.Value;
                return true;
            }
        }

        peer = null!;
        return false;
    }

    private static async Task SendAsync(VoipConnectedPeer peer, VoipSignalMessage message, CancellationToken ct)
    {
        if (peer.Socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await peer.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static bool IsSignalingPath(string path)
    {
        var normalized = path.TrimEnd('/');
        var expected = VoipConstants.SignalingWebSocketPath.TrimEnd('/');
        return normalized.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Stop();
}

public sealed class VoipConnectedPeer
{
    public VoipConnectedPeer(WebSocket socket) => Socket = socket;

    public WebSocket Socket { get; }
    public string PeerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = VoipConstants.RoleVehicle;
}
