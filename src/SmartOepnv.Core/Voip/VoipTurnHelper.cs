namespace SmartOepnv.Core.Voip;

public static class VoipTurnHelper
{
    public static bool IsTurnConfigured(VoipSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.BuildTurnUrlForRemotePeers());

    public static IReadOnlyList<string> ExpandTurnUrls(VoipSettings settings)
    {
        var primary = settings.BuildTurnUrlForRemotePeers();
        if (string.IsNullOrWhiteSpace(primary))
        {
            return Array.Empty<string>();
        }

        var host = settings.TurnHost.Trim();
        if (host.Equals("openrelay.metered.ca", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "turn:openrelay.metered.ca:80",
                "turn:openrelay.metered.ca:443",
                "turn:openrelay.metered.ca:443?transport=tcp",
                "turns:openrelay.metered.ca:443?transport=tcp"
            ];
        }

        var urls = new List<string> { primary };
        var port = settings.TurnPort;
        if (port is not 80 and not 443)
        {
            if (!primary.Contains("transport=", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add($"turn:{host}:{port}?transport=udp");
            }

            urls.Add($"turn:{host}:{port}?transport=tcp");
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
