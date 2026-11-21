using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Voip;

public static class VoipSignalTypes
{
    public const string Register = "register";
    public const string Registered = "registered";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Call = "call";
    public const string IncomingCall = "incoming_call";
    public const string Accept = "accept";
    public const string Reject = "reject";
    public const string Hangup = "hangup";
    public const string CallEnded = "call_ended";
    public const string Error = "error";
    public const string PeerOnline = "peer_online";
    public const string PeerOffline = "peer_offline";
    public const string SdpOffer = "sdp-offer";
    public const string SdpAnswer = "sdp-answer";
    public const string IceCandidate = "ice-candidate";
    public const string PttDown = "ptt-down";
    public const string PttUp = "ptt-up";
}

public sealed class VoipSignalMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("peerId")]
    public string? PeerId { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("callId")]
    public string? CallId { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("fromName")]
    public string? FromName { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("sdp")]
    public string? Sdp { get; set; }

    [JsonPropertyName("candidate")]
    public string? Candidate { get; set; }

    [JsonPropertyName("sdpMid")]
    public string? SdpMid { get; set; }

    [JsonPropertyName("sdpMLineIndex")]
    public int? SdpMLineIndex { get; set; }
}
