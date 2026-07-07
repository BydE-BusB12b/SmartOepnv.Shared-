using System.Collections.Generic;
using System.Net;

namespace SmartOepnv.Core.Voip;

public static class VoipHttpListenHelper
{
    /// <summary>
    /// HttpListener auf Windows: "+" für alle Adapter (nicht 0.0.0.0 oder feste LAN-IP im Prefix).
    /// </summary>
    public static string ResolveBindHost(string? listenHostRaw)
    {
        var raw = string.IsNullOrWhiteSpace(listenHostRaw) ? "127.0.0.1" : listenHostRaw.Trim();
        return raw switch
        {
            "127.0.0.1" or "localhost" => "127.0.0.1",
            "0.0.0.0" or "*" or "+" => "+",
            _ when IPAddress.TryParse(raw, out _) => "+",
            _ => raw
        };
    }

    public static string BuildPrefix(VoipSettings settings, string bindHost) =>
        $"http://{bindHost}:{settings.SignalingPort}/";

    public static string BuildPathPrefix(VoipSettings settings, string bindHost)
    {
        var path = VoipConstants.SignalingWebSocketPath.TrimEnd('/');
        return $"http://{bindHost}:{settings.SignalingPort}{path}/";
    }

    /// <summary>
    /// Prefix-Gruppen für HttpListener. Root + Pfad nötig, wenn Windows eine path-spezifische URL-ACL hat
    /// (sonst HTTP 503 beim WebSocket-Upgrade auf /voip/ws).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> BuildListenPrefixGroups(VoipSettings settings)
    {
        var bindHost = ResolveBindHost(settings.ListenHost);
        var groups = new List<IReadOnlyList<string>>();
        var hosts = new List<string>();

        if (string.Equals(bindHost, "+", StringComparison.Ordinal))
        {
            hosts.Add("+");
        }
        else if (!string.Equals(bindHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            hosts.Add(bindHost);
        }

        if (!hosts.Contains("127.0.0.1", StringComparer.OrdinalIgnoreCase))
        {
            hosts.Add("127.0.0.1");
        }

        foreach (var host in hosts)
        {
            groups.Add(new[]
            {
                BuildPrefix(settings, host),
                BuildPathPrefix(settings, host)
            });
        }

        return groups;
    }

    public static string BuildUrlAclReservation(VoipSettings settings, string bindHost) =>
        BuildPrefix(settings, bindHost);

    public static string FormatStartFailure(VoipSettings settings, Exception ex)
    {
        var prefix = BuildUrlAclReservation(settings, ResolveBindHost(settings.ListenHost));
        if (ex is HttpListenerException { ErrorCode: 5 })
        {
            return "Zugriff verweigert – Windows blockiert den VoIP-Port. " +
                   "Die Leitstelle versucht beim Start automatisch, den Port freizugeben (einmalig Administrator).";
        }

        return ex.Message;
    }
}
