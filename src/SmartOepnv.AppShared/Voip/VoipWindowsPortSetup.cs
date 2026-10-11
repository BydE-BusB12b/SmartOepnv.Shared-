using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.Voip;

/// <summary>Windows-Freigabe für HttpListener (URL-ACL + Firewall) – direkt aus der Leitstelle.</summary>
public static class VoipWindowsPortSetup
{
    /// <summary>Everyone/Welt – funktioniert auf deutschen und englischen Windows (kein „Jeder“/„Everyone“).</summary>
    private const string UrlAclUserSid = "S-1-1-0";

    public static bool LooksLikeAccessDenied(string? statusMessage) =>
        !string.IsNullOrWhiteSpace(statusMessage) &&
        (statusMessage.Contains("Zugriff verweigert", StringComparison.OrdinalIgnoreCase) ||
         statusMessage.Contains("Access is denied", StringComparison.OrdinalIgnoreCase));

    public static string InAppFixHint =>
        "Die Leitstelle richtet den VoIP-Port beim ersten Start automatisch ein (einmalig Windows-Administrator mit „Ja“).";

    public static bool WasSetupCompleted(VoipSettings settings) =>
        settings.WindowsPortSetupCompletedUtc is not null;

    public static void MarkSetupCompleted(VoipSettings settings)
    {
        settings.WindowsPortSetupCompletedUtc = DateTime.UtcNow;
        new VoipSettingsStore("Leitstelle").Save(settings);
    }

    /// <summary>Prüft ohne Admin-Rechte, ob URL-Reservierung für den Port fehlt.</summary>
    public static bool IsPortReservationMissing(VoipSettings settings)
    {
        try
        {
            var existing = ReadUrlAclListing();
            return !HasReservation(existing, settings);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Port automatisch freigeben (UAC) und warten, bis die Reservierung aktiv ist.</summary>
    public static async Task<bool> TryEnsurePortReadyAsync(
        VoipSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsPortReservationMissing(settings))
        {
            MarkSetupCompleted(settings);
            return true;
        }

        if (WasSetupCompleted(settings))
        {
            progress?.Report("VoIP-Port war bereits eingerichtet – warte auf Windows…");
            await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            if (!IsPortReservationMissing(settings))
            {
                return true;
            }
        }

        progress?.Report("VoIP-Port: Windows-Administrator mit „Ja“ bestätigen…");
        if (!TryLaunchElevatedRegistration(settings, out _, automatic: true))
        {
            return false;
        }

        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            if (!IsPortReservationMissing(settings))
            {
                MarkSetupCompleted(settings);
                progress?.Report("VoIP-Port freigegeben – Signaling wird gestartet…");
                return true;
            }
        }

        if (!IsPortReservationMissing(settings))
        {
            MarkSetupCompleted(settings);
            return true;
        }

        return false;
    }

    private static bool HasReservation(string listing, VoipSettings settings)
    {
        if (string.IsNullOrWhiteSpace(listing))
        {
            return false;
        }

        var port = settings.SignalingPort;
        var bindHost = VoipHttpListenHelper.ResolveBindHost(settings.ListenHost);
        var prefix = VoipHttpListenHelper.BuildPrefix(settings, bindHost).TrimEnd('/');
        if (listing.Contains(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return listing.Contains($":{port}/", StringComparison.Ordinal) ||
               listing.Contains($":{port}\r", StringComparison.Ordinal) ||
               listing.Contains($":{port}\n", StringComparison.Ordinal);
    }

    private static string ReadUrlAclListing()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = "http show urlacl",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null)
        {
            return string.Empty;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }

    /// <summary>UAC-Dialog → netsh urlacl + Firewall-Regel (ohne externe .bat/.ps1).</summary>
    public static bool TryLaunchElevatedRegistration(
        VoipSettings settings,
        out string message,
        bool automatic = false)
    {
        var port = settings.SignalingPort;
        var path = VoipConstants.SignalingWebSocketPath.TrimEnd('/');
        var scriptPath = Path.Combine(Path.GetTempPath(), "SmartOepnv-VoipPortSetup.ps1");

        var closePrompt = automatic
            ? string.Empty
            : "\nRead-Host 'Enter zum Schliessen'";

        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $port = {{port}}
            $path = '{{path}}'
            $userSid = '{{UrlAclUserSid}}'
            $urls = @(
                "http://+:$port/",
                "http://127.0.0.1:$port/"
            )
            $removeUrls = @(
                "http://+:$port$path/",
                "http://127.0.0.1:$port$path/"
            )
            foreach ($old in $removeUrls) {
                netsh http delete urlacl url=$old 2>$null
            }
            $acl = netsh http show urlacl 2>$null
            $ok = $true

            if (-not {{automatic.ToString().ToLowerInvariant()}}) {
                Write-Host '=== Smart-OEPNV VoIP Port ===' -ForegroundColor Cyan
            }
            foreach ($url in $urls) {
                if ($acl -like "*$url*") { continue }
                $null = netsh http add urlacl url=$url user=$userSid 2>&1
                if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 183) { $ok = $false }
            }

            netsh advfirewall firewall delete rule name="Smart-OEPNV VoIP" | Out-Null
            $null = netsh advfirewall firewall add rule name="Smart-OEPNV VoIP" dir=in action=allow protocol=TCP localport=$port 2>&1
            if ($LASTEXITCODE -ne 0) { $ok = $false }

            netsh advfirewall firewall delete rule name="Smart-OEPNV VoIP Media" | Out-Null
            $null = netsh advfirewall firewall add rule name="Smart-OEPNV VoIP Media" dir=in action=allow protocol=UDP localport=10000-65535 2>&1
            if ($LASTEXITCODE -ne 0) { $ok = $false }

            if (-not {{automatic.ToString().ToLowerInvariant()}}) {
                Write-Host ''
                if ($ok) {
                    Write-Host 'VoIP-Port freigegeben.' -ForegroundColor Green
                } else {
                    Write-Host 'Teilweise fehlgeschlagen – Leitstelle testen.' -ForegroundColor Yellow
                }
            }
            {{closePrompt}}
            """;

        try
        {
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = automatic
                    ? $"-ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -File \"{scriptPath}\""
                    : $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = automatic ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            });

            message = automatic
                ? "VoIP-Port-Freigabe läuft (Windows-Administrator bestätigen)."
                : "UAC mit „Ja“ bestätigen – VoIP startet danach automatisch neu.";
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            message = "Abgebrochen – Administratorrechte wurden nicht erteilt.";
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}
