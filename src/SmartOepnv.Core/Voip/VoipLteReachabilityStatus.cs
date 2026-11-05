namespace SmartOepnv.Core.Voip;

/// <summary>LTE-Erreichbarkeit für Tablets unterwegs (Tailscale Funnel + TURN).</summary>
public sealed class VoipLteReachabilityStatus
{
    public bool IsRelevant { get; init; }

    public bool FunnelActive { get; init; }

    public bool TurnReachable { get; init; }

    public bool IsReady => IsRelevant && FunnelActive && TurnReachable;

    public string Summary { get; init; } = "—";
}
