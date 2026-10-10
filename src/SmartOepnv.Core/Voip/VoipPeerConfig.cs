using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Voip;

/// <summary>Dropbox <c>voip_config_&lt;Telefon&gt;.json</c> bzw. <c>voip_dispatch.json</c>.</summary>
public sealed class VoipPeerConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = VoipConstants.ConfigVersion;

    [JsonPropertyName("peerId")]
    public string PeerId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = VoipConstants.RoleVehicle;

    [JsonPropertyName("signalingUrl")]
    public string SignalingUrl { get; set; } = string.Empty;

    [JsonPropertyName("signalingUrlFallback")]
    public string? SignalingUrlFallback { get; set; }

    [JsonPropertyName("connectivityMode")]
    public string? ConnectivityMode { get; set; }

    [JsonPropertyName("turnUrl")]
    public string TurnUrl { get; set; } = string.Empty;

    [JsonPropertyName("turnUsername")]
    public string TurnUsername { get; set; } = string.Empty;

    [JsonPropertyName("turnPassword")]
    public string TurnPassword { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }
}
