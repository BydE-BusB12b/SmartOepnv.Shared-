using System.Text;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>IBIS-Telegramme wie <c>IbisMessage</c> (GPSAnsagen).</summary>
public static class IbisTelegramBuilder
{
    private static readonly Dictionary<string, string> IbisSpecialCharacters = new()
    {
        ["ä"] = "{",
        ["ö"] = "|",
        ["ü"] = "}",
        ["Ä"] = "[",
        ["Ö"] = "\\",
        ["Ü"] = "]",
        ["ß"] = "~"
    };

    public static byte[] CreateIbisMessage(string message) => Build(message);

    public static byte[] CreateDs003aTwoLine(string line1, string line2)
    {
        var l1 = Pad16(line1);
        var l2 = Pad16(line2);
        return Build("zA4" + l1 + l2);
    }

    /// <summary>DS003a Krefeld – Front (Zeile 1+2) oder Seite (Zeile 3+4).</summary>
    public static byte[] CreateDs003aKrefeld(
        string line1,
        string? line2,
        string? line3,
        string? line4,
        bool useZa4 = true,
        bool useZa5 = false,
        string? controlCodes = null,
        bool includeControlBlock = true)
    {
        static string ToIbisUpper(string src) =>
            new(src.Select(ch => ch is >= 'a' and <= 'z' ? char.ToUpperInvariant(ch) : ch).ToArray());

        var l1 = ToIbisUpper(line1);
        var l2 = ToIbisUpper(line2 ?? string.Empty);
        var l3 = ToIbisUpper(line3 ?? string.Empty);
        var l4 = ToIbisUpper(line4 ?? string.Empty);
        var textPart = string.Join("\n", new[] { l1, l2, l3, l4 });
        var id = useZa5 ? 5 : 4;

        var payload = includeControlBlock
            ? (controlCodes?.Trim().ToUpperInvariant() is { Length: 12 } codes
                ? $"zA{id}{textPart}\n.{codes}   "
                : $"zA{id}{textPart}\n.DG5DG7DG7DG7   ")
            : $"zA{id}{textPart}";

        return Build(payload);
    }

    public static byte[] CreateDs003aKrefeldEmpty(bool useZa4 = true, bool useZa5 = false)
    {
        var id = useZa5 ? 5 : 4;
        return Build($"zA{id}\n\n{new string(' ', 16)}");
    }

    private static string Pad16(string text) =>
        text.PadRight(16, ' ')[..16];

    private static byte[] Build(string message)
    {
        var sb = new StringBuilder(message);
        ReplaceIbisCharacters(sb);
        sb.Append('\r');
        var body = Encoding.ASCII.GetBytes(sb.ToString());
        var parity = CalculateParity(body);
        var result = new byte[body.Length + 1];
        Array.Copy(body, result, body.Length);
        result[^1] = parity;
        return result;
    }

    private static void ReplaceIbisCharacters(StringBuilder sb)
    {
        foreach (var (original, replacement) in IbisSpecialCharacters)
        {
            sb.Replace(original, replacement);
        }
    }

    private static byte CalculateParity(byte[] bytes)
    {
        byte parity = 127;
        foreach (var b in bytes)
        {
            parity = (byte)(parity ^ b);
        }

        return parity;
    }
}

/// <summary>DS003a Krefeld + DS021T Außenanzeigen für <c>outsideDisplays</c>.</summary>
public static class OutsideDisplayTelegramFactory
{
    public static (byte[] Front, byte[] Side) BuildKrefeldTelegrams(OutsideDisplayProgram program)
    {
        var line = NormalizeLine(program.Ds001Value);
        var noDestination = string.IsNullOrWhiteSpace(program.FrontLine1) &&
                            string.IsNullOrWhiteSpace(program.FrontLine2) &&
                            string.IsNullOrWhiteSpace(program.SideLine1) &&
                            string.IsNullOrWhiteSpace(program.SideLine2);

        if (noDestination && line == "000")
        {
            var clear = IbisTelegramBuilder.CreateIbisMessage("zA0");
            return (clear, clear);
        }

        byte[] front;
        if (!string.IsNullOrWhiteSpace(program.FrontLine1) || !string.IsNullOrWhiteSpace(program.FrontLine2))
        {
            front = IbisTelegramBuilder.CreateDs003aKrefeld(
                program.FrontLine1,
                program.FrontLine2,
                null,
                null,
                program.UseZa4,
                program.UseZa5,
                program.ControlCodes);
        }
        else
        {
            front = IbisTelegramBuilder.CreateDs003aKrefeldEmpty(program.UseZa4, program.UseZa5);
        }

        byte[] side;
        if (!string.IsNullOrWhiteSpace(program.SideLine1) || !string.IsNullOrWhiteSpace(program.SideLine2))
        {
            side = IbisTelegramBuilder.CreateDs003aKrefeld(
                string.Empty,
                null,
                program.SideLine1,
                program.SideLine2,
                program.UseZa4,
                program.UseZa5,
                program.ControlCodes);
        }
        else if (!string.IsNullOrWhiteSpace(program.FrontLine1) || !string.IsNullOrWhiteSpace(program.FrontLine2))
        {
            side = front;
        }
        else
        {
            side = IbisTelegramBuilder.CreateDs003aKrefeldEmpty(program.UseZa4, program.UseZa5);
        }

        return (front, side);
    }

    public static (byte[] Front, byte[] Side) BuildDs021tTelegrams(OutsideDisplayProgram program)
    {
        var frontGoals = OutsideDisplayCycleParser.CollectFrontGoals(program.FrontCycles);
        if (frontGoals.Count == 0)
        {
            frontGoals = [(program.FrontLine1, program.FrontLine2)];
        }

        var sideGoals = OutsideDisplayCycleParser.CollectSideGoals(program.SideCycles, frontGoals);

        string? special = program.Ds001Type == "special" ? program.Ds001Value.ToUpperInvariant() : null;
        var front = Ds021tProgramBuilder.CreateFrontProgramA2(frontGoals, program.IntervalSeconds, special);
        var side = Ds021tProgramBuilder.CreateSideProgramA2(sideGoals, program.IntervalSeconds, special);
        return (front, side);
    }

    private static string NormalizeLine(string value)
    {
        var raw = value.Trim().ToUpperInvariant();
        return Regex.IsMatch(raw, @"^[0-9]{1,3}$") ? raw.PadLeft(3, '0') : "001";
    }
}
