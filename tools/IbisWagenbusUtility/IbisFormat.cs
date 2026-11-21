using System.Globalization;
using System.Text;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.IbisWagenbusUtility;

/// <summary>Alle gängigen IBIS-Telegramme (GPSAnsagen / Planer / Gorba).</summary>
public static class IbisFormat
{
    public static byte[] Line(string lineNumber)
    {
        var digits = DigitsOnly(lineNumber, fallback: "000");
        return IbisTelegramBuilder.CreateDs001Line(PadLine3(digits));
    }

    public static byte[] Special(string code)
    {
        var c = code.Trim().ToUpperInvariant();
        if (c.Length == 0)
        {
            throw new ArgumentException("Sonderzeichen leer.");
        }

        return IbisTelegramBuilder.CreateDs001Special(c);
    }

    public static byte[] DestinationNumber(string number)
    {
        var digits = DigitsOnly(number, fallback: "0");
        if (digits.Length < 3)
        {
            digits = digits.PadLeft(3, '0');
        }
        else if (digits.Length > 4)
        {
            digits = digits[^4..];
        }

        return IbisTelegramBuilder.CreateDs003DestinationNumber(digits);
    }

    public static byte[] ClearDestination() => IbisTelegramBuilder.CreateIbisMessage("zA0");

    public static byte[] TwoLineZa4(string line1, string line2) =>
        IbisTelegramBuilder.CreateDs003aTwoLine(line1 ?? "", line2 ?? "");

    public static byte[] Krefeld(
        string front1,
        string front2,
        string side1,
        string side2,
        bool useZa5 = true) =>
        IbisTelegramBuilder.CreateDs003aKrefeld(
            front1 ?? "",
            front2,
            side1,
            side2,
            useZa4: !useZa5,
            useZa5: useZa5);

    public static byte[] KrefeldEmpty(bool useZa5 = true) =>
        IbisTelegramBuilder.CreateDs003aKrefeldEmpty(useZa4: !useZa5, useZa5: useZa5);

    /// <summary>DS009 Innen: <c>v</c> + 20 Zeichen + CR + Parität (Start 0, dann XOR 0x7F).</summary>
    public static byte[] Ds009StopText(string text)
    {
        var ibis = ToIbisCharset(text ?? "").PadRight(20)[..20];
        var padded = ("v" + ibis).PadRight(21, ' ');
        var body = Encoding.GetEncoding("ISO-8859-1").GetBytes(padded + "\r");
        byte parity = 0;
        foreach (var b in body)
        {
            parity ^= b;
        }

        parity ^= 0x7F;
        var result = new byte[body.Length + 1];
        Array.Copy(body, result, body.Length);
        result[^1] = parity;
        return result;
    }

    public static byte[] Ds021tFront(IReadOnlyList<(string, string)> goals, int intervalSeconds = 3) =>
        Ds021tProgramBuilder.CreateFrontProgramA2(NormalizeGoals(goals), intervalSeconds);

    public static byte[] Ds021tSide(IReadOnlyList<(string, string)> goals, int intervalSeconds = 3) =>
        Ds021tProgramBuilder.CreateSideProgramA2(NormalizeGoals(goals), intervalSeconds);

    public static byte[] Ds021tFront(string line1, string line2, int intervalSeconds = 3) =>
        Ds021tFront([(line1, line2)], intervalSeconds);

    public static byte[] Ds021tSide(string line1, string line2, int intervalSeconds = 3) =>
        Ds021tSide([(line1, line2)], intervalSeconds);

    public static byte[] Ds021NeuFront(string line1, string line2 = "") =>
        Ds021NeuProgramBuilder.CreateFrontTelegram(line1 ?? "", line2 ?? "");

    public static byte[] Ds021NeuSide(string line1, string line2 = "") =>
        Ds021NeuProgramBuilder.CreateSideTelegram(line1 ?? "", line2 ?? "");

    public static (byte[] Front, byte[] Side) Ds021NeuBoth(
        string front1,
        string front2,
        string side1,
        string side2) =>
        Ds021NeuProgramBuilder.CreateDestinationTelegrams(
            front1 ?? "",
            front2 ?? "",
            string.IsNullOrWhiteSpace(side1) ? front1 ?? "" : side1,
            string.IsNullOrWhiteSpace(side2) ? front2 ?? "" : side2);

    public static (byte[] Front, byte[] Side) FmaS1(
        IReadOnlyList<(string, string)> goals,
        string lineNumber,
        string? special = null)
    {
        var cycles = NormalizeGoals(goals)
            .Select(g => new FmaS1ProgramBuilder.TextCycle(g.Item1, g.Item2))
            .ToList();
        var y = FmaS1ProgramBuilder.ResolveYLineNumber(lineNumber, special);
        return FmaS1ProgramBuilder.CreateDestinationTelegrams(cycles, cycles, y);
    }

    /// <summary>Gorba TFT: nur Payload+CR (7E1), kein XOR.</summary>
    public static byte[] GorbaCrOnly(string payload) =>
        Encoding.ASCII.GetBytes(ToIbisCharset(payload) + "\r");

    public static byte[] GorbaDs005(string nextStop) =>
        GorbaCrOnly("a" + (nextStop ?? "").Trim());

    public static byte[] GorbaDs006(int index, string stopText)
    {
        if (index is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "DS006-Index 0–9");
        }

        return GorbaCrOnly($"b{index}{(stopText ?? "").Trim()}");
    }

    public static byte[] GorbaDs003Destination(string destination) =>
        GorbaCrOnly("z" + (destination ?? "").Trim());

    public static byte[] GorbaDs001Line(string lineNumber) =>
        GorbaCrOnly("l" + PadLine3(DigitsOnly(lineNumber, "000")));

    public static byte[] FromAsciiPayload(string payloadWithoutCr)
    {
        if (string.IsNullOrEmpty(payloadWithoutCr))
        {
            throw new ArgumentException("Payload leer.");
        }

        return IbisTelegramBuilder.CreateIbisMessage(payloadWithoutCr);
    }

    public static byte[] FromHex(string hex, bool appendParityIfMissing = false)
    {
        var cleaned = hex
            .Replace(",", " ")
            .Replace(";", " ")
            .Replace("0x", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<", " ")
            .Replace(">", " ");
        var parts = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new List<byte>();
        if (parts.Length == 1 && parts[0].Length % 2 == 0 && parts[0].All(Uri.IsHexDigit))
        {
            var s = parts[0];
            for (var i = 0; i < s.Length; i += 2)
            {
                bytes.Add(byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber));
            }
        }
        else
        {
            foreach (var p in parts)
            {
                bytes.Add(byte.Parse(p, NumberStyles.HexNumber));
            }
        }

        if (bytes.Count == 0)
        {
            throw new ArgumentException("Keine Hex-Bytes.");
        }

        if (appendParityIfMissing && bytes[^1] != 0x0D && !bytes.Contains((byte)0x0D))
        {
            bytes.Add(0x0D);
            byte parity = 127;
            foreach (var b in bytes)
            {
                parity ^= b;
            }

            bytes.Add(parity);
        }

        return bytes.ToArray();
    }

    public static string ToHexCsv(byte[] data) =>
        string.Join(",", data.Select(b => b.ToString("X2")));

    public static string ToAsciiLog(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data)
        {
            sb.Append(b switch
            {
                0x0D => "<CR>",
                0x0A => "<LF>",
                >= 32 and <= 126 => ((char)b).ToString(),
                _ => $"<{b:X2}>"
            });
        }

        return sb.ToString();
    }

    public static string ToIbisCharset(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

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

    private static List<(string, string)> NormalizeGoals(IReadOnlyList<(string, string)> goals)
    {
        var list = goals
            .Select(g => (g.Item1?.Trim() ?? "", g.Item2?.Trim() ?? ""))
            .Where(g => g.Item1.Length > 0 || g.Item2.Length > 0)
            .Take(4)
            .ToList();
        return list.Count == 0 ? [("", "")] : list;
    }

    private static string DigitsOnly(string raw, string fallback)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? fallback : digits;
    }

    private static string PadLine3(string digits)
    {
        if (digits.Length >= 3)
        {
            return digits[^3..];
        }

        return digits.PadLeft(3, '0');
    }
}
