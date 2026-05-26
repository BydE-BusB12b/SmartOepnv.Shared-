using System.Text;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>DS021T A2-Programme für Außenanzeigen (wie <c>Ds021tMessage.createFrontProgramA2</c>).</summary>
public static class Ds021tProgramBuilder
{
    public static byte[] CreateFrontProgramA2(
        IReadOnlyList<(string, string)> goals,
        int intervalSeconds,
        string? specialChar = null) =>
        BuildA2Program(1, goals, intervalSeconds, specialChar);

    public static byte[] CreateSideProgramA2(
        IReadOnlyList<(string, string)> goals,
        int intervalSeconds,
        string? specialChar = null) =>
        BuildA2Program(2, goals, intervalSeconds, specialChar);

    private static byte[] BuildA2Program(
        int position,
        IReadOnlyList<(string, string)> goals,
        int intervalSeconds,
        string? specialChar)
    {
        var safeGoals = goals.Count == 0
            ? new List<(string, string)> { ("", "") }
            : goals.ToList();

        var mappedGoals = safeGoals
            .Select(g => (Transliterate(g.Item1), Transliterate(g.Item2)))
            .ToList();

        var isSingle = mappedGoals.Count == 1;
        var isSingleLine = isSingle && string.IsNullOrEmpty(mappedGoals[0].Item2);

        var body = new StringBuilder();
        foreach (var (l1, l2) in mappedGoals)
        {
            if (string.IsNullOrEmpty(l2))
            {
                body.Append(l1);
                body.Append('\n');
                body.Append('\n');
                body.Append('\n');
            }
            else
            {
                body.Append(l1);
                body.Append('\n');
                body.Append(l2);
                body.Append('\n');
                body.Append('\n');
            }
        }

        var tail = isSingle
            ? isSingleLine
                ? new string(' ', 8) + "\r"
                : new string(' ', 6) + "\r"
            : new string(' ', 7) + "\r";

        string header;
        if (isSingle)
        {
            var linesCode = isSingleLine ? 2 : 3;
            var intervalCodeChar = GetIntervalCodeChar(intervalSeconds, specialChar);
            header = $"aA{position}{linesCode}A{intervalCodeChar}";
        }
        else
        {
            var linesCode = mappedGoals.Count == 2 ? 5 : 4;
            var intervalCodeChar = GetIntervalCodeChar(intervalSeconds, specialChar);
            header = $"aA{position}{linesCode}A{intervalCodeChar}";
        }

        var message = header + body + tail;
        var bytes = Encoding.ASCII.GetBytes(message);
        var parity = CalculateParity(bytes);
        var finalBytes = new byte[bytes.Length + 1];
        Array.Copy(bytes, finalBytes, bytes.Length);
        finalBytes[^1] = parity;
        return finalBytes;
    }

    private static string GetIntervalCodeChar(int intervalSeconds, string? specialChar)
    {
        if (specialChar is not null &&
            Regex.IsMatch(specialChar, @"^[A-Z]01$") &&
            intervalSeconds == 1)
        {
            return "????????";
        }

        return intervalSeconds switch
        {
            1 => "0",
            2 => "2",
            3 => "4",
            4 => "6",
            5 => "8",
            6 => ":",
            7 => "<",
            8 => ">",
            _ => (intervalSeconds <= 1 ? 0 : 2 * (intervalSeconds - 1)).ToString()
        };
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

    private static string Transliterate(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case 'ö':
                case 'Ö':
                    sb.Append('|');
                    break;
                case 'ü':
                case 'Ü':
                    sb.Append('}');
                    break;
                case 'ß':
                    sb.Append('~');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }
}
