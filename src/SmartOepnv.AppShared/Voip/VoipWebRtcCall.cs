using System.Net;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.Voip;

/// <summary>WebRTC-Audio für einen Funk-Anruf (Leitstelle-Seite).</summary>
public sealed class VoipWebRtcCall : IDisposable
{
    private readonly VoipSettings _settings;
    private readonly string _callId;
    private readonly string _remotePeerId;
    private readonly string _localPeerId;
    private readonly bool _isCaller;
    private readonly Action<VoipSignalMessage> _sendSignal;
    private readonly Action<RTCPeerConnectionState>? _onConnectionStateChanged;

    private RTCPeerConnection? _peerConnection;
    private WindowsAudioEndPoint? _audioEndPoint;
    private bool _disposed;
    private bool _remoteDescriptionSet;
    private bool _audioStarted;
    private bool _micTransmitEnabled;
    private volatile bool _suppressRemotePlayback;
    private CancellationTokenSource? _receiveGuardCts;
    private const int ReceiveGuardAfterTransmitMs = 600;
    private readonly List<(string Candidate, string? SdpMid, int SdpMLineIndex)> _pendingIceCandidates = [];

    public VoipWebRtcCall(
        VoipSettings settings,
        string callId,
        string remotePeerId,
        string localPeerId,
        bool isCaller,
        Action<VoipSignalMessage> sendSignal,
        Action<RTCPeerConnectionState>? onConnectionStateChanged = null)
    {
        _settings = settings;
        _callId = callId;
        _remotePeerId = remotePeerId;
        _localPeerId = localPeerId;
        _isCaller = isCaller;
        _sendSignal = sendSignal;
        _onConnectionStateChanged = onConnectionStateChanged;
    }

    public async Task StartAsync()
    {
        var config = BuildRtcConfiguration();

        _peerConnection = new RTCPeerConnection(config);
        _peerConnection.onicecandidate += candidate =>
        {
            if (candidate == null)
            {
                return;
            }

            _sendSignal(new VoipSignalMessage
            {
                Type = VoipSignalTypes.IceCandidate,
                CallId = _callId,
                From = _localPeerId,
                To = _remotePeerId,
                Candidate = candidate.candidate,
                SdpMid = candidate.sdpMid,
                SdpMLineIndex = candidate.sdpMLineIndex
            });
        };

        _audioEndPoint = new WindowsAudioEndPoint(new AudioEncoder(), -1, -1, disableSource: false, disableSink: false);

        var audioTrack = new MediaStreamTrack(
            _audioEndPoint.GetAudioSourceFormats(),
            MediaStreamStatusEnum.SendRecv);
        _peerConnection.addTrack(audioTrack);

        _peerConnection.OnAudioFormatsNegotiated += formats =>
        {
            var format = formats.First();
            _audioEndPoint!.SetAudioSourceFormat(format);
            _audioEndPoint.SetAudioSinkFormat(format);
        };

        _audioEndPoint.OnAudioSourceEncodedSample += (duration, sample) =>
            _peerConnection!.SendAudio(duration, sample);

        _peerConnection.OnRtpPacketReceived += (remoteEndPoint, media, rtpPacket) =>
        {
            if (media != SDPMediaTypesEnum.audio || _suppressRemotePlayback)
            {
                return;
            }

            _audioEndPoint!.GotAudioRtp(
                remoteEndPoint,
                rtpPacket.Header.SyncSource,
                rtpPacket.Header.SequenceNumber,
                rtpPacket.Header.Timestamp,
                rtpPacket.Header.PayloadType,
                rtpPacket.Header.MarkerBit == 1,
                rtpPacket.Payload);
        };

        _peerConnection.onconnectionstatechange += state =>
        {
            try
            {
                _onConnectionStateChanged?.Invoke(state);
            }
            catch
            {
                // ignore
            }

            if (_audioEndPoint is null || _disposed)
            {
                return;
            }

            if (state == RTCPeerConnectionState.connected)
            {
                _ = StartAudioSafeAsync();
            }
            else if (state is RTCPeerConnectionState.closed or RTCPeerConnectionState.failed)
            {
                _ = CloseAudioSafeAsync();
            }
        };

        _peerConnection.oniceconnectionstatechange += state =>
        {
            if (state is RTCIceConnectionState.failed or RTCIceConnectionState.disconnected)
            {
                try
                {
                    _onConnectionStateChanged?.Invoke(RTCPeerConnectionState.failed);
                }
                catch
                {
                    // ignore
                }
            }
        };

        if (_isCaller)
        {
            await CreateOfferAsync().ConfigureAwait(false);
        }
    }

    private async Task StartAudioSafeAsync()
    {
        try
        {
            if (_audioEndPoint is not null && !_disposed)
            {
                await _audioEndPoint.Start().ConfigureAwait(false);
                _audioStarted = true;
                VoipLeitstelleAudioHelper.ApplyPlaybackVolume(_audioEndPoint);
                // Funk-PTT: Mikro erst bei Leertaste (Standard: stumm).
                _micTransmitEnabled = false;
                await _audioEndPoint.PauseAudio().ConfigureAwait(false);
            }
        }
        catch
        {
            // Mikrofon/Lautsprecher nicht verfügbar – kein Absturz.
        }
    }

    /// <summary>Mikrofon senden (Leertaste gedrückt) oder stumm schalten.</summary>
    public void SetMicrophoneTransmitEnabled(bool enabled)
    {
        if (_disposed || _audioEndPoint is null)
        {
            return;
        }

        _micTransmitEnabled = enabled;
        _ = ApplyMicTransmitStateAsync();
    }

    private async Task ApplyMicTransmitStateAsync()
    {
        try
        {
            if (_disposed || _audioEndPoint is null || !_audioStarted)
            {
                return;
            }

            if (_micTransmitEnabled)
            {
                _receiveGuardCts?.Cancel();
                _receiveGuardCts?.Dispose();
                _receiveGuardCts = null;
                _suppressRemotePlayback = true;
                VoipLeitstelleAudioHelper.FlushPlaybackBuffer(_audioEndPoint);
                await _audioEndPoint.ResumeAudio().ConfigureAwait(false);
            }
            else
            {
                await _audioEndPoint.PauseAudio().ConfigureAwait(false);
                _suppressRemotePlayback = true;
                VoipLeitstelleAudioHelper.FlushPlaybackBuffer(_audioEndPoint);

                _receiveGuardCts?.Cancel();
                _receiveGuardCts?.Dispose();
                _receiveGuardCts = new CancellationTokenSource();
                var token = _receiveGuardCts.Token;
                try
                {
                    await Task.Delay(ReceiveGuardAfterTransmitMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (_disposed || _micTransmitEnabled || _audioEndPoint is null)
                {
                    return;
                }

                _suppressRemotePlayback = false;
            }
        }
        catch
        {
            // ignore
        }
    }

    private async Task CloseAudioSafeAsync()
    {
        try
        {
            if (_audioEndPoint is not null)
            {
                await _audioEndPoint.Close().ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore
        }
    }

    public async Task HandleRemoteOfferAsync(string sdp)
    {
        if (_peerConnection is null)
        {
            return;
        }

        _peerConnection.setRemoteDescription(new RTCSessionDescriptionInit
        {
            sdp = sdp,
            type = RTCSdpType.offer
        });
        _remoteDescriptionSet = true;
        FlushPendingIceCandidates();
        await CreateAnswerAsync().ConfigureAwait(false);
    }

    public void HandleRemoteAnswer(string sdp)
    {
        _peerConnection?.setRemoteDescription(new RTCSessionDescriptionInit
        {
            sdp = sdp,
            type = RTCSdpType.answer
        });
        _remoteDescriptionSet = true;
        FlushPendingIceCandidates();
    }

    public void HandleRemoteIceCandidate(string candidate, string? sdpMid, int sdpMLineIndex)
    {
        if (!_remoteDescriptionSet)
        {
            _pendingIceCandidates.Add((candidate, sdpMid, sdpMLineIndex));
            return;
        }

        AddIceCandidate(candidate, sdpMid, sdpMLineIndex);
    }

    private void AddIceCandidate(string candidate, string? sdpMid, int sdpMLineIndex)
    {
        _peerConnection?.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = (ushort)sdpMLineIndex
        });
    }

    private void FlushPendingIceCandidates()
    {
        foreach (var (candidate, sdpMid, sdpMLineIndex) in _pendingIceCandidates)
        {
            AddIceCandidate(candidate, sdpMid, sdpMLineIndex);
        }

        _pendingIceCandidates.Clear();
    }

    private async Task CreateOfferAsync()
    {
        if (_peerConnection is null)
        {
            return;
        }

        var offer = _peerConnection.createOffer(null);
        var offerSdp = ForceBidirectionalAudioInSdp(offer.sdp?.ToString() ?? string.Empty);
        offer = new RTCSessionDescriptionInit { type = offer.type, sdp = offerSdp };
        await _peerConnection.setLocalDescription(offer).ConfigureAwait(false);
        await WaitForIceGatheringBeforeSdpSendAsync(_peerConnection).ConfigureAwait(false);
        var sdp = ForceBidirectionalAudioInSdp(GetLocalSdp(_peerConnection, offerSdp));
        _sendSignal(new VoipSignalMessage
        {
            Type = VoipSignalTypes.SdpOffer,
            CallId = _callId,
            From = _localPeerId,
            To = _remotePeerId,
            Sdp = sdp
        });
    }

    private async Task CreateAnswerAsync()
    {
        if (_peerConnection is null)
        {
            return;
        }

        var answer = _peerConnection.createAnswer(null);
        var answerSdp = ForceBidirectionalAudioInSdp(answer.sdp?.ToString() ?? string.Empty);
        answer = new RTCSessionDescriptionInit { type = answer.type, sdp = answerSdp };
        await _peerConnection.setLocalDescription(answer).ConfigureAwait(false);
        await WaitForIceGatheringBeforeSdpSendAsync(_peerConnection).ConfigureAwait(false);
        var sdp = ForceBidirectionalAudioInSdp(GetLocalSdp(_peerConnection, answerSdp));
        _sendSignal(new VoipSignalMessage
        {
            Type = VoipSignalTypes.SdpAnswer,
            CallId = _callId,
            From = _localPeerId,
            To = _remotePeerId,
            Sdp = sdp
        });
    }

    private RTCConfiguration BuildRtcConfiguration()
    {
        var turnConfigured = VoipTurnHelper.IsTurnConfigured(_settings);
        var config = new RTCConfiguration
        {
            iceServers = BuildIceServers(),
            X_ICEIncludeAllInterfaceAddresses = true,
            X_GatherTimeoutMs = 12_000
        };

        if (turnConfigured && _settings.UsesCloudSignaling())
        {
            config.iceTransportPolicy = RTCIceTransportPolicy.relay;
        }

        if (!_settings.UsesCloudSignaling())
        {
            var mediaHost = _settings.ResolveMediaHost();
            if (IPAddress.TryParse(mediaHost, out var bindIp) && !IPAddress.IsLoopback(bindIp))
            {
                config.X_BindAddress = bindIp;
            }
        }

        return config;
    }

    private List<RTCIceServer> BuildIceServers()
    {
        var servers = new List<RTCIceServer>
        {
            new() { urls = "stun:stun.l.google.com:19302" }
        };

        foreach (var turnUrl in VoipTurnHelper.ExpandTurnUrls(_settings))
        {
            servers.Add(new RTCIceServer
            {
                urls = turnUrl,
                username = _settings.TurnUsername,
                credential = _settings.TurnPassword
            });
        }

        return servers;
    }

    private int GetIceGatheringWaitMs()
    {
        if (VoipTurnHelper.IsTurnConfigured(_settings) && _settings.UsesCloudSignaling())
        {
            return 400;
        }

        return 2_000;
    }

    private Task WaitForIceGatheringBeforeSdpSendAsync(RTCPeerConnection pc) =>
        WaitForIceGatheringCompleteAsync(pc, GetIceGatheringWaitMs());

    private static async Task WaitForIceGatheringCompleteAsync(RTCPeerConnection pc, int timeoutMs = 2_000)
    {
        if (pc.iceGatheringState == RTCIceGatheringState.complete)
        {
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(RTCIceGatheringState state)
        {
            if (state == RTCIceGatheringState.complete)
            {
                tcs.TrySetResult(true);
            }
        }

        pc.onicegatheringstatechange += Handler;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timeout – SDP trotzdem senden.
        }
        finally
        {
            pc.onicegatheringstatechange -= Handler;
        }
    }

    private static string GetLocalSdp(RTCPeerConnection pc, string fallback)
    {
        var local = pc.localDescription;
        if (local?.sdp != null)
        {
            return local.sdp.ToString() ?? fallback;
        }

        return fallback;
    }

    private static string ForceBidirectionalAudioInSdp(string sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
        {
            return sdp;
        }

        return sdp
            .Replace("a=sendonly", "a=sendrecv", StringComparison.Ordinal)
            .Replace("a=recvonly", "a=sendrecv", StringComparison.Ordinal)
            .Replace("a=inactive", "a=sendrecv", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _receiveGuardCts?.Cancel();
        _receiveGuardCts?.Dispose();
        _receiveGuardCts = null;
        if (_audioEndPoint is not null)
        {
            _ = _audioEndPoint.Close();
        }

        _peerConnection?.Close("bye");
        _peerConnection?.Dispose();
    }
}
