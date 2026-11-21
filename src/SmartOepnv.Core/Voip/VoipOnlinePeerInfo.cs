namespace SmartOepnv.Core.Voip;

/// <summary>Online-Peer am Signaling-Server (lokal oder Cloud).</summary>
public sealed class VoipOnlinePeerInfo
{
    public string PeerId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = VoipConstants.RoleVehicle;
}
