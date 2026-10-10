namespace SmartOepnv.Core.Voip;

/// <summary>Leitstellen-Einstellungen für Funk/VoIP (lokal gespeichert).</summary>
public sealed class VoipSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>HTTP/WebSocket-Listener: 0.0.0.0 = alle Adapter.</summary>
    public string ListenHost { get; set; } = "0.0.0.0";

    public int SignalingPort { get; set; } = VoipConstants.DefaultSignalingPort;

    /// <summary>Von Tablets unterwegs erreichbar: öffentliche IP oder Hostname (Legacy-Modus).</summary>
    public string PublicSignalingHost { get; set; } = "127.0.0.1";

    /// <summary>Tailscale Funnel Hostname, z. B. leitstelle.tailnet.ts.net</summary>
    public string TailscaleFunnelHost { get; set; } = string.Empty;

    /// <summary>Funnel beim VoIP-Start automatisch starten/stoppen.</summary>
    public bool AutoManageTailscaleFunnel { get; set; } = true;

    /// <summary>Cloud-Server Hostname (VPS), z. B. voip.example.com</summary>
    public string CloudSignalingHost { get; set; } = string.Empty;

    /// <summary>Cloud-Signaling über wss:// (empfohlen, Port 443).</summary>
    public bool CloudSignalingUseTls { get; set; } = true;

    /// <summary>Cloud-Signaling-Port (0 = Standard: 443 bei TLS, sonst 8787).</summary>
    public int CloudSignalingPort { get; set; }

    public string TurnHost { get; set; } = "127.0.0.1";

    public int TurnPort { get; set; } = VoipConstants.DefaultTurnPort;

    public string TurnUsername { get; set; } = "smartoepnv";

    public string TurnPassword { get; set; } = "smartoepnv";

    public string DispatchDisplayName { get; set; } = "Leitstelle";

    /// <summary>Windows URL-ACL/Firewall erfolgreich eingerichtet (einmalig, persistent).</summary>
    public DateTime? WindowsPortSetupCompletedUtc { get; set; }

    /// <summary>Betriebshof vs. Mobilfunk vs. beides.</summary>
    public VoipConnectivityMode ConnectivityMode { get; set; } = VoipConnectivityMode.ManagedCloud;

    /// <summary>WLAN-IP des Leitstellen-PCs (Betriebshof / Fallback).</summary>
    public string DepotLanHost { get; set; } = string.Empty;

    public bool UsesManagedCloud() => ConnectivityMode == VoipConnectivityMode.ManagedCloud;

    public bool UsesCloudSignaling() =>
        ConnectivityMode is VoipConnectivityMode.Cloud or VoipConnectivityMode.ManagedCloud;

    public bool UsesLocalSignalingServer() => !UsesCloudSignaling();

    public string BuildSignalingUrl() =>
        ConnectivityMode switch
        {
            VoipConnectivityMode.TailscaleFunnel => BuildFunnelSignalingUrl(),
            VoipConnectivityMode.Cloud or VoipConnectivityMode.ManagedCloud => BuildCloudSignalingUrl(),
            _ => BuildSignalingUrlForHost(ResolvePrimaryHost())
        };

    public string? BuildSignalingFallbackUrl()
    {
        if (ConnectivityMode is not (VoipConnectivityMode.Dual or VoipConnectivityMode.TailscaleFunnel))
        {
            return null;
        }

        var fallbackHost = ResolveDepotLanHost();
        if (string.IsNullOrWhiteSpace(fallbackHost))
        {
            return null;
        }

        if (ConnectivityMode == VoipConnectivityMode.TailscaleFunnel)
        {
            return BuildSignalingUrlForHost(fallbackHost);
        }

        var primary = ResolvePrimaryHost();
        if (string.Equals(primary.Trim(), fallbackHost.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return BuildSignalingUrlForHost(fallbackHost);
    }

    /// <summary>Leitstelle verbindet sich hierhin (lokal oder Cloud).</summary>
    public string BuildDispatchSignalingUrl() =>
        UsesCloudSignaling() ? BuildCloudSignalingUrl() : BuildLocalDispatchSignalingUrl();

    public VoipLteReachabilityStatus EvaluateLteReachability()
    {
        if (ConnectivityMode is VoipConnectivityMode.Cloud or VoipConnectivityMode.ManagedCloud)
        {
            return EvaluateCloudReachability();
        }

        if (ConnectivityMode != VoipConnectivityMode.TailscaleFunnel)
        {
            return new VoipLteReachabilityStatus { IsRelevant = false, Summary = "—" };
        }

        var funnel = VoipTailscaleFunnel.QueryStatus(SignalingPort);
        var funnelHost = VoipTailscaleFunnel.NormalizeHostname(TailscaleFunnelHost);
        if (string.IsNullOrWhiteSpace(funnelHost) && !string.IsNullOrWhiteSpace(funnel.PublicHostname))
        {
            funnelHost = funnel.PublicHostname!;
        }

        var funnelConfigured = !string.IsNullOrWhiteSpace(funnelHost);
        var funnelActive = funnel.IsFunnelActive && funnelConfigured;
        var turnConfigured = VoipTurnHelper.IsTurnConfigured(this);
        var turnReachable = IsTurnServerReachable();

        if (!VoipTailscaleFunnel.IsTailscaleCliAvailable())
        {
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = false,
                TurnReachable = turnReachable,
                Summary = "LTE erreichbar: nein (Tailscale nicht gefunden)"
            };
        }

        var login = VoipTailscaleFunnel.TryGetLoginState();
        if (login?.NeedsLogin == true)
        {
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = false,
                TurnReachable = turnReachable,
                Summary = "LTE erreichbar: nein (Tailscale noch nicht angemeldet)"
            };
        }

        if (!funnelConfigured)
        {
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = false,
                TurnReachable = turnReachable,
                Summary = "LTE erreichbar: nein (Funnel-URL fehlt)"
            };
        }

        if (!funnelActive)
        {
            var turnHint = turnReachable ? "" : ", TURN nicht erreichbar";
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = false,
                TurnReachable = turnReachable,
                Summary = $"LTE erreichbar: nein (Funnel inaktiv{turnHint})"
            };
        }

        if (!turnConfigured)
        {
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = funnelActive,
                TurnReachable = false,
                Summary = "LTE erreichbar: teilweise (Funnel ok, TURN nicht in Einstellungen)"
            };
        }

        if (!turnReachable)
        {
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = funnelActive,
                TurnReachable = false,
                Summary = $"LTE erreichbar: teilweise (Funnel ok, TURN {TurnHost}:{TurnPort} eingetragen)"
            };
        }

        return new VoipLteReachabilityStatus
        {
            IsRelevant = true,
            FunnelActive = true,
            TurnReachable = true,
            Summary = $"LTE erreichbar: ja ({BuildFunnelSignalingUrl()})"
        };
    }

    public VoipLteReachabilityStatus EvaluateCloudReachability()
    {
        var cloudHost = CloudSignalingHost.Trim();
        var cloudConfigured = !string.IsNullOrWhiteSpace(cloudHost);
        var cloudPort = ResolveCloudSignalingPort();
        var cloudReachable = cloudConfigured &&
                             VoipReachability.IsTcpReachable(cloudHost, cloudPort);
        var turnConfigured = VoipTurnHelper.IsTurnConfigured(this);

        if (!cloudConfigured)
        {
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = false,
                TurnReachable = turnConfigured,
                Summary = "Cloud: nicht konfiguriert (Server-Host fehlt)"
            };
        }

        if (!turnConfigured)
        {
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = cloudReachable,
                TurnReachable = false,
                Summary = cloudReachable
                    ? "Cloud: teilweise (Signaling ok, TURN fehlt)"
                    : "Cloud: Server nicht erreichbar, TURN fehlt"
            };
        }

        if (!cloudReachable)
        {
            return new VoipLteReachabilityStatus
            {
                IsRelevant = true,
                FunnelActive = false,
                TurnReachable = IsTurnServerReachable(),
                Summary = $"Cloud: Server {cloudHost}:{cloudPort} nicht erreichbar"
            };
        }

        return new VoipLteReachabilityStatus
        {
            IsRelevant = true,
            FunnelActive = true,
            TurnReachable = true,
            Summary = $"Cloud erreichbar: ja ({BuildCloudSignalingUrl()})"
        };
    }

    private string ResolvePrimaryHost() =>
        ConnectivityMode switch
        {
            VoipConnectivityMode.DepotWlan => ResolveDepotLanHost(),
            VoipConnectivityMode.MobilePublic => PublicSignalingHost.Trim(),
            VoipConnectivityMode.Dual => PublicSignalingHost.Trim(),
            VoipConnectivityMode.TailscaleFunnel => VoipTailscaleFunnel.NormalizeHostname(TailscaleFunnelHost),
            VoipConnectivityMode.Cloud or VoipConnectivityMode.ManagedCloud => CloudSignalingHost.Trim(),
            _ => PublicSignalingHost.Trim()
        };

    private string ResolveDepotLanHost()
    {
        if (!string.IsNullOrWhiteSpace(DepotLanHost))
        {
            return DepotLanHost.Trim();
        }

        return VoipReachability.IsPrivateOrLocalHost(PublicSignalingHost)
            ? PublicSignalingHost.Trim()
            : string.Empty;
    }

    public string BuildCloudSignalingUrl()
    {
        var host = CloudSignalingHost.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        host = VoipTailscaleFunnel.NormalizeHostname(host);
        var path = VoipConstants.SignalingWebSocketPath.TrimEnd('/');
        var port = ResolveCloudSignalingPort();

        if (CloudSignalingUseTls)
        {
            return port is 443
                ? $"wss://{host}{path}"
                : $"wss://{host}:{port}{path}";
        }

        return $"ws://{host}:{port}{path}";
    }

    public int ResolveCloudSignalingPort()
    {
        if (CloudSignalingPort > 0)
        {
            return CloudSignalingPort;
        }

        return CloudSignalingUseTls ? 443 : SignalingPort;
    }

    private string BuildFunnelSignalingUrl()
    {
        var host = VoipTailscaleFunnel.NormalizeHostname(TailscaleFunnelHost);
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var path = VoipConstants.SignalingWebSocketPath.TrimEnd('/');
        return $"wss://{host}{path}";
    }

    private string BuildSignalingUrlForHost(string host)
    {
        var path = VoipConstants.SignalingWebSocketPath.TrimEnd('/');
        return $"ws://{host.Trim()}:{SignalingPort}{path}";
    }

    /// <summary>Leitstelle-intern: Dispatch-Client verbindet sich lokal (nicht über LAN-IP).</summary>
    public string BuildLocalDispatchSignalingUrl()
    {
        var path = VoipConstants.SignalingWebSocketPath.TrimEnd('/');
        return $"ws://127.0.0.1:{SignalingPort}{path}";
    }

    public string BuildTurnUrl()
    {
        if (string.IsNullOrWhiteSpace(TurnHost))
        {
            return string.Empty;
        }

        return $"turn:{TurnHost.Trim()}:{TurnPort}";
    }

    /// <summary>TURN-URL für Tablets/Fahrzeuge (niemals 127.0.0.1).</summary>
    public string BuildTurnUrlForRemotePeers()
    {
        var host = TurnHost.Trim();
        if (IsLoopbackHost(host))
        {
            if (UsesCloudSignaling() && !string.IsNullOrWhiteSpace(CloudSignalingHost))
            {
                host = CloudSignalingHost.Trim();
            }
            else
            {
                host = ResolveMediaHost();
            }
        }

        if (string.IsNullOrWhiteSpace(host) || IsLoopbackHost(host))
        {
            return string.Empty;
        }

        if (VoipReachability.IsPrivateOrLocalHost(host))
        {
            return string.Empty;
        }

        return $"turn:{host}:{TurnPort}";
    }

    public bool IsTurnServerReachable()
    {
        var turnUrl = BuildTurnUrlForRemotePeers();
        if (string.IsNullOrWhiteSpace(turnUrl))
        {
            return false;
        }

        var host = turnUrl["turn:".Length..].Split(':')[0];
        return VoipReachability.IsTcpReachable(host, TurnPort);
    }

    /// <summary>LAN-IP für WebRTC-RTP/ICE auf der Leitstelle.</summary>
    public string ResolveMediaHost()
    {
        if (!string.IsNullOrWhiteSpace(DepotLanHost))
        {
            return DepotLanHost.Trim();
        }

        var primary = PublicSignalingHost.Trim();
        if (!IsLoopbackHost(primary) && VoipReachability.IsLocalAdapterAddress(primary))
        {
            return primary;
        }

        if (!IsLoopbackHost(primary) && VoipReachability.IsPrivateOrLocalHost(primary))
        {
            return primary;
        }

        return VoipReachability.TryGetRecommendedLanHost() ?? primary;
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("127.0.0.1", StringComparison.Ordinal) ||
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("::1", StringComparison.Ordinal);
}
