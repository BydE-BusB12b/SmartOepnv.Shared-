using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.Voip;

/// <summary>Tailscale Funnel für LTE-Signaling ohne Router (nur Leitstellen-PC).</summary>
public static class VoipTailscaleFunnel
{
    private static readonly Regex TsNetHostRegex = new(
        @"https?://([a-zA-Z0-9][a-zA-Z0-9.-]*\.ts\.net)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LocalProxyRegex = new(
        @"proxy\s+https?://127\.0\.0\.1:(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? _resolvedExecutable;

    public sealed class FunnelStatus
    {
        public bool IsTailscaleAvailable { get; init; }

        public bool IsFunnelActive { get; init; }

        public string? PublicHostname { get; init; }

        public int? ProxiedLocalPort { get; init; }

        public string? RawOutput { get; init; }

        public string? Error { get; init; }

        public string? BuildSignalingUrl()
        {
            if (string.IsNullOrWhiteSpace(PublicHostname))
            {
                return null;
            }

            var path = VoipConstants.SignalingWebSocketPath.TrimEnd('/');
            return $"wss://{PublicHostname.Trim()}{path}";
        }
    }

    public sealed class TailscaleLoginState
    {
        public string BackendState { get; init; } = string.Empty;

        public bool NeedsLogin =>
            BackendState.Equals("NeedsLogin", StringComparison.OrdinalIgnoreCase) ||
            BackendState.Equals("Stopped", StringComparison.OrdinalIgnoreCase);

        public string? AuthUrl { get; init; }

        public string? DnsName { get; init; }
    }

    public static bool IsTailscaleCliAvailable() => !string.IsNullOrWhiteSpace(ResolveExecutablePath());

    public static string? ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_resolvedExecutable) && File.Exists(_resolvedExecutable))
        {
            return _resolvedExecutable;
        }

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Tailscale",
                "tailscale.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Tailscale",
                "tailscale.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Tailscale",
                "tailscale.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                _resolvedExecutable = candidate;
                return candidate;
            }
        }

        if (TryResolveFromPath(out var fromPath))
        {
            _resolvedExecutable = fromPath;
            return fromPath;
        }

        return null;
    }

    public static TailscaleLoginState? TryGetLoginState()
    {
        if (!TryRunTailscale("status --json", out var output, 5000) ||
            string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            var backendState = root.TryGetProperty("BackendState", out var stateEl)
                ? stateEl.GetString() ?? string.Empty
                : string.Empty;
            var authUrl = root.TryGetProperty("AuthURL", out var authEl)
                ? authEl.GetString()
                : null;

            string? dnsName = null;
            if (root.TryGetProperty("Self", out var selfEl))
            {
                dnsName = selfEl.TryGetProperty("DNSName", out var dnsEl)
                    ? dnsEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(dnsName) &&
                    selfEl.TryGetProperty("HostName", out var hostEl) &&
                    root.TryGetProperty("MagicDNSSuffix", out var suffixEl))
                {
                    var host = hostEl.GetString()?.Trim().ToLowerInvariant();
                    var suffix = suffixEl.GetString()?.Trim().TrimStart('.');
                    if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(suffix))
                    {
                        dnsName = $"{host}.{suffix}";
                    }
                }
            }

            dnsName = NormalizeHostname(dnsName);
            return new TailscaleLoginState
            {
                BackendState = backendState,
                AuthUrl = authUrl,
                DnsName = string.IsNullOrWhiteSpace(dnsName) ? null : dnsName
            };
        }
        catch
        {
            return null;
        }
    }

    public static FunnelStatus QueryStatus(int expectedLocalPort = VoipConstants.DefaultSignalingPort)
    {
        if (!IsTailscaleCliAvailable())
        {
            return new FunnelStatus
            {
                IsTailscaleAvailable = false,
                Error = "tailscale.exe nicht gefunden (Program Files oder PATH)"
            };
        }

        if (!TryRunTailscale("funnel status", out var output, 5000))
        {
            return new FunnelStatus
            {
                IsTailscaleAvailable = true,
                Error = string.IsNullOrWhiteSpace(output) ? "tailscale funnel status fehlgeschlagen" : output.Trim()
            };
        }

        var outputText = output ?? string.Empty;
        var hostname = TryParsePublicHostname(outputText) ?? TryDetectPublicHostnameFromStatus();
        var proxiedPort = TryParseProxiedLocalPort(outputText);
        var active =
            outputText.Contains("Funnel on", StringComparison.OrdinalIgnoreCase) ||
            (hostname is not null && outputText.Contains("proxy", StringComparison.OrdinalIgnoreCase));

        var matchesPort = !proxiedPort.HasValue || proxiedPort.Value == expectedLocalPort;

        return new FunnelStatus
        {
            IsTailscaleAvailable = true,
            IsFunnelActive = active && hostname is not null && matchesPort,
            PublicHostname = hostname,
            ProxiedLocalPort = proxiedPort,
            RawOutput = outputText.Trim()
        };
    }

    public static bool TryEnsureStarted(int localPort, out string? publicHostname, out string? error)
    {
        publicHostname = null;
        error = null;

        var login = TryGetLoginState();
        if (login?.NeedsLogin == true)
        {
            error = "Tailscale ist installiert, aber noch nicht angemeldet. Tailscale-App öffnen und anmelden.";
            return false;
        }

        var status = QueryStatus(localPort);
        if (!status.IsTailscaleAvailable)
        {
            error = status.Error ?? "Tailscale nicht verfügbar";
            return false;
        }

        if (status.IsFunnelActive && !string.IsNullOrWhiteSpace(status.PublicHostname))
        {
            publicHostname = status.PublicHostname;
            return true;
        }

        if (!TryRunTailscale($"funnel --bg http://127.0.0.1:{localPort}", out var startOutput, 15000))
        {
            error = string.IsNullOrWhiteSpace(startOutput)
                ? "tailscale funnel konnte nicht gestartet werden"
                : startOutput.Trim();
            return false;
        }

        Thread.Sleep(600);
        var after = QueryStatus(localPort);
        if (after.IsFunnelActive && !string.IsNullOrWhiteSpace(after.PublicHostname))
        {
            publicHostname = after.PublicHostname;
            return true;
        }

        var fallbackHost = after.PublicHostname ?? TryDetectPublicHostnameFromStatus();
        if (!string.IsNullOrWhiteSpace(fallbackHost))
        {
            publicHostname = fallbackHost;
            return true;
        }

        error = after.RawOutput ?? startOutput?.Trim() ?? "Funnel-URL nach Start nicht ermittelbar";
        return false;
    }

    public static bool TryStop(out string? error)
    {
        error = null;
        if (!TryRunTailscale("funnel reset", out var output, 10000))
        {
            error = string.IsNullOrWhiteSpace(output) ? "tailscale funnel reset fehlgeschlagen" : output.Trim();
            return false;
        }

        return true;
    }

    public static string? TryGetFunnelEnableUrl()
    {
        if (!TryRunTailscale("status --json", out var output, 5000) ||
            string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            if (!doc.RootElement.TryGetProperty("Self", out var selfEl) ||
                !selfEl.TryGetProperty("ID", out var idEl))
            {
                return null;
            }

            var nodeId = idEl.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(nodeId)
                ? null
                : $"https://login.tailscale.com/f/funnel?node={Uri.EscapeDataString(nodeId)}";
        }
        catch
        {
            return null;
        }
    }

    public static string? TryDetectPublicHostname()
    {
        var fromFunnel = QueryStatus().PublicHostname;
        if (!string.IsNullOrWhiteSpace(fromFunnel))
        {
            return fromFunnel;
        }

        return TryDetectPublicHostnameFromStatus();
    }

    public static string NormalizeHostname(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        trimmed = trimmed
            .Replace("wss://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("ws://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        var pathIndex = trimmed.IndexOf('/');
        if (pathIndex >= 0)
        {
            trimmed = trimmed[..pathIndex];
        }

        return trimmed.TrimEnd('.');
    }

    public static bool IsTsNetHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        host.Trim().EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase);

    private static string? TryDetectPublicHostnameFromStatus()
    {
        var login = TryGetLoginState();
        if (!string.IsNullOrWhiteSpace(login?.DnsName))
        {
            var normalized = NormalizeHostname(login.DnsName);
            if (IsTsNetHost(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static bool TryResolveFromPath(out string? executablePath)
    {
        executablePath = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "tailscale",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            var first = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first) && File.Exists(first))
            {
                executablePath = first;
                return true;
            }
        }
        catch
        {
            // optional
        }

        return false;
    }

    private static string? TryParsePublicHostname(string output)
    {
        var match = TsNetHostRegex.Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static int? TryParseProxiedLocalPort(string output)
    {
        var match = LocalProxyRegex.Match(output);
        return match.Success && int.TryParse(match.Groups[1].Value, out var port) ? port : null;
    }

    private static bool TryRunTailscale(string arguments, out string? combinedOutput, int timeoutMs)
    {
        combinedOutput = null;
        var executable = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(executable))
        {
            combinedOutput = "tailscale.exe nicht gefunden";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // optional
                }

                combinedOutput = "tailscale-Befehl Timeout";
                return false;
            }

            combinedOutput = string.Join(
                Environment.NewLine,
                new[] { stdout, stderr }.Where(static s => !string.IsNullOrWhiteSpace(s)));

            // funnel status / funnel start: Ausgabe auch bei Exit-Code != 0 auswerten
            if (process.ExitCode != 0 &&
                arguments.Contains("funnel", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(combinedOutput))
            {
                return true;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            combinedOutput = ex.Message;
            return false;
        }
    }
}
