namespace SmartOepnv.Core.Voip;

/// <summary>Zentraler Smart-ÖPNV-Funkdienst – ein Server für alle Leitstellen.</summary>
public static class VoipManagedCloud
{
    /// <summary>Öffentliche VPS-IP (Signaling direkt Port 8787, ohne nginx).</summary>
    public const string DefaultHost = "82.165.167.157";

    /// <summary>Hostname mit TLS (später, wenn nginx-WebSocket steht).</summary>
    public const string TlsHost = "82-165-167-157.sslip.io";

    public const string TurnUsername = "smartoepnv";

    public static int TurnPort => VoipConstants.DefaultTurnPort;

    public static bool IsManagedMode(VoipSettings settings) =>
        settings.ConnectivityMode == VoipConnectivityMode.ManagedCloud;

    public static void ApplyTo(VoipSettings settings, string? turnPassword = null)
    {
        settings.ConnectivityMode = VoipConnectivityMode.ManagedCloud;
        settings.CloudSignalingHost = DefaultHost;
        settings.CloudSignalingUseTls = false;
        settings.CloudSignalingPort = VoipConstants.DefaultSignalingPort;
        settings.PublicSignalingHost = DefaultHost;
        settings.TurnHost = DefaultHost;
        settings.TurnPort = TurnPort;
        settings.TurnUsername = TurnUsername;
        if (!string.IsNullOrWhiteSpace(turnPassword))
        {
            settings.TurnPassword = turnPassword;
        }
    }

    public static bool IsReady(VoipSettings settings) =>
        IsManagedMode(settings) &&
        !string.IsNullOrWhiteSpace(settings.CloudSignalingHost) &&
        !string.IsNullOrWhiteSpace(settings.TurnPassword);
}
