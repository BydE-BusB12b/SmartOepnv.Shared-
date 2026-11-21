using System.Runtime.InteropServices;

namespace SmartOepnv.Fahrgeraet;

/// <summary>Empfangsbalken und Akkustand wie in der Tablet-Statuszeile.</summary>
internal static class LinuxHeaderStatus
{
    public static int SignalLevel()
    {
        if (!OperatingSystem.IsLinux())
        {
            return 0;
        }

        var quality = WirelessQuality();
        if (quality is int link)
        {
            return link switch
            {
                >= 60 => 4,
                >= 40 => 3,
                >= 25 => 2,
                > 0 => 1,
                _ => HasDefaultRoute() ? 1 : 0
            };
        }

        return HasDefaultRoute() ? 4 : 0;
    }

    public readonly record struct AkkuLage(bool Akku, DateTime? SeitUtc);

    /// <summary>Der Abschaltdienst schreibt den Zustand. Die Leitung selbst bleibt bei ihm.</summary>
    public static AkkuLage LiesAkku()
    {
        const string path = "/run/smart-oepnv/x1200-zustand";
        try
        {
            if (!OperatingSystem.IsLinux() || !File.Exists(path))
            {
                return new AkkuLage(false, null);
            }

            var parts = File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                parts[0] == "akku" &&
                long.TryParse(parts[1], out var unix))
            {
                return new AkkuLage(true, DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime);
            }
        }
        catch
        {
            // Datei gerade im Wechsel
        }

        return new AkkuLage(false, null);
    }

    public static int? BatteryPercent(out bool charging)
    {
        charging = false;
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/sys/class/power_supply"))
        {
            return null;
        }

        foreach (var dir in Directory.EnumerateDirectories("/sys/class/power_supply"))
        {
            var type = Read(Path.Combine(dir, "type"));
            if (!string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var capacity = Read(Path.Combine(dir, "capacity"));
            if (!int.TryParse(capacity, out var percent))
            {
                continue;
            }

            var status = Read(Path.Combine(dir, "status"));
            charging = status is "Charging" or "Full";
            return Math.Clamp(percent, 0, 100);
        }

        return X1200Stand();
    }

    /// <summary>
    /// Geekworm X1200: Maxim-Tankgeber MAX17040 an I2C-Adresse 0x36, Register 0x04 ist der Prozentwert.
    /// Ohne aufgesteckte Platine antwortet der Bus nicht, dann bleibt die Anzeige aus.
    /// </summary>
    private static DateTime _x1200Pause = DateTime.MinValue;

    private static int? X1200Stand()
    {
        if (DateTime.UtcNow < _x1200Pause || !File.Exists("/dev/i2c-1"))
        {
            return null;
        }

        var fd = open("/dev/i2c-1", 2);
        if (fd < 0)
        {
            _x1200Pause = DateTime.UtcNow.AddSeconds(10);
            return null;
        }

        try
        {
            if (ioctl(fd, 0x0703, 0x36) < 0 || write(fd, [0x04], 1) != 1)
            {
                _x1200Pause = DateTime.UtcNow.AddSeconds(10);
                return null;
            }

            var buf = new byte[2];
            if (read(fd, buf, 2) != 2)
            {
                _x1200Pause = DateTime.UtcNow.AddSeconds(10);
                return null;
            }

            var percent = buf[0] + buf[1] / 256.0;
            if (percent is < 0 or > 100.5)
            {
                return null;
            }

            _x1200Pause = DateTime.MinValue;
            return (int)Math.Clamp(Math.Round(percent), 0, 100);
        }
        finally
        {
            close(fd);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, int request, int arg);

    [DllImport("libc", SetLastError = true)]
    private static extern long write(int fd, byte[] buffer, long count);

    [DllImport("libc", SetLastError = true)]
    private static extern long read(int fd, byte[] buffer, long count);

    private static int? WirelessQuality()
    {
        var path = "/proc/net/wireless";
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (var line in File.ReadLines(path))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            var raw = fields[1].TrimEnd('.');
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var quality))
            {
                return (int)quality;
            }
        }

        return null;
    }

    private static bool HasDefaultRoute()
    {
        var path = "/proc/net/route";
        if (!File.Exists(path))
        {
            return false;
        }

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 1 && fields[1] == "00000000")
            {
                return true;
            }
        }

        return false;
    }

    private static string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch
        {
            return "";
        }
    }
}
