using System.Net.Sockets;
using System.Text;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

public static class DisplayCatalog
{
    public static readonly string[] Outside =
    [
        "DS003", "DS003a", "DS003a_Krefeld", "DS003a_UESTRA", "DS021", "DS021T",
        "DS021 Testversion", "DS021 Test 2", "DS021 Test 3", "DS021neu", "FMA-S1",
        "Zielnummer", "Mobitec", "IBIS-IP"
    ];

    public static readonly string[] Interior =
    [
        "DS009", "DS003c", "TFT SWS", "Bustec", "ANNAX", "Gorba", "Alternativ", "TFT Lawo"
    ];
}

public static class DisplayTelegrams
{
    public static async Task<string> SendAsync(
        FahrSession session,
        string line,
        string destination,
        string stop,
        IReadOnlyList<string>? course = null)
    {
        DankeAnzeige.Remember(line, destination);
        var notes = new List<string>();
        var outside = session.OutsideProtocol;
        if (outside == "IBIS-IP")
        {
            notes.Add("IBIS-IP: die Anzeigen lesen den Dienst auf Port " +
                      (session.IbisIpListenPort <= 0 ? 8080 : session.IbisIpListenPort));
        }
        else if (outside == "Mobitec")
        {
            foreach (var frame in MobitecZiel.Frames(line, destination))
            {
                VehicleLink.Send(session.Rs485Port, frame, 4800);
            }

            notes.Add("Mobitec über RS485, 4800 Baud, 8N1");
        }
        else
        {
            var pause = outside.StartsWith("DS021 Test", StringComparison.Ordinal);
            foreach (var frame in OutsideFrames(outside, line, destination))
            {
                VehicleLink.Send(session.IbisPort, frame, 9600);
                if (pause)
                {
                    await Task.Delay(200);
                }
            }

            notes.Add($"{outside} über IBIS, 9600 Baud");
        }

        if (session.InteriorProtocol == "ANNAX")
        {
            foreach (var frame in AnnaxFrames(session, stop, line, destination, course))
            {
                VehicleLink.Send(session.IbisPort, frame, 9600, System.IO.Ports.Parity.Even, 7, System.IO.Ports.StopBits.Two);
            }
        }
        else
        {
            foreach (var frame in InteriorFrames(session, stop, line, destination, course))
            {
                VehicleLink.Send(session.IbisPort, frame, 9600);
            }
        }

        notes.Add(session.InteriorProtocol + (session.Ds009Additional && session.InteriorProtocol != "DS009" ? " + DS009" : ""));
        return string.Join("\n", notes);
    }

    private static IEnumerable<byte[]> OutsideFrames(string protocol, string line, string destination)
    {
        var text = string.IsNullOrWhiteSpace(destination) ? line : destination;
        switch (protocol)
        {
            case "DS021neu":
                var neu = Ds021NeuProgramBuilder.CreateDestinationTelegrams(line, text, line, text);
                yield return neu.Front;
                yield return neu.Side;
                yield break;
            case "FMA-S1":
                var cycle = new FmaS1ProgramBuilder.TextCycle(text, line);
                var fma = FmaS1ProgramBuilder.CreateDestinationTelegrams([cycle], [cycle], line);
                yield return fma.Front;
                yield return fma.Side;
                yield break;
            case "DS003":
            case "Zielnummer":
                yield return Ibis127("z" + TargetNumber(text));
                yield break;
            case "DS003a":
                yield return Ibis127("l" + TargetNumber(line));
                yield return Ibis127("zA4" + Pad16(text) + Pad16(""));
                yield break;
            case "DS003a_Krefeld":
                yield return KrefeldDestination(line, text);
                yield break;
            case "DS003a_UESTRA":
                yield return UestraDestination(line, text);
                yield break;
            case "DS021":
                yield return Ds021Lawo(1, line, text);
                yield return Ds021Lawo(2, line, text);
                yield break;
            case "DS021 Testversion":
                foreach (var frame in Ds021Test(1, line, text)) yield return frame;
                yield break;
            case "DS021 Test 2":
                foreach (var frame in Ds021Test(2, line, text)) yield return frame;
                yield break;
            case "DS021 Test 3":
                foreach (var frame in Ds021Test(3, line, text)) yield return frame;
                yield break;
            default:
                if (text.Contains('\n'))
                {
                    foreach (var frame in Ds021A4(line, text))
                    {
                        yield return frame;
                    }

                    yield break;
                }

                yield return Ds021tProgramBuilder.CreateFrontProgramA2([(line, text)], 5);
                yield return Ds021tProgramBuilder.CreateSideProgramA2([(line, text)], 5);
                yield break;
        }
    }

    private static IEnumerable<byte[]> InteriorFrames(
        FahrSession session,
        string stop,
        string line,
        string destination,
        IReadOnlyList<string>? course)
    {
        var stopText = string.IsNullOrWhiteSpace(stop) ? destination : stop;
        var sendDs009 = session.InteriorProtocol == "DS009" || session.Ds009Additional;
        if (sendDs009)
        {
            yield return Ds009(stopText, session.Ds009Length);
        }

        switch (session.InteriorProtocol)
        {
            case "DS003c":
                var field = session.Ds003cVerlauf
                    ? $"{stopText} {line} {destination}"
                    : stopText;
                yield return Ibis127("zI5" + Fit(IbisCharset(field), 20));
                break;
            case "Bustec":
                yield return KrefeldDestination(destination, destination);
                break;
            case "TFT SWS":
                foreach (var frame in TftSwsFrames(session, stopText, course)) yield return frame;
                break;
            case "Gorba":
                yield return Ibis127("l" + TargetNumber(line));
                yield return KrefeldDestination(destination, destination);
                if (session.TftTime)
                {
                    yield return Ibis127("u" + DateTime.Now.ToString("HHmm"));
                }
                break;
            case "TFT Lawo":
                var address = Math.Clamp(GeraetEinstellungen.Int("tft_lawo_display_address", 1), 1, 9);
                yield return Ds021Lawo(address, line, string.IsNullOrWhiteSpace(stopText) ? destination : stopText);
                break;
            case "Alternativ":
                var next = (course ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Take(3).ToArray();
                yield return Ds009(next.Length == 0 ? stopText : string.Join(" - ", next), session.Ds009Length);
                break;
        }
    }

    private static IEnumerable<byte[]> TftSwsFrames(FahrSession session, string stop, IReadOnlyList<string>? course)
    {
        var names = (course ?? [stop]).Where(name => !string.IsNullOrWhiteSpace(name)).Take(99).ToList();
        if (names.Count == 0)
        {
            names.Add(stop);
        }

        var address = Math.Clamp(session.TftSwsAddress <= 0 ? 7 : session.TftSwsAddress, 1, 9);
        for (var i = 0; i < names.Count; i++)
        {
            var status = i == 0 ? 0 : i == names.Count - 1 ? 2 : 1;
            yield return SwsText(address, status, i + 1, names[i]);
        }

        var current = names.FindIndex(name => string.Equals(name, stop, StringComparison.OrdinalIgnoreCase));
        yield return SwsIndex(current < 0 ? 1 : current + 1);
    }

    private static byte[] SwsText(int address, int status, int index, string text)
    {
        var body = new List<byte>
        {
            (byte)'a', (byte)'X', (byte)('0' + address), (byte)('0' + status), 0x03
        };
        body.AddRange(Encoding.ASCII.GetBytes(index.ToString("000")));
        body.Add(0x04);
        body.AddRange(TftSwsText.Encode(text));
        return Ibis127(body);
    }

    private static byte[] SwsIndex(int index)
    {
        var body = new List<byte> { (byte)'x', (byte)'2' };
        body.AddRange(Encoding.ASCII.GetBytes(Math.Clamp(index, 0, 999).ToString("000")));
        return Ibis127(Encoding.ASCII.GetString(body.ToArray()));
    }

    private static IEnumerable<byte[]> AnnaxFrames(
        FahrSession session,
        string stop,
        string line,
        string destination,
        IReadOnlyList<string>? course)
    {
        var address = Math.Clamp(session.AnnaxAddress is < 1 or > 15 ? 10 : session.AnnaxAddress, 1, 15);
        var hex = "0123456789ABCDEF"[address];
        if (session.TftTime)
        {
            yield return Annax("u" + DateTime.Now.ToString("HHmm"));
        }

        yield return Annax("l" + TargetNumber(line));
        var ziel = string.IsNullOrWhiteSpace(destination) ? stop : destination;
        var zielLines = ziel.Replace("\r", "").Split('\n');
        var z1 = AnnaxText(zielLines[0]);
        if (z1.Length > 40)
        {
            z1 = z1[..40];
        }

        if (zielLines.Length > 1 && zielLines[1].Trim().Length > 0)
        {
            var z2 = AnnaxText(zielLines[1].Trim());
            if (z2.Length > 40)
            {
                z2 = z2[..40];
            }

            yield return Annax("zA4" + z1 + "\n" + z2 + "\n");
        }
        else
        {
            yield return Annax("zA4" + z1 + "\n");
        }
        var names = (course ?? [stop])
            .Where(name => !string.IsNullOrWhiteSpace(name) &&
                           !name.StartsWith("Hinweis:", StringComparison.OrdinalIgnoreCase) &&
                           !name.Contains("Wegpunkt", StringComparison.OrdinalIgnoreCase))
            .Take(99)
            .ToList();
        yield return Annax("xI00");
        for (var i = 0; i < names.Count; i++)
        {
            var stopText = AnnaxText(names[i]);
            if (stopText.Length > 48)
            {
                stopText = stopText[..48];
            }

            var dataText = "\u0003" + (i + 1).ToString("00") + "\u0004" + stopText + "\u0005";
            var header = "aL" + hex + VdvHex(dataText.Length / 16) + VdvHex(dataText.Length % 16);
            yield return Annax(header + dataText);
        }

        var index = names.FindIndex(name => string.Equals(name, stop, StringComparison.OrdinalIgnoreCase));
        yield return Annax("xI" + (index < 0 ? 1 : index + 1).ToString("00"));
    }

    private static string AnnaxText(string value) =>
        value.Replace('ä', '{').Replace('Ä', '[').Replace('ö', '|').Replace('Ö', '\\')
            .Replace('ü', '}').Replace('Ü', ']').Replace('ß', '~');

    private static byte[] Annax(string payload)
    {
        var bytes = Encoding.ASCII.GetBytes(payload.EndsWith('\r') ? payload : payload + "\r");
        var sum = 0xFF;
        foreach (var b in bytes)
        {
            sum ^= b;
        }

        return bytes.Concat([(byte)(sum & 0x7F)]).ToArray();
    }

    private static IEnumerable<byte[]> Ds021Test(int variant, string line, string destination)
    {
        if (variant == 1)
        {
            yield return Ibis("l000");
            yield return Ibis("lE90");
            yield return Ibis("l000");
        }
        else if (variant == 2)
        {
            yield return Ibis("l000");
        }
        else
        {
            yield return Ibis("lE90");
            yield return Ibis("l000");
        }

        foreach (var frame in Ds021A4(line, destination))
        {
            yield return frame;
        }
    }

    private static IEnumerable<byte[]> Ds021A4(string line, string destination)
    {
        var lines = destination.Replace("\r", "").Split('\n').Select(part => part.Trim()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var goals = new List<(string A, string B)>();
        for (var i = 0; i < lines.Count;)
        {
            var first = lines[i];
            if (i + 1 < lines.Count)
            {
                goals.Add((first, lines[i + 1]));
                i += 2;
            }
            else
            {
                if (first.Length > 0)
                {
                    goals.Add((first, ""));
                }

                break;
            }
        }

        if (goals.Count >= 2 && IsVia(goals[1].A) && goals[0].A.Length > 0 && goals[0].B.Length > 0 && goals[1].B.Length > 0)
        {
            yield return A4(1, 3, [goals[0].A, goals[0].B, "", "}ber", goals[1].B], 6);
            yield return A4(3, 3, [goals[0].A, goals[0].B, "", "}ber", goals[1].B], 6);
            yield break;
        }

        if (goals.Count >= 2 && goals[0].B.Length > 0 && goals[1].A.Length > 0 && goals[1].B.Length > 0)
        {
            yield return A4(1, 3, [goals[0].A, goals[0].B, "", goals[1].A, goals[1].B], 4);
            yield return A4(3, 3, [goals[0].A, goals[0].B, "", goals[1].A, goals[1].B], 4);
            yield break;
        }

        if (lines.Count > 2)
        {
            yield return A4(1, 5, lines.ToArray(), 13);
            yield return A4(2, 5, lines.ToArray(), 13);
            yield break;
        }

        var top = goals.Count == 0 ? line : goals[0].A;
        var bottom = goals.Count == 0 ? destination : goals[0].B;
        var pad = A4Pad(bottom);
        yield return A4(1, 2, [top, bottom], pad);
        yield return A4(2, 2, [top, bottom], pad);
    }

    private static bool IsVia(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        return text is "über" or "ueber" or "}ber";
    }

    private static int A4Pad(string line2)
    {
        var length = Ds021Map(line2).Length;
        var need = Math.Max(0, 12 - length);
        if (need == 0)
        {
            return 0;
        }

        return need == 3 ? 5 : ((need + 3) / 4) * 4;
    }

    private static byte[] A4(int position, int linesCode, string[] lines, int spaces)
    {
        var message = $"aA{position}{linesCode}A4{string.Join("\n", lines.Select(Ds021Map))}\n\n{new string(' ', spaces)}\r";
        var bytes = Encoding.ASCII.GetBytes(message);
        byte parity = 127;
        foreach (var b in bytes)
        {
            parity ^= b;
        }

        return bytes.Concat([parity]).ToArray();
    }

    private static string Ds021Map(string value) =>
        value.Replace("ö", "|").Replace("Ö", "|").Replace("ü", "}").Replace("Ü", "}").Replace("ß", "~");

    private static byte[] Ds009(string text, int length)
    {
        var len = length <= 16 ? 16 : 20;
        var ibis = IbisCharset(text);
        if (ibis.Length > len)
        {
            ibis = ibis[..len];
        }

        var payload = ("v" + ibis).PadRight(1 + len) + "\r";
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(payload);
        byte parity = 0;
        foreach (var b in bytes)
        {
            parity ^= b;
        }

        parity ^= 0x7F;
        return [.. bytes, parity];
    }

    public static byte[] Ibis(string payload) => Ibis127(payload);

    private static byte[] Ibis127(string payload)
    {
        var bytes = Encoding.ASCII.GetBytes(payload + "\r");
        byte parity = 127;
        foreach (var b in bytes)
        {
            parity ^= b;
        }

        return [.. bytes, parity];
    }

    private static byte[] Ibis127(List<byte> payload)
    {
        var bytes = new byte[payload.Count + 1];
        payload.CopyTo(bytes);
        bytes[^1] = (byte)'\r';
        byte parity = 127;
        foreach (var b in bytes)
        {
            parity ^= b;
        }

        return [.. bytes, parity];
    }

    private static byte[] KrefeldDestination(string line1, string line2)
    {
        var a = IbisUpper(line1);
        var b = string.IsNullOrWhiteSpace(line2) ? a : IbisUpper(line2);
        return Ibis127($"zA4{a}\n{b}\n\n\n.DG5DG7DG7DG7   ");
    }

    private static byte[] Ds021Lawo(int address, string line, string destination)
    {
        var interval = '5';
        var l1 = IbisCharset(line);
        var l2 = IbisCharset(destination);
        var body = string.IsNullOrWhiteSpace(l2)
            ? $"{l1}\n\n\n"
            : $"{l1}\n{l2}\n\n";
        return Ibis127($"aA{address}A{interval}{body}");
    }

    private static byte[] UestraDestination(string line, string destination)
    {
        var data = $"\n.W{CaseSwitch(destination)}\n{CaseSwitch("")}\n.X{CaseSwitch(destination)}\n{CaseSwitch("")}\n.Y{CaseSwitch(line)}\n\n.C1100MMM";
        var blocks = (int)Math.Ceiling(data.Length / 4.0);
        var padded = data.PadRight(blocks * 4);
        return Ibis127("zA" + VdvHex(blocks).PadLeft(2) + padded);
    }

    private static string CaseSwitch(string text)
    {
        if (text.Length == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        if (!(char.IsUpper(text[0]) || char.IsDigit(text[0])))
        {
            sb.Append('\u0006');
        }

        for (var i = 0; i < text.Length; i++)
        {
            sb.Append(char.ToUpperInvariant(text[i]));
            if (i < text.Length - 1)
            {
                var currentUpper = char.IsUpper(text[i]) || char.IsDigit(text[i]);
                var nextUpper = char.IsUpper(text[i + 1]) || char.IsDigit(text[i + 1]);
                if (currentUpper != nextUpper)
                {
                    sb.Append('\u0006');
                }
            }
        }

        return sb.ToString();
    }

    private static string VdvHex(int value)
    {
        const string alphabet = "0123456789:;<=>?";
        return value > 15
            ? $"{alphabet[value >> 4]}{alphabet[value & 0x0F]}"
            : alphabet[value].ToString();
    }

    private static string Pad16(string text)
    {
        var upper = IbisCharset(text).ToUpperInvariant();
        return upper.Length <= 16 ? upper.PadRight(16) : upper[..16];
    }

    private static string IbisUpper(string text) => IbisCharset(text).ToUpperInvariant();

    private static string TargetNumber(string raw)
    {
        var digits = Digits(raw);
        return digits.Length switch
        {
            0 => "000",
            < 3 => digits.PadLeft(3, '0'),
            _ => digits.Length > 4 ? digits[..4] : digits
        };
    }

    private static async Task<string> SendIbisIpAsync(FahrSession session, string line, string destination)
    {
        var host = string.IsNullOrWhiteSpace(session.IbisIpHost) ? "127.0.0.1" : session.IbisIpHost.Trim();
        var port = session.IbisIpPort <= 0 ? 8080 : session.IbisIpPort;
        if (session.IbisIpMode == "HTTP" && !string.IsNullOrWhiteSpace(session.IbisIpUrl))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var body = new StringContent($"{{\"line\":\"{line}\",\"destination\":\"{destination}\"}}", Encoding.UTF8, "application/json");
            var response = await http.PostAsync(session.IbisIpUrl.Trim(), body);
            return $"IBIS-IP HTTP {(int)response.StatusCode}";
        }

        using var client = new TcpClient();
        await client.ConnectAsync(host, port);
        var payload = Encoding.ASCII.GetBytes($"DS021T {line} {destination}\n");
        await client.GetStream().WriteAsync(payload);
        return $"IBIS-IP {session.IbisIpMode} {host}:{port}";
    }

    private static string IbisCharset(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            sb.Append(ch switch
            {
                'ä' => '{',
                'Ä' => '[',
                'ö' => '|',
                'Ö' => '\\',
                'ü' => '}',
                'Ü' => ']',
                'ß' => '~',
                _ => ch
            });
        }

        return sb.ToString();
    }

    private static string Fit(string text, int length) =>
        text.Length <= length ? text.PadRight(length) : text[..length];

    private static string Digits(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? "0" : digits;
    }
}
