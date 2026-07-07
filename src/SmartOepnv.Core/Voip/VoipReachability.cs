using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Linq;

namespace SmartOepnv.Core.Voip;

/// <summary>Hilfen für erreichbare VoIP-Adressen (Bus unterwegs ≠ gleiches WLAN).</summary>
public static class VoipReachability
{
    /// <summary>Typisches Tailscale-CIDR (100.64.0.0/10).</summary>
    public static bool IsTailscaleIpv4(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        return bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }

    public static bool IsTailscaleIpv4(string? host) =>
        IPAddress.TryParse(host?.Trim(), out var ip) && IsTailscaleIpv4(ip);

    /// <summary>Tailscale-IPv4 des PCs (Adapter oder tailscale.exe).</summary>
    public static string? TryDetectTailscaleIpv4()
    {
        foreach (var ip in ListTailscaleIpv4Candidates())
        {
            return ip;
        }

        return TryTailscaleCliIpv4();
    }

    public static IReadOnlyList<string> ListTailscaleIpv4Candidates()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    continue;
                }

                var label = $"{ni.Name} {ni.Description}";
                var adapterLooksLikeTailscale =
                    label.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    if (IPAddress.IsLoopback(addr.Address))
                    {
                        continue;
                    }

                    if (adapterLooksLikeTailscale || IsTailscaleIpv4(addr.Address))
                    {
                        result.Add(addr.Address.ToString());
                    }
                }
            }
        }
        catch
        {
            // optional
        }

        return result.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? TryTailscaleCliIpv4()
    {
        var executable = VoipTailscaleFunnel.ResolveExecutablePath() ?? "tailscale";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "ip -4",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var firstLine = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return firstLine is { Length: > 0 } && IsTailscaleIpv4(firstLine) ? firstLine : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsPrivateOrLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var trimmed = host.Trim();
        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(trimmed, out var ip))
        {
            // Hostname ohne Punkt → kein IPv4
            return !trimmed.Contains('.');
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            127 => true,
            172 when bytes[1] is >= 16 and <= 31 => true,
            192 when bytes[1] == 168 => true,
            169 when bytes[1] == 254 => true,
            _ => false
        };
    }

    public static IReadOnlyList<string> ListLocalIpv4Addresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var ip = addr.Address.ToString();
                    if (IPAddress.IsLoopback(addr.Address) || IsTailscaleIpv4(addr.Address))
                    {
                        continue;
                    }

                    result.Add(ip);
                }
            }
        }
        catch
        {
            // optional
        }

        return result.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Typische WLAN/LAN-IP dieses PCs für Tablets im gleichen Netz.</summary>
    public static string? TryGetRecommendedLanHost()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    continue;
                }

                var label = $"{ni.Name} {ni.Description}";
                var isWifiOrLan =
                    label.Contains("WLAN", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("Ethernet", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("LAN", StringComparison.OrdinalIgnoreCase);

                if (!isWifiOrLan)
                {
                    continue;
                }

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    if (IPAddress.IsLoopback(addr.Address) || IsTailscaleIpv4(addr.Address))
                    {
                        continue;
                    }

                    return addr.Address.ToString();
                }
            }
        }
        catch
        {
            // optional
        }

        return ListLocalIpv4Addresses().FirstOrDefault();
    }

    public static bool IsLocalAdapterAddress(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return ListLocalIpv4Addresses().Contains(host.Trim(), StringComparer.Ordinal);
    }

    public static bool IsTcpReachable(string? host, int port, int timeoutMs = 4000)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0)
        {
            return false;
        }

        try
        {
            var trimmed = host.Trim();
            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(trimmed);
            }
            catch
            {
                return false;
            }

            if (addresses.Length == 0)
            {
                return false;
            }

            var ordered = addresses
                .OrderBy(static a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToArray();

            foreach (var address in ordered)
            {
                try
                {
                    using var client = new TcpClient(address.AddressFamily);
                    var connect = client.ConnectAsync(address, port);
                    if (connect.Wait(timeoutMs) && client.Connected)
                    {
                        return true;
                    }
                }
                catch
                {
                    // nächste Adresse probieren
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
