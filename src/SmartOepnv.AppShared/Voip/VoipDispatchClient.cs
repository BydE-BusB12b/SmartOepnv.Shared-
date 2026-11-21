using System.Collections.Concurrent;
using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.Voip;

/// <summary>Leitstelle als dispatch-Peer am Signaling-Server (lokal oder Cloud).</summary>
public sealed class VoipDispatchClient : IDisposable
{
    private readonly VoipWebRtcCoordinator _coordinator;
    private readonly VoipWebSocketClient _client;
    private readonly ConcurrentDictionary<string, VoipOnlinePeerInfo> _onlinePeers = new(StringComparer.Ordinal);

    public VoipDispatchClient(string signalingUrl, VoipWebRtcCoordinator coordinator)
    {
        _coordinator = coordinator;
        _client = new VoipWebSocketClient(signalingUrl);
        _client.MessageReceived += message =>
        {
            TrackOnlinePeer(message);
            _ = HandleMessageSafeAsync(message);
        };
    }

    public bool IsConnected => _client.IsConnected;

    public IReadOnlyCollection<VoipOnlinePeerInfo> OnlinePeers =>
        _onlinePeers.Values.ToList();

    public event Action? OnlinePeersChanged;

    private async Task HandleMessageSafeAsync(VoipSignalMessage message)
    {
        try
        {
            await _coordinator.HandleSignalAsync(message).ConfigureAwait(false);
        }
        catch
        {
            // WebRTC-Fehler dürfen die Leitstelle nicht beenden.
        }
    }

    public Task ConnectAsync(CancellationToken ct = default) =>
        _client.ConnectAndRegisterAsync(
            VoipConstants.RoleDispatch,
            VoipConstants.RoleDispatch,
            "Leitstelle",
            ct);

    public Task SendCallAsync(
        string toPeerId,
        string displayName,
        string callId,
        CancellationToken ct = default) =>
        SendAsync(
            new VoipSignalMessage
            {
                Type = VoipSignalTypes.Call,
                From = VoipConstants.RoleDispatch,
                To = toPeerId,
                FromName = displayName,
                CallId = callId
            },
            ct);

    public Task SendAsync(VoipSignalMessage message, CancellationToken ct = default) =>
        _client.SendAsync(message, ct);

    private void TrackOnlinePeer(VoipSignalMessage message)
    {
        var peerId = VoipPhone.NormalizePeerId(message.PeerId);
        if (string.IsNullOrEmpty(peerId) ||
            string.Equals(peerId, VoipConstants.RoleDispatch, StringComparison.Ordinal))
        {
            return;
        }

        switch (message.Type)
        {
            case VoipSignalTypes.PeerOnline:
                _onlinePeers[peerId] = new VoipOnlinePeerInfo
                {
                    PeerId = peerId,
                    DisplayName = string.IsNullOrWhiteSpace(message.DisplayName) ? peerId : message.DisplayName.Trim(),
                    Role = string.IsNullOrWhiteSpace(message.Role) ? VoipConstants.RoleVehicle : message.Role.Trim()
                };
                OnlinePeersChanged?.Invoke();
                break;
            case VoipSignalTypes.PeerOffline:
                if (_onlinePeers.TryRemove(peerId, out _))
                {
                    OnlinePeersChanged?.Invoke();
                }

                break;
        }
    }

    public void Dispose() => _client.Dispose();
}
