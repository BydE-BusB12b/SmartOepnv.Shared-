using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Öffnet die Dropbox-Anmeldung und liest den Freigabecode aus der Weiterleitung,
/// damit das Fahrgerät selbst verbunden wird.
/// </summary>
internal static class DropboxAnmeldung
{
    public static async Task<string> WartenAufCodeAsync(string authorizeUrl, CancellationToken ct = default)
    {
        var browser = FindBrowser();
        if (browser is null)
        {
            throw new InvalidOperationException(
                "Chromium fehlt. Ohne Browser kann das Programm den Dropbox-Code nicht übernehmen.");
        }

        var profile = Path.Combine(Path.GetTempPath(), "smart-oepnv-dropbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var port = 9300 + Random.Shared.Next(0, 80);
        var start = new ProcessStartInfo(browser)
        {
            UseShellExecute = false
        };
        start.ArgumentList.Add("--remote-debugging-port=" + port);
        start.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        start.ArgumentList.Add("--user-data-dir=" + profile);
        start.ArgumentList.Add("--no-first-run");
        start.ArgumentList.Add("--no-default-browser-check");
        start.ArgumentList.Add("--disable-sync");
        start.ArgumentList.Add("--password-store=basic");
        start.ArgumentList.Add("--start-maximized");
        start.ArgumentList.Add("--new-window");
        start.ArgumentList.Add("--touch-events=enabled");
        start.ArgumentList.Add("--ozone-platform=wayland");
        start.ArgumentList.Add("--enable-wayland-ime");
        start.ArgumentList.Add(authorizeUrl);

        Bildschirmtastatur.SystemEin();
        var process = Process.Start(start)
            ?? throw new InvalidOperationException("Der Browser konnte nicht gestartet werden.");
        try
        {
            await Task.Delay(800, ct);
        }
        catch
        {
            process.Dispose();
            Bildschirmtastatur.SystemAus();
            throw;
        }

        if (process.HasExited)
        {
            process.Dispose();
            start.ArgumentList.Remove("--ozone-platform=wayland");
            start.ArgumentList.Remove("--enable-wayland-ime");
            process = Process.Start(start)
                ?? throw new InvalidOperationException("Der Browser konnte nicht gestartet werden.");
        }

        using var browserProcess = process;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddMinutes(4);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    throw new InvalidOperationException("Dropbox-Verbindung abgebrochen.");
                }

                await Task.Delay(400, ct);
                string json;
                try
                {
                    json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    continue;
                }

                var code = ReadCode(json);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    return code;
                }
            }

            throw new InvalidOperationException("Die Dropbox-Anmeldung wurde nicht abgeschlossen.");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Browser ist schon zu
            }

            try
            {
                Directory.Delete(profile, true);
            }
            catch
            {
                // Profilreste sind unkritisch
            }

            Bildschirmtastatur.SystemAus();
        }
    }

    private static string? ReadCode(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("url", out var urlProperty))
            {
                continue;
            }

            var raw = urlProperty.GetString();
            if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (!uri.Host.Equals("www.dropbox.com", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var query = Query(uri);
            if (query.TryGetValue("error", out var error) && !string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException("Dropbox-Fehler: " + error);
            }

            if (query.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code))
            {
                return code;
            }
        }

        return null;
    }

    private static Dictionary<string, string> Query(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(uri.Query))
        {
            return result;
        }

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2)
            {
                result[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1]);
            }
        }

        return result;
    }

    private static string? FindBrowser()
    {
        foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome", "google-chrome-stable" })
        {
            var path = Path.Combine("/usr/bin", name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
