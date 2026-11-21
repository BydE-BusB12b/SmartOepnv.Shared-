namespace SmartOepnv.Core.Sev;

public sealed record SevTftEndpoint(string Host, int Port, string? ApiToken)
{
    public const int DefaultPort = 8080;

    public Uri CreateUri(string absolutePath)
    {
        var builder = new UriBuilder(Uri.UriSchemeHttp, Host, Port, absolutePath.TrimStart('/'));
        return builder.Uri;
    }

    public static bool TryCreate(
        string? hostText,
        string? portText,
        string? apiToken,
        out SevTftEndpoint? endpoint,
        out string error)
    {
        endpoint = null;
        var host = NormalizeHost(hostText);
        if (host.Length == 0)
        {
            error = "Bitte die IP-Adresse des Raspberry Pi eintragen.";
            return false;
        }

        if (host.Any(char.IsWhiteSpace) || host.Contains('/'))
        {
            error = "Die Adresse darf nur die IP oder der Hostname sein, ohne Pfad.";
            return false;
        }

        var portRaw = (portText ?? string.Empty).Trim();
        var port = DefaultPort;
        if (portRaw.Length > 0 &&
            (!int.TryParse(portRaw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port) ||
             port is < 1 or > 65535))
        {
            error = "Der Port muss zwischen 1 und 65535 liegen.";
            return false;
        }

        var token = (apiToken ?? string.Empty).Trim();
        endpoint = new SevTftEndpoint(host, port, token.Length == 0 ? null : token);
        error = string.Empty;
        return true;
    }

    public static string NormalizeHost(string? hostText)
    {
        var host = (hostText ?? string.Empty).Trim();
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            host = host["http://".Length..];
        }
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            host = host["https://".Length..];
        }

        var slash = host.IndexOf('/');
        if (slash >= 0)
        {
            host = host[..slash];
        }

        if (host.Count(c => c == ':') == 1)
        {
            var parts = host.Split(':');
            if (int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                host = parts[0];
            }
        }

        return host.Trim();
    }
}
