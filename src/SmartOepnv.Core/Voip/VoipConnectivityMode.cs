namespace SmartOepnv.Core.Voip;

/// <summary>VoIP-Erreichbarkeit für Tablets (Betriebshof vs. unterwegs).</summary>
public enum VoipConnectivityMode
{
    /// <summary>Nur WLAN-IP des Leitstellen-PCs (Betriebshof).</summary>
    DepotWlan = 0,

    /// <summary>Öffentliche IP / DynDNS + Router-Portweiterleitung (Mobilfunk).</summary>
    MobilePublic = 1,

    /// <summary>Beides: unterwegs öffentliche IP, im Betriebshof WLAN-Fallback.</summary>
    Dual = 2,

    /// <summary>LTE über Tailscale Funnel (wss://*.ts.net), WLAN-Fallback im Betriebshof.</summary>
    TailscaleFunnel = 3,

    /// <summary>Cloud/VPS – Signaling + TURN über Server (kein Tailscale, kein Router).</summary>
    Cloud = 4,

    /// <summary>Smart ÖPNV Funk-Cloud – vorkonfigurierter zentraler Server (ein Klick).</summary>
    ManagedCloud = 5
}
