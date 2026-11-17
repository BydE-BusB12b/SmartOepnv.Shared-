using System.Diagnostics;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Ansagen laufen über Bluetooth: links innen, rechts außen.
/// Tastentöne, IVU-Töne und der Funk laufen über die Lautsprecher des HDMI-Displays.
/// </summary>
public static class TonAusgang
{
    public static string Describe()
    {
        var hdmi = HdmiCard();
        var mac = Mac();
        var ansage = string.IsNullOrWhiteSpace(mac)
            ? "keine Bluetooth-Adresse für die Ansagen"
            : Connected(mac) ? "Ansagen verbunden" : "Ansagen nicht verbunden";
        var display = hdmi is null ? "kein Display-Ton" : "Display " + hdmi;
        return ansage + " · " + display;
    }

    public static void Play(byte[] source, bool? inside, bool hoerer)
    {
        var wav = hoerer || inside is null ? StereoWav.ToBothChannels(source) : StereoWav.ToZonedWav(source, inside.Value);
        var path = Path.Combine(Path.GetTempPath(), hoerer ? "smartoepnv-hoerer.wav" : "smartoepnv-kabine.wav");
        File.WriteAllBytes(path, wav);
        if (OperatingSystem.IsWindows())
        {
            using var player = new System.Media.SoundPlayer(path);
            player.PlaySync();
            return;
        }

        if (hoerer && PlayHandset(path))
        {
            return;
        }

        PlayCabin(path);
    }

    /// <summary>Ansage auf Bluetooth. true = nur links (innen), false = nur rechts (außen), null = beide.</summary>
    public static void PlayAnnouncement(byte[] source, bool? inside)
    {
        var wav = inside is null ? StereoWav.ToBothChannels(source) : StereoWav.ToZonedWav(source, inside.Value);
        var path = Path.Combine(Path.GetTempPath(), "smartoepnv-ansage.wav");
        File.WriteAllBytes(path, wav);
        if (OperatingSystem.IsWindows())
        {
            using var player = new System.Media.SoundPlayer(path);
            player.PlaySync();
            return;
        }

        PlayHandset(path);
    }

    public static bool PlayFile(string path, bool hoerer)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            var wav = Path.Combine(Path.GetTempPath(), hoerer ? "smartoepnv-hoerer.wav" : "smartoepnv-kabine.wav");
            if (!DecodeMp3(path, wav))
            {
                return false;
            }

            path = wav;
        }

        if (OperatingSystem.IsWindows())
        {
            using var player = new System.Media.SoundPlayer(path);
            player.PlaySync();
            return true;
        }

        return hoerer && PlayHandset(path) || PlayCabin(path);
    }

    public static void PlayDisplay(byte[] source)
    {
        var path = Path.Combine(Path.GetTempPath(), "smartoepnv-display.wav");
        File.WriteAllBytes(path, StereoWav.ToBothChannels(source));
        if (OperatingSystem.IsWindows())
        {
            using var player = new System.Media.SoundPlayer(path);
            player.PlaySync();
            return;
        }

        PlayDisplayDevice(path);
    }

    public static bool PlayFileOnDisplay(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            var wav = Path.Combine(Path.GetTempPath(), "smartoepnv-display.wav");
            if (!DecodeMp3(path, wav))
            {
                return false;
            }

            path = wav;
        }

        if (OperatingSystem.IsWindows())
        {
            using var player = new System.Media.SoundPlayer(path);
            player.PlaySync();
            return true;
        }

        return PlayDisplayDevice(path);
    }

    private static bool PlayDisplayDevice(string path)
    {
        var card = HdmiCard();
        if (card is not null && Run("aplay", ["-D", "plughw:CARD=" + card + ",DEV=0", path], 120_000))
        {
            return true;
        }

        var sink = HdmiSink();
        if (sink is not null && Run("paplay", ["--device=" + sink, path], 120_000))
        {
            return true;
        }

        if (sink is not null && Run("pw-play", ["--target", sink, path], 120_000))
        {
            return true;
        }

        return Run("aplay", [path], 120_000);
    }

    private static bool PlayHandset(string path)
    {
        var mac = Mac();
        if (mac.Length == 0 || !Connected(mac))
        {
            return false;
        }

        var sink = BluezSink(mac);
        if (sink is not null && Run("paplay", ["--device=" + sink, path], 120_000))
        {
            return true;
        }

        if (Run("pw-play", ["--target", sink ?? mac, path], 120_000) && sink is not null)
        {
            return true;
        }

        return Run("aplay", ["-D", "bluealsa:DEV=" + mac + ",PROFILE=a2dp", path], 120_000);
    }

    private static bool PlayCabin(string path)
    {
        var card = UsbCard();
        if (card is not null && Run("aplay", ["-D", "plughw:CARD=" + card + ",DEV=0", path], 120_000))
        {
            return true;
        }

        return Run("aplay", [path], 120_000);
    }

    private static bool DecodeMp3(string mp3, string wav) =>
        Run("mpg123", ["-q", "-w", wav, mp3], 30_000) ||
        Run("ffmpeg", ["-y", "-i", mp3, wav], 30_000);

    public static string? UsbCard()
    {
        if (!Directory.Exists("/sys/class/sound"))
        {
            return null;
        }

        foreach (var dir in Directory.EnumerateDirectories("/sys/class/sound"))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("card", StringComparison.Ordinal))
            {
                continue;
            }

            var device = Path.Combine(dir, "device");
            var target = "";
            try
            {
                target = File.ResolveLinkTarget(device, returnFinalTarget: true)?.FullName ?? "";
            }
            catch
            {
                target = "";
            }

            if (!target.Contains("/usb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idPath = Path.Combine(dir, "id");
            if (!File.Exists(idPath))
            {
                continue;
            }

            var id = File.ReadAllText(idPath).Trim();
            if (id.Length > 0)
            {
                return id;
            }
        }

        return null;
    }

    public static string? HdmiCard()
    {
        if (!Directory.Exists("/sys/class/sound"))
        {
            return null;
        }

        var cards = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories("/sys/class/sound"))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("card", StringComparison.Ordinal))
            {
                continue;
            }

            var idPath = Path.Combine(dir, "id");
            if (!File.Exists(idPath))
            {
                continue;
            }

            var id = File.ReadAllText(idPath).Trim();
            if (id.Contains("hdmi", StringComparison.OrdinalIgnoreCase))
            {
                cards.Add(id);
            }
        }

        var port = ConnectedHdmiPort();
        if (port is int index)
        {
            var match = cards.FirstOrDefault(id => id.EndsWith(index.ToString(), StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return cards.Count == 0 ? null : cards[0];
    }

    private static int? ConnectedHdmiPort()
    {
        if (!Directory.Exists("/sys/class/drm"))
        {
            return null;
        }

        foreach (var dir in Directory.EnumerateDirectories("/sys/class/drm"))
        {
            var name = Path.GetFileName(dir);
            var marker = name.IndexOf("HDMI-A-", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                continue;
            }

            var statusPath = Path.Combine(dir, "status");
            if (!File.Exists(statusPath))
            {
                continue;
            }

            var status = File.ReadAllText(statusPath).Trim();
            if (!status.StartsWith("connected", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tail = name[(marker + "HDMI-A-".Length)..];
            if (int.TryParse(tail, out var port) && port >= 1)
            {
                return port - 1;
            }
        }

        return null;
    }

    private static string? HdmiSink()
    {
        var text = Capture("pactl", "list short sinks");
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            var name = parts.Length > 1 ? parts[1] : line;
            if (name.Contains("hdmi", StringComparison.OrdinalIgnoreCase))
            {
                return name.Trim();
            }
        }

        return null;
    }

    private static string Mac() =>
        GeraetEinstellungen.Text("bluetooth_device", "").Trim().ToUpperInvariant();

    private static bool Connected(string mac)
    {
        var info = Capture("bluetoothctl", "info " + mac);
        return info.Contains("Connected: yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BluezSink(string mac)
    {
        var compact = mac.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        var text = Capture("pactl", "list short sinks");
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            var name = parts.Length > 1 ? parts[1] : line;
            var folded = name.Replace(":", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
            if (folded.Contains(compact, StringComparison.Ordinal))
            {
                return name.Trim();
            }
        }

        return null;
    }

    private static string Capture(string file, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return "";
            }

            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);
            return text;
        }
        catch
        {
            return "";
        }
    }

    private static bool Run(string file, IReadOnlyList<string> args, int waitMs)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = file,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(waitMs);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
