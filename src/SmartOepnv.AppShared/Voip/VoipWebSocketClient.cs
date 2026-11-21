using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.Voip;

public sealed class VoipWebSocketClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _url;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;

    public event Action<VoipSignalMessage>? MessageReceived;

    public VoipWebSocketClient(string url) =>
        _url = NormalizeSignalingUrl(url);

    private static string NormalizeSignalingUrl(string raw)
    {
        var trimmed = raw.Trim().TrimEnd('/');
        return trimmed;
    }

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAndRegisterAsync(
        string peerId,
        string role,
        string displayName,
        CancellationToken ct = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(_url), ct).ConfigureAwait(false);
        _receiveCts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);

        await SendAsync(new VoipSignalMessage
        {
            Type = VoipSignalTypes.Register,
            PeerId = peerId,
            Role = role,
            DisplayName = displayName
        }, ct).ConfigureAwait(false);
    }

    public Task SendAsync(VoipSignalMessage message, CancellationToken ct = default)
    {
        if (_socket?.State != WebSocketState.Open)
        {
            return Task.CompletedTask;
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (_socket?.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
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
            VoipSignalMessage? msg;
            try
            {
                msg = JsonSerializer.Deserialize<VoipSignalMessage>(json, JsonOptions);
            }
            catch
            {
                continue;
            }

            if (msg is not null)
            {
                MessageReceived?.Invoke(msg);
            }
        }
    }

    public async Task DisconnectAsync()
    {
        _receiveCts?.Cancel();
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        if (_socket?.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _socket?.Dispose();
        _socket = null;
        _receiveCts?.Dispose();
        _receiveCts = null;
        _receiveLoop = null;
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }
}
