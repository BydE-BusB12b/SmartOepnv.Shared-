namespace SmartOepnv.Core.Voip;

/// <summary>Bekannte TURN-Vorlagen (öffentlich erreichbar).</summary>
public static class VoipTurnPresets
{
    /// <summary>Öffentlicher Test-TURN (Metered Open Relay) – nur zum Testen, nicht für Dauerbetrieb.</summary>
    public static VoipSettings ApplyOpenRelayTest(VoipSettings settings)
    {
        settings.TurnHost = "openrelay.metered.ca";
        settings.TurnPort = 80;
        settings.TurnUsername = "openrelayproject";
        settings.TurnPassword = "openrelayproject";
        return settings;
    }

    /// <summary>Alternative Test-TURN (Port 443).</summary>
    public static VoipSettings ApplyOpenRelayTlsTest(VoipSettings settings)
    {
        settings.TurnHost = "openrelay.metered.ca";
        settings.TurnPort = 443;
        settings.TurnUsername = "openrelayproject";
        settings.TurnPassword = "openrelayproject";
        return settings;
    }
}
