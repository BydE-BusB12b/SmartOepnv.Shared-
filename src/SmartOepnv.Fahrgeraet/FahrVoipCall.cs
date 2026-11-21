using System.Diagnostics;
using Concentus;
using Concentus.Enums;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Hörbare WebRTC-Strecke. Das Fahrzeug ist der Angerufene:
/// die Leitstelle sendet das Angebot, nur sie beendet das Gespräch.
/// </summary>
public sealed class FahrVoipCall : IAsyncDisposable
{
    private readonly string _callId;
    private readonly string _remotePeerId;
    private readonly string _localPeerId;
    private readonly string _turnUrl;
    private readonly string _turnUser;
    private readonly string _turnPassword;
    private readonly Func<VoipSignal, Task> _send;

    private RTCPeerConnection? _pc;
    private IFahrAudio? _audio;
    private bool _remoteSet;
    private bool _closed;
    private readonly List<(string Candidate, string? Mid, int Index)> _pendingIce = [];

    public event Action<string>? StateChanged;

    public FahrVoipCall(
        string callId,
        string remotePeerId,
        string localPeerId,
        string? turnUrl,
        string? turnUser,
        string? turnPassword,
        Func<VoipSignal, Task> send)
    {
        _callId = callId;
        _remotePeerId = remotePeerId;
        _localPeerId = localPeerId;
        _turnUrl = turnUrl ?? "";
        _turnUser = turnUser ?? "";
        _turnPassword = turnPassword ?? "";
        _send = send;
    }

    public Task StartAsync()
    {
        var turnReady = _turnUrl.Length > 0 && _turnPassword.Length > 0;
        var config = new RTCConfiguration
        {
            iceServers = IceServers(turnReady),
            X_ICEIncludeAllInterfaceAddresses = true,
            X_GatherTimeoutMs = 12_000
        };
        if (turnReady)
        {
            config.iceTransportPolicy = RTCIceTransportPolicy.relay;
        }

        _pc = new RTCPeerConnection(config);
        _pc.onicecandidate += candidate =>
        {
            if (candidate == null || _closed)
            {
                return;
            }

            _ = _send(new VoipSignal("ice-candidate", _callId, _localPeerId, _remotePeerId)
            {
                Candidate = candidate.candidate,
                SdpMid = candidate.sdpMid,
                SdpMLineIndex = candidate.sdpMLineIndex
            });
        };
        _pc.onconnectionstatechange += state =>
        {
            if (state == RTCPeerConnectionState.connected)
            {
                _ = StartAudioAsync();
                StateChanged?.Invoke("Sprechverbindung aktiv");
            }
            else if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.disconnected)
            {
                StateChanged?.Invoke("Sprechverbindung unterbrochen");
            }
        };

        _audio = new PcmFahrAudio();
        var track = new MediaStreamTrack(_audio.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
        _pc.addTrack(track);
        _pc.OnAudioFormatsNegotiated += formats =>
        {
            var format = formats.First();
            _audio.SetAudioSourceFormat(format);
            _audio.SetAudioSinkFormat(format);
        };
        _audio.OnAudioSourceEncodedSample += (duration, sample) => _pc?.SendAudio(duration, sample);
        _pc.OnRtpPacketReceived += (_, media, packet) =>
        {
            if (media != SDPMediaTypesEnum.audio || _audio is null)
            {
                return;
            }

            _audio.GotAudioRtp(
                packet.Header.SyncSource,
                packet.Header.SequenceNumber,
                packet.Header.Timestamp,
                packet.Header.PayloadType,
                packet.Header.MarkerBit == 1,
                packet.Payload);
        };
        return Task.CompletedTask;
    }

    public async Task AcceptOfferAsync(string sdp)
    {
        if (_pc is null || _closed)
        {
            return;
        }

        var offer = ForceSendRecv(sdp);
        _pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = offer, type = RTCSdpType.offer });
        _remoteSet = true;
        FlushIce();
        var answer = _pc.createAnswer(null);
        var answerSdp = ForceSendRecv(answer.sdp?.ToString() ?? "");
        answer = new RTCSessionDescriptionInit { type = answer.type, sdp = answerSdp };
        await _pc.setLocalDescription(answer);
        await WaitForIceAsync(_pc, _turnUrl.Length > 0 ? 400 : 2000);
        var local = ForceSendRecv(_pc.localDescription?.sdp?.ToString() ?? answerSdp);
        await _send(new VoipSignal("sdp-answer", _callId, _localPeerId, _remotePeerId) { Sdp = local });
    }

    public void AddIce(string candidate, string? mid, int index)
    {
        if (!_remoteSet)
        {
            _pendingIce.Add((candidate, mid, index));
            return;
        }

        _pc?.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = string.IsNullOrWhiteSpace(mid) ? "0" : mid,
            sdpMLineIndex = (ushort)Math.Max(0, index)
        });
    }

    public void SetMicEnabled(bool enabled) => _audio?.SetMicEnabled(enabled);

    public async ValueTask DisposeAsync()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (_audio is not null)
        {
            await _audio.Close();
        }

        _pc?.Close("bye");
        _pc?.Dispose();
        _pc = null;
    }

    private async Task StartAudioAsync()
    {
        if (_audio is null || _closed)
        {
            return;
        }

        try
        {
            await _audio.Start();
        }
        catch (Exception ex)
        {
            StateChanged?.Invoke("Tongerät: " + ex.Message);
        }
    }

    private void FlushIce()
    {
        foreach (var (candidate, mid, index) in _pendingIce)
        {
            AddIce(candidate, mid, index);
        }

        _pendingIce.Clear();
    }

    private List<RTCIceServer> IceServers(bool turnReady)
    {
        var servers = new List<RTCIceServer>
        {
            new() { urls = "stun:stun.l.google.com:19302" }
        };
        if (!turnReady)
        {
            return servers;
        }

        foreach (var url in ExpandTurn(_turnUrl))
        {
            servers.Add(new RTCIceServer
            {
                urls = url,
                username = _turnUser,
                credential = _turnPassword
            });
        }

        return servers;
    }

    private static IEnumerable<string> ExpandTurn(string raw)
    {
        if (raw.Contains("openrelay.metered.ca", StringComparison.OrdinalIgnoreCase))
        {
            yield return "turn:openrelay.metered.ca:80";
            yield return "turn:openrelay.metered.ca:443";
            yield return "turn:openrelay.metered.ca:443?transport=tcp";
            yield return "turns:openrelay.metered.ca:443?transport=tcp";
            yield break;
        }

        yield return raw;
        var host = raw.Replace("turns:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("turn:", "", StringComparison.OrdinalIgnoreCase)
            .Split('?')[0];
        var portText = host.Contains(':') ? host[(host.LastIndexOf(':') + 1)..] : "3478";
        host = host.Split(':')[0];
        if (int.TryParse(portText, out var port) && port is not 80 and not 443)
        {
            yield return $"turn:{host}:{port}?transport=udp";
            yield return $"turn:{host}:{port}?transport=tcp";
        }
    }

    private static async Task WaitForIceAsync(RTCPeerConnection pc, int timeoutMs)
    {
        if (pc.iceGatheringState == RTCIceGatheringState.complete)
        {
            return;
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(RTCIceGatheringState state)
        {
            if (state == RTCIceGatheringState.complete)
            {
                done.TrySetResult(true);
            }
        }

        pc.onicegatheringstatechange += Handler;
        try
        {
            await done.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch (TimeoutException)
        {
            // Angebot der Leitstelle trotzdem beantworten.
        }
        finally
        {
            pc.onicegatheringstatechange -= Handler;
        }
    }

    private static string ForceSendRecv(string sdp) =>
        sdp.Replace("a=sendonly", "a=sendrecv", StringComparison.Ordinal)
            .Replace("a=recvonly", "a=sendrecv", StringComparison.Ordinal)
            .Replace("a=inactive", "a=sendrecv", StringComparison.Ordinal);

    private interface IFahrAudio
    {
        List<AudioFormat> GetAudioSourceFormats();
        void SetAudioSourceFormat(AudioFormat format);
        void SetAudioSinkFormat(AudioFormat format);
        event Action<uint, byte[]>? OnAudioSourceEncodedSample;
        void GotAudioRtp(uint ssrc, ushort seq, uint timestamp, int payloadType, bool marker, byte[] payload);
        void SetMicEnabled(bool enabled);
        Task Start();
        Task Close();
    }

    private sealed class PcmFahrAudio : IFahrAudio
    {
        private readonly AudioEncoder _encoder = new();
        private readonly List<short> _pending = [];
        private AudioFormat _format = new(SDPWellKnownMediaFormatsEnum.PCMU);
        private IOpusEncoder? _opusEnc;
        private IOpusDecoder? _opusDec;
        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _waveBuffer;
        private Process? _play;
        private Process? _record;
        private CancellationTokenSource? _capture;
        private bool _mic = true;
        private int _started;

        public event Action<uint, byte[]>? OnAudioSourceEncodedSample;

        public List<AudioFormat> GetAudioSourceFormats() =>
        [
            new AudioFormat(111, "opus", 48000, 2, "minptime=10;useinbandfec=1"),
            .. _encoder.SupportedFormats
        ];

        public void SetAudioSourceFormat(AudioFormat format)
        {
            _format = format;
            if (!Opus)
            {
                return;
            }

            _opusEnc = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_VOIP);
            _opusDec = OpusCodecFactory.CreateDecoder(48000, 2);
        }

        public void SetAudioSinkFormat(AudioFormat format) => SetAudioSourceFormat(format);

        private bool Opus => _format.FormatName.Contains("opus", StringComparison.OrdinalIgnoreCase);

        private int CaptureRate =>
            Opus ? 48000 :
            _format.FormatName.Contains("G722", StringComparison.OrdinalIgnoreCase) ? 16000 :
            Math.Max(8000, _format.ClockRate);

        private int FrameSamples => CaptureRate / 50;

        public void GotAudioRtp(uint ssrc, ushort seq, uint timestamp, int payloadType, bool marker, byte[] payload)
        {
            short[] pcm;
            try
            {
                if (Opus && _opusDec is not null)
                {
                    pcm = new short[960 * 2];
                    var decoded = _opusDec.Decode(payload, pcm, 960, false);
                    if (decoded <= 0)
                    {
                        return;
                    }

                    if (decoded < 960)
                    {
                        pcm = pcm[..(decoded * 2)];
                    }
                }
                else
                {
                    pcm = _encoder.DecodeAudio(payload, _format);
                }
            }
            catch
            {
                return;
            }

            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            if (_waveBuffer is not null)
            {
                _waveBuffer.AddSamples(bytes, 0, bytes.Length);
                return;
            }

            try
            {
                _play?.StandardInput.BaseStream.Write(bytes);
            }
            catch
            {
                // Wiedergabe beendet
            }
        }

        public void SetMicEnabled(bool enabled) => _mic = enabled;

        public Task Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                return Task.CompletedTask;
            }

            if (OperatingSystem.IsWindows())
            {
                var playFormat = new WaveFormat(CaptureRate, 16, Opus ? 2 : 1);
                _waveBuffer = new BufferedWaveProvider(playFormat) { DiscardOnBufferOverflow = true };
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_waveBuffer);
                _waveOut.Play();
                _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(CaptureRate, 16, 1), BufferMilliseconds = 20 };
                _waveIn.DataAvailable += (_, args) => SendPcm(args.Buffer, args.BytesRecorded);
                _waveIn.StartRecording();
                return Task.CompletedTask;
            }

            _play = StartAlsa("aplay", CaptureRate, Opus ? 2 : 1);
            _record = StartAlsa("arecord", CaptureRate, 1);
            _capture = new CancellationTokenSource();
            _ = Capture(_capture.Token);
            return Task.CompletedTask;
        }

        public Task Close()
        {
            _capture?.Cancel();
            try
            {
                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _waveOut?.Stop();
                _waveOut?.Dispose();
            }
            catch
            {
                // Gerät schon zu
            }

            TryKill(_record);
            TryKill(_play);
            return Task.CompletedTask;
        }

        private void SendPcm(byte[] buffer, int length)
        {
            if (!_mic || length < 2)
            {
                return;
            }

            var samples = length / 2;
            var pcm = new short[samples];
            Buffer.BlockCopy(buffer, 0, pcm, 0, samples * 2);
            _pending.AddRange(pcm);
            var need = FrameSamples;
            while (_pending.Count >= need)
            {
                var frame = _pending.GetRange(0, need).ToArray();
                _pending.RemoveRange(0, need);
                if (Opus && _opusEnc is not null)
                {
                    var stereo = new short[need * 2];
                    for (var i = 0; i < need; i++)
                    {
                        stereo[i * 2] = frame[i];
                        stereo[i * 2 + 1] = frame[i];
                    }

                    var output = new byte[4000];
                    var encodedLength = _opusEnc.Encode(stereo, need, output, output.Length);
                    OnAudioSourceEncodedSample?.Invoke(960, output[..encodedLength]);
                }
                else
                {
                    var encoded = _encoder.EncodeAudio(frame, _format);
                    var duration = frame.Length * 8000 / CaptureRate;
                    OnAudioSourceEncodedSample?.Invoke((uint)duration, encoded);
                }
            }
        }

        private async Task Capture(CancellationToken ct)
        {
            var stream = _record?.StandardOutput.BaseStream;
            if (stream is null)
            {
                return;
            }

            var frame = new byte[Math.Max(320, FrameSamples * 2)];
            while (!ct.IsCancellationRequested)
            {
                var read = 0;
                while (read < frame.Length)
                {
                    var n = await stream.ReadAsync(frame.AsMemory(read), ct);
                    if (n == 0)
                    {
                        return;
                    }

                    read += n;
                }

                SendPcm(frame, frame.Length);
            }
        }

        private static Process StartAlsa(string program, int rate, int channels)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = program,
                    RedirectStandardInput = program == "aplay",
                    RedirectStandardOutput = program == "arecord",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-q");
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add("S16_LE");
            process.StartInfo.ArgumentList.Add("-r");
            process.StartInfo.ArgumentList.Add(rate.ToString());
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add(channels.ToString());
            process.StartInfo.ArgumentList.Add("-t");
            process.StartInfo.ArgumentList.Add("raw");
            process.Start();
            return process;
        }

        private static void TryKill(Process? process)
        {
            try
            {
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // schon beendet
            }
        }
    }
}

public sealed class VoipSignal
{
    public VoipSignal(string type, string callId, string from, string to)
    {
        Type = type;
        CallId = callId;
        From = from;
        To = to;
    }

    public string Type { get; }
    public string CallId { get; }
    public string From { get; }
    public string To { get; }
    public string? Sdp { get; init; }
    public string? Candidate { get; init; }
    public string? SdpMid { get; init; }
    public int? SdpMLineIndex { get; init; }
}
