using SIPSorcery.Net;
using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.Voip;

public sealed class VoipWebRtcCoordinator : IDisposable
{
    private const int ConnectTimeoutSeconds = 50;
    private const string ConnectTimeoutMessage =
        "Sprechverbindung konnte nicht aufgebaut werden (TURN/ICE prüfen – VoIP-Config nach Dropbox, Tablet neu laden)";

    private readonly VoipSettings _settings;
    private readonly Action<VoipSignalMessage> _sendSignal;
    private readonly object _callGate = new();
    private VoipWebRtcCall? _activeCall;
    private string? _pendingCallId;
    private string? _pendingRemotePeerId;
    private string? _pendingRemoteDisplayName;
    private string? _activeCallId;
    private string? _activeRemotePeerId;
    private CancellationTokenSource? _connectTimeoutCts;

    public VoipCallStatus CallStatus { get; private set; } = new();

    public event Action? CallStatusChanged;

    public VoipWebRtcCoordinator(VoipSettings settings, Action<VoipSignalMessage> sendSignal)
    {
        _settings = settings;
        _sendSignal = sendSignal;
    }

    public void RegisterOutgoingCall(string callId, string remotePeerId, string? remoteDisplayName = null)
    {
        lock (_callGate)
        {
            _pendingCallId = callId;
            _pendingRemotePeerId = remotePeerId;
            _pendingRemoteDisplayName = remoteDisplayName;
            SetCallStatus(VoipCallConnectionState.Outgoing, remotePeerId, remoteDisplayName);
        }
    }

    public async Task HandleSignalAsync(VoipSignalMessage message)
    {
        switch (message.Type)
        {
            case VoipSignalTypes.Accept:
            {
                string callId;
                string remotePeerId;
                lock (_callGate)
                {
                    callId = _pendingCallId ?? message.CallId ?? string.Empty;
                    remotePeerId = _pendingRemotePeerId ?? message.From ?? string.Empty;
                }

                if (string.IsNullOrEmpty(callId) || string.IsNullOrEmpty(remotePeerId))
                {
                    return;
                }

                await StartCallAsync(callId, remotePeerId, isCaller: true).ConfigureAwait(false);
                break;
            }

            case VoipSignalTypes.SdpOffer:
                if (!string.IsNullOrWhiteSpace(message.Sdp))
                {
                    if (_activeCall is null && !string.IsNullOrEmpty(message.CallId) &&
                        !string.IsNullOrEmpty(message.From))
                    {
                        await StartCallAsync(message.CallId, message.From, isCaller: false)
                            .ConfigureAwait(false);
                    }

                    await (_activeCall?.HandleRemoteOfferAsync(message.Sdp) ?? Task.CompletedTask)
                        .ConfigureAwait(false);
                }
                break;

            case VoipSignalTypes.SdpAnswer:
                if (!string.IsNullOrWhiteSpace(message.Sdp))
                {
                    _activeCall?.HandleRemoteAnswer(message.Sdp);
                }
                break;

            case VoipSignalTypes.IceCandidate:
                if (!string.IsNullOrWhiteSpace(message.Candidate))
                {
                    _activeCall?.HandleRemoteIceCandidate(
                        message.Candidate,
                        message.SdpMid,
                        message.SdpMLineIndex ?? 0);
                }
                break;

            case VoipSignalTypes.Hangup:
            case VoipSignalTypes.CallEnded:
                EndCall();
                break;
        }
    }

    private async Task StartCallAsync(string callId, string remotePeerId, bool isCaller)
    {
        VoipWebRtcCall call;
        lock (_callGate)
        {
            EndCall(notifyEnded: false);
            _activeCallId = callId;
            _activeRemotePeerId = remotePeerId;
            SetCallStatus(VoipCallConnectionState.Connecting, remotePeerId, _pendingRemoteDisplayName);
            StartConnectTimeout();
            call = new VoipWebRtcCall(
                _settings,
                callId,
                remotePeerId,
                VoipConstants.RoleDispatch,
                isCaller,
                _sendSignal,
                OnPeerConnectionStateChanged);
            _activeCall = call;
            _pendingCallId = null;
            _pendingRemotePeerId = null;
        }

        await call.StartAsync().ConfigureAwait(false);
    }

    private void OnPeerConnectionStateChanged(RTCPeerConnectionState state)
    {
        _ = Task.Run(() =>
        {
            try
            {
                ProcessConnectionState(state);
            }
            catch
            {
                // WebRTC-Fehler dürfen die Leitstelle nicht beenden.
            }
        });
    }

    private void ProcessConnectionState(RTCPeerConnectionState state)
    {
        lock (_callGate)
        {
            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    CancelConnectTimeout();
                    SetCallStatus(
                        VoipCallConnectionState.Connected,
                        CallStatus.RemotePeerId,
                        CallStatus.RemoteDisplayName);
                    break;
                case RTCPeerConnectionState.failed:
                    CancelConnectTimeout();
                    FailActiveCall(ConnectTimeoutMessage);
                    break;
                case RTCPeerConnectionState.closed:
                    CancelConnectTimeout();
                    if (CallStatus.IsActive)
                    {
                        SetCallStatus(
                            VoipCallConnectionState.Ended,
                            CallStatus.RemotePeerId,
                            CallStatus.RemoteDisplayName);
                    }

                    _activeCall?.Dispose();
                    _activeCall = null;
                    _activeCallId = null;
                    _activeRemotePeerId = null;
                    break;
            }
        }
    }

    private void StartConnectTimeout()
    {
        CancelConnectTimeout();
        _connectTimeoutCts = new CancellationTokenSource();
        var token = _connectTimeoutCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ConnectTimeoutSeconds), token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                lock (_callGate)
                {
                    if (CallStatus.State is not VoipCallConnectionState.Connecting
                        and not VoipCallConnectionState.Outgoing)
                    {
                        return;
                    }

                    FailActiveCall(ConnectTimeoutMessage);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }, token);
    }

    private void CancelConnectTimeout()
    {
        try
        {
            _connectTimeoutCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _connectTimeoutCts?.Dispose();
        _connectTimeoutCts = null;
    }

    private void FailActiveCall(string message)
    {
        if (!string.IsNullOrEmpty(_activeCallId) && !string.IsNullOrEmpty(_activeRemotePeerId))
        {
            _sendSignal(new VoipSignalMessage
            {
                Type = VoipSignalTypes.Hangup,
                CallId = _activeCallId,
                From = VoipConstants.RoleDispatch,
                To = _activeRemotePeerId
            });
        }

        _activeCall?.Dispose();
        _activeCall = null;
        _activeCallId = null;
        _activeRemotePeerId = null;
        SetCallStatus(
            VoipCallConnectionState.Failed,
            CallStatus.RemotePeerId,
            CallStatus.RemoteDisplayName,
            message);
    }

    public void HangUpActiveCall()
    {
        lock (_callGate)
        {
            CancelConnectTimeout();
            if (!string.IsNullOrEmpty(_activeCallId) && !string.IsNullOrEmpty(_activeRemotePeerId))
            {
                _sendSignal(new VoipSignalMessage
                {
                    Type = VoipSignalTypes.Hangup,
                    CallId = _activeCallId,
                    From = VoipConstants.RoleDispatch,
                    To = _activeRemotePeerId
                });
            }

            EndCall();
        }
    }

    public void SetMicrophoneTransmitEnabled(bool enabled) =>
        _activeCall?.SetMicrophoneTransmitEnabled(enabled);

    private void SetCallStatus(
        VoipCallConnectionState state,
        string? remotePeerId,
        string? remoteDisplayName,
        string? failureMessage = null)
    {
        CallStatus = new VoipCallStatus
        {
            State = state,
            RemotePeerId = remotePeerId,
            RemoteDisplayName = remoteDisplayName,
            FailureMessage = failureMessage
        };
        CallStatusChanged?.Invoke();
    }

    public void EndCall() => EndCall(notifyEnded: true);

    private void EndCall(bool notifyEnded)
    {
        CancelConnectTimeout();
        _activeCall?.Dispose();
        _activeCall = null;
        _activeCallId = null;
        _activeRemotePeerId = null;
        if (notifyEnded && CallStatus.IsActive)
        {
            SetCallStatus(VoipCallConnectionState.Ended, CallStatus.RemotePeerId, CallStatus.RemoteDisplayName);
        }
        else if (!CallStatus.IsActive)
        {
            SetCallStatus(VoipCallConnectionState.Idle, null, null);
        }
    }

    public void Dispose()
    {
        lock (_callGate)
        {
            EndCall(notifyEnded: false);
        }
    }
}
