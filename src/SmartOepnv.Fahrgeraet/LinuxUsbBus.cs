using System.Runtime.InteropServices;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Ordnet auf Linux die USB-Wandler wie das Tablet zu:
/// CH340E bzw. IBIS-Platine = IBIS, CH340G bzw. RS485-Stick = RS485.
/// ttyUSB0/ttyUSB1 werden nicht verwendet, die Nummern tauschen sich nach einem Neustart.
/// </summary>
public static class LinuxUsbBus
{
    private const int VendorWch = 0x1A86;
    private const int VendorFtdi = 0x0403;
    private const int VendorProlific = 0x067B;
    private const int VendorSilabs = 0x10C4;

    private enum Die { Unknown, E, G }

    private sealed class Stick
    {
        public string Tty { get; init; } = "";
        public string OpenPath { get; init; } = "";
        public string Sysfs { get; init; } = "";
        public string ByPath { get; init; } = "";
        public string Serial { get; init; } = "";
        public string Blob { get; init; } = "";
        public int Vendor { get; init; }
        public Die Die { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Die> DieCache = new(StringComparer.Ordinal);
    private static int _started;
    private static int _retryScheduled;
    private static int _huntScheduled;
    private static int _huntAttempts;
    private static bool _allowFallback;

    public static void Start(FahrSession session)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    Apply(session);
                }
                catch (Exception ex)
                {
                    FahrProtokoll.Add("USB-Zuordnung: " + ex.Message);
                }

                await Task.Delay(TimeSpan.FromSeconds(8));
            }
        });
    }

    public static void Toggle(FahrSession session)
    {
        var values = GeraetEinstellungen.Load();
        var swapped = !GeraetEinstellungen.Bool(values, "usb_identical_ch340_swap", false);
        var ibisSerial = values.GetValueOrDefault("usb_ibis_serial") ?? "";
        var rsSerial = values.GetValueOrDefault("usb_mobitec_serial") ?? "";
        values["usb_identical_ch340_swap"] = swapped ? "true" : "false";
        values["usb_ibis_serial"] = rsSerial;
        values["usb_mobitec_serial"] = ibisSerial;
        GeraetEinstellungen.Save(values);
        lock (Gate)
        {
            DieCache.Clear();
        }

        Apply(session);
        FahrProtokoll.Add(swapped
            ? "IBIS und RS485 getauscht."
            : "IBIS und RS485 in Normalreihenfolge.");
    }

    public static bool Apply(FahrSession session)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        lock (Gate)
        {
            var sticks = Scan();
            var hubWait = sticks.Count == 1 && BehindExternalHub(sticks[0].Sysfs);
            if (!hubWait)
            {
                _huntAttempts = 0;
            }
            else if (_huntAttempts < 6)
            {
                _huntAttempts++;
                ScheduleHunt(session);
                return false;
            }

            var unresolved = Unresolved(sticks);
            var ibis = PickIbis(sticks);
            var rs = PickRs(sticks, ibis);
            if (ibis is null && rs is null)
            {
                ScheduleRetry(session, unresolved);
                return false;
            }

            var changed = false;
            if (ibis is not null && session.IbisPort != ibis.OpenPath)
            {
                session.IbisPort = ibis.OpenPath;
                changed = true;
            }

            if (rs is not null && session.Rs485Port != rs.OpenPath)
            {
                session.Rs485Port = rs.OpenPath;
                changed = true;
            }

            Remember(ibis, rs);
            if (changed)
            {
                var values = GeraetEinstellungen.Load();
                values["ibis_port"] = session.IbisPort;
                values["rs485_port"] = session.Rs485Port;
                GeraetEinstellungen.Save(values);
                FahrProtokoll.Add($"USB fest: IBIS {session.IbisPort} · RS485 {session.Rs485Port}");
            }

            if (ibis is not null && rs is not null)
            {
                _allowFallback = false;
            }
            else
            {
                ScheduleRetry(session, unresolved);
            }

            return changed;
        }
    }

    /// <summary>
    /// Nur ein externer Hub (nicht der Root-Hub des Pi) lässt den zweiten CH340 verzögert erscheinen.
    /// An eigenen Buchsen wird der einzelne Wandler sofort zugeordnet.
    /// </summary>
    private static bool BehindExternalHub(string sysfs)
    {
        var dir = Directory.GetParent(sysfs)?.FullName ?? "";
        for (var i = 0; i < 8 && dir.Length > 0; i++)
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith("usb", StringComparison.OrdinalIgnoreCase) &&
                name.Length > 3 && name[3..].All(char.IsDigit))
            {
                return false;
            }

            if (ReadSys(Path.Combine(dir, "bDeviceClass")) == "09")
            {
                return true;
            }

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        return false;
    }

    private static void ScheduleHunt(FahrSession session)
    {
        if (Interlocked.Exchange(ref _huntScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                Apply(session);
            }
            catch (Exception ex)
            {
                FahrProtokoll.Add("USB-Hub: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _huntScheduled, 0);
            }
        });
    }

    private static void ScheduleRetry(FahrSession session, bool unresolved)
    {
        if (!unresolved || Interlocked.Exchange(ref _retryScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(1200);
            _allowFallback = true;
            try
            {
                Apply(session);
            }
            catch (Exception ex)
            {
                FahrProtokoll.Add("USB-Zuordnung: " + ex.Message);
            }
            finally
            {
                _allowFallback = false;
            }
        });
    }

    private static void Remember(Stick? ibis, Stick? rs)
    {
        if (ibis is null || rs is null)
        {
            return;
        }

        if (ibis.Serial.Length == 0 || rs.Serial.Length == 0 || ibis.Serial == rs.Serial)
        {
            return;
        }

        var values = GeraetEinstellungen.Load();
        if (values.GetValueOrDefault("usb_ibis_serial") == ibis.Serial &&
            values.GetValueOrDefault("usb_mobitec_serial") == rs.Serial)
        {
            return;
        }

        values["usb_ibis_serial"] = ibis.Serial;
        values["usb_mobitec_serial"] = rs.Serial;
        GeraetEinstellungen.Save(values);
    }

    private static List<Stick> Scan()
    {
        var found = new List<Stick>();
        if (!Directory.Exists("/sys/class/tty"))
        {
            return found;
        }

        foreach (var dir in Directory.EnumerateDirectories("/sys/class/tty"))
        {
            var tty = Path.GetFileName(dir);
            if (!tty.StartsWith("ttyUSB", StringComparison.Ordinal) &&
                !tty.StartsWith("ttyACM", StringComparison.Ordinal))
            {
                continue;
            }

            var stick = TryRead(tty);
            if (stick is null || stick.Vendor == 0)
            {
                continue;
            }

            if (!DieCache.TryGetValue(stick.Sysfs, out var die))
            {
                die = Indistinguishable(stick) ? Probe(stick) : Die.Unknown;
                if (die != Die.Unknown)
                {
                    DieCache[stick.Sysfs] = die;
                }
            }

            stick.Die = die;
            found.Add(stick);
        }

        var present = found.Select(s => s.Sysfs).ToHashSet(StringComparer.Ordinal);
        foreach (var key in DieCache.Keys.Where(key => !present.Contains(key)).ToList())
        {
            DieCache.Remove(key);
        }

        return found;
    }

    private static Stick? TryRead(string tty)
    {
        try
        {
            var link = Path.Combine("/sys/class/tty", tty, "device");
            var iface = File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
            if (string.IsNullOrWhiteSpace(iface))
            {
                return null;
            }

            var usb = Directory.GetParent(iface)?.FullName;
            if (string.IsNullOrWhiteSpace(usb) || !File.Exists(Path.Combine(usb, "idVendor")))
            {
                return null;
            }

            var vendor = ParseHex(ReadSys(Path.Combine(usb, "idVendor")));
            var product = ReadSys(Path.Combine(usb, "product"));
            var maker = ReadSys(Path.Combine(usb, "manufacturer"));
            var serial = ReadSys(Path.Combine(usb, "serial"));
            var blob = $"{tty} {product} {maker} {serial}".ToUpperInvariant();
            return new Stick
            {
                Tty = tty,
                OpenPath = OpenPath(tty),
                Sysfs = usb,
                ByPath = ByPath(tty),
                Serial = serial.Trim(),
                Blob = blob,
                Vendor = vendor,
                Die = Die.Unknown
            };
        }
        catch
        {
            return null;
        }
    }

    private static Die Probe(Stick stick)
    {
        var node = UsbNodeOf(stick.Sysfs);
        if (node.Length == 0)
        {
            return Die.Unknown;
        }

        var fd = open(node, 2);
        if (fd < 0)
        {
            return Die.Unknown;
        }

        try
        {
            var die = Die.Unknown;
            for (var attempt = 0; attempt < 3 && die == Die.Unknown; attempt++)
            {
                var version = Control(fd, 0x5F, 0);
                var breakLen = Control(fd, 0x95, 0x0005);
                _ = version;
                die = breakLen switch
                {
                    2 => Die.E,
                    < 0 => Die.G,
                    _ => Die.Unknown
                };
                if (die == Die.Unknown && attempt < 2)
                {
                    Thread.Sleep(40);
                }
            }

            FahrProtokoll.Add($"CH340 {stick.Tty} die={die}");
            return die;
        }
        catch (Exception ex)
        {
            FahrProtokoll.Add($"CH340-Probe {stick.Tty}: {ex.Message}");
            return Die.Unknown;
        }
        finally
        {
            _ = close(fd);
        }
    }

    private static string UsbNodeOf(string sysfs)
    {
        var bus = ReadSys(Path.Combine(sysfs, "busnum"));
        var dev = ReadSys(Path.Combine(sysfs, "devnum"));
        if (!int.TryParse(bus, out var b) || !int.TryParse(dev, out var d))
        {
            return "";
        }

        return $"/dev/bus/usb/{b:D3}/{d:D3}";
    }

    private static int Control(int fd, byte request, ushort value)
    {
        var buffer = new byte[2];
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            if (IntPtr.Size == 8)
            {
                var transfer = new Ctrl64
                {
                    RequestType = 0xC0,
                    Request = request,
                    Value = value,
                    Length = 2,
                    Timeout = 400,
                    Data = pinned.AddrOfPinnedObject()
                };
                var box = GCHandle.Alloc(transfer, GCHandleType.Pinned);
                try
                {
                    return ioctl(fd, 0xC0185500, box.AddrOfPinnedObject());
                }
                finally
                {
                    box.Free();
                }
            }

            var narrow = new Ctrl32
            {
                RequestType = 0xC0,
                Request = request,
                Value = value,
                Length = 2,
                Timeout = 400,
                Data = pinned.AddrOfPinnedObject()
            };
            var box32 = GCHandle.Alloc(narrow, GCHandleType.Pinned);
            try
            {
                return ioctl(fd, 0xC0105500, box32.AddrOfPinnedObject());
            }
            finally
            {
                box32.Free();
            }
        }
        finally
        {
            pinned.Free();
        }
    }

    private static Stick? PickIbis(List<Stick> all)
    {
        var list = Order(all);
        if (list.Count == 0)
        {
            return null;
        }

        var serial = GeraetEinstellungen.Text("usb_ibis_serial", "");
        return list.FirstOrDefault(s => serial.Length > 0 && s.Serial == serial)
            ?? list.FirstOrDefault(LooksLikeIbis)
            ?? list.FirstOrDefault(IsIbisDie)
            ?? (Unresolved(list) ? null : list.FirstOrDefault(s => s.Vendor == VendorWch) ?? list[0]);
    }

    private static Stick? PickRs(List<Stick> all, Stick? ibis)
    {
        var list = Order(all.Where(s => ibis is null || s.Tty != ibis.Tty).ToList());
        if (list.Count == 0)
        {
            return null;
        }

        var serial = GeraetEinstellungen.Text("usb_mobitec_serial", "");
        return list.FirstOrDefault(s => serial.Length > 0 && s.Serial == serial)
            ?? list.FirstOrDefault(LooksLikeRs485)
            ?? list.FirstOrDefault(IsRs485Die)
            ?? list.FirstOrDefault(s => s.Vendor != VendorWch)
            ?? (Unresolved(list) ? null : list.Count >= 1 && all.Count >= 2 ? list[0] : null);
    }

    private static List<Stick> Order(List<Stick> sticks)
    {
        var sorted = sticks
            .OrderBy(s => s.ByPath, StringComparer.Ordinal)
            .ThenBy(s => s.Tty, StringComparer.Ordinal)
            .ToList();
        if (!GeraetEinstellungen.Bool("usb_identical_ch340_swap", false))
        {
            return sorted;
        }

        var indexes = sorted.Select((s, i) => (s, i)).Where(x => Indistinguishable(x.s)).Select(x => x.i).ToList();
        if (indexes.Count < 2)
        {
            return sorted;
        }

        var reversed = indexes.Select(i => sorted[i]).Reverse().ToList();
        for (var n = 0; n < indexes.Count; n++)
        {
            sorted[indexes[n]] = reversed[n];
        }

        return sorted;
    }

    private static bool Unresolved(IReadOnlyList<Stick> sticks)
    {
        var identical = sticks.Where(Indistinguishable).ToList();
        if (identical.Count < 2 || _allowFallback)
        {
            return false;
        }

        return identical.Count(s => s.Die is Die.E or Die.G) < 2;
    }

    private static bool IsIbisDie(Stick stick)
    {
        var want = GeraetEinstellungen.Bool("usb_identical_ch340_swap", false) ? Die.G : Die.E;
        return stick.Die == want;
    }

    private static bool IsRs485Die(Stick stick)
    {
        var want = GeraetEinstellungen.Bool("usb_identical_ch340_swap", false) ? Die.E : Die.G;
        return stick.Die == want;
    }

    private static bool Indistinguishable(Stick stick) =>
        stick.Vendor == VendorWch && !LooksLikeIbis(stick) && !LooksLikeRs485(stick);

    private static bool LooksLikeIbis(Stick stick) =>
        stick.Blob.Contains("IBIS", StringComparison.Ordinal) ||
        stick.Blob.Contains("CH340E", StringComparison.Ordinal) ||
        stick.Blob.Contains("CH340-E", StringComparison.Ordinal) ||
        stick.Blob.Contains("D42", StringComparison.Ordinal) ||
        stick.Blob.Contains("ED42", StringComparison.Ordinal) ||
        stick.Blob.Contains("WAGENBUS", StringComparison.Ordinal) ||
        stick.Blob.Contains("WAGEN-BUS", StringComparison.Ordinal);

    private static bool LooksLikeRs485(Stick stick) =>
        stick.Blob.Contains("RS485", StringComparison.Ordinal) ||
        stick.Blob.Contains("RS-485", StringComparison.Ordinal) ||
        stick.Blob.Contains("485", StringComparison.Ordinal) ||
        stick.Blob.Contains("CH340G", StringComparison.Ordinal) ||
        stick.Blob.Contains("CH340-G", StringComparison.Ordinal) ||
        stick.Vendor is VendorFtdi or VendorProlific or VendorSilabs;

    private static string OpenPath(string tty)
    {
        var node = "/dev/" + tty;
        var byId = MatchLink("/dev/serial/by-id", node);
        return byId ?? ByPath(tty) ?? node;
    }

    private static string ByPath(string tty) => MatchLink("/dev/serial/by-path", "/dev/" + tty) ?? "";

    private static string? MatchLink(string directory, string node)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var link in Directory.EnumerateFiles(directory))
        {
            try
            {
                var target = File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
                if (string.Equals(target, node, StringComparison.Ordinal))
                {
                    return link;
                }
            }
            catch
            {
                // defekter Verweis
            }
        }

        return null;
    }

    private static string ReadSys(string path) =>
        File.Exists(path) ? File.ReadAllText(path).Trim() : "";

    private static int ParseHex(string text) =>
        int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : 0;

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Ctrl64
    {
        [FieldOffset(0)] public byte RequestType;
        [FieldOffset(1)] public byte Request;
        [FieldOffset(2)] public ushort Value;
        [FieldOffset(4)] public ushort Index;
        [FieldOffset(6)] public ushort Length;
        [FieldOffset(8)] public uint Timeout;
        [FieldOffset(16)] public IntPtr Data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Ctrl32
    {
        [FieldOffset(0)] public byte RequestType;
        [FieldOffset(1)] public byte Request;
        [FieldOffset(2)] public ushort Value;
        [FieldOffset(4)] public ushort Index;
        [FieldOffset(6)] public ushort Length;
        [FieldOffset(8)] public uint Timeout;
        [FieldOffset(12)] public IntPtr Data;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, IntPtr data);
}
