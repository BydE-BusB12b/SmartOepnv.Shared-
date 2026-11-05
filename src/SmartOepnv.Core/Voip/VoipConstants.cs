namespace SmartOepnv.Core.Voip;

public static class VoipConstants
{
    public const int ConfigVersion = 1;
    public const int DefaultSignalingPort = 8787;
    public const int DefaultTurnPort = 3478;
    public const string DispatchConfigFileName = "voip_dispatch.json";
    public const string VehicleConfigPrefix = "voip_config_";
    public const string SignalingWebSocketPath = "/voip/ws";
    public const string RoleDispatch = "dispatch";
    public const string RoleVehicle = "vehicle";
}
