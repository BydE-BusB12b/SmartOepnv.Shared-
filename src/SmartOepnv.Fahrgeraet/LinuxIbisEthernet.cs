namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// USB-Ethernet-Dongle am Raspberry. Grün nur solange der Stick steckt.
/// Eine liegen gebliebene eth-Schnittstelle nach dem Ziehen zählt nicht.
/// </summary>
public static class LinuxIbisEthernet
{
    public static bool Present()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/sys/class/net"))
        {
            return false;
        }

        var live = 0;
        foreach (var dir in Directory.EnumerateDirectories("/sys/class/net"))
        {
            var name = Path.GetFileName(dir);
            if (name is "lo" || name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("wl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var usb = UsbDeviceOf(Path.Combine(dir, "device"));
            if (usb is null)
            {
                continue;
            }

            live++;
        }

        return live > 0;
    }

    private static string? UsbDeviceOf(string deviceLink)
    {
        try
        {
            if (!Directory.Exists(deviceLink) && !File.Exists(deviceLink))
            {
                return null;
            }

            var target = File.ResolveLinkTarget(deviceLink, returnFinalTarget: true)?.FullName ?? deviceLink;
            var dir = target;
            for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "idVendor")))
                {
                    var vendor = File.ReadAllText(Path.Combine(dir, "idVendor")).Trim();
                    var product = File.Exists(Path.Combine(dir, "idProduct"))
                        ? File.ReadAllText(Path.Combine(dir, "idProduct")).Trim()
                        : "";
                    if (!target.Contains("/usb", StringComparison.OrdinalIgnoreCase) &&
                        !IsKnownEthernet(vendor, product))
                    {
                        return null;
                    }

                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName ?? "";
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool IsKnownEthernet(string vendor, string product)
    {
        var id = vendor.PadLeft(4, '0') + ":" + product.PadLeft(4, '0');
        return id is "0bda:8152" or "0bda:8153" or "0bda:8155" or "0bda:8156" or "0bda:8150"
            or "0b95:1790" or "0b95:178a" or "0b95:7720" or "0b95:772a" or "0b95:772b" or "0b95:7e2b"
            or "0fe6:9700" or "9710:7830" or "9710:7832";
    }
}
