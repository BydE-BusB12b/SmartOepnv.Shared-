using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>FMA-S1 Außenanzeigen (wie GPSAnsagen <c>FmaS1Message</c>).</summary>
public static class FmaS1ProgramBuilder
{
    private const string FooterSuffixQuestion = "0000000111MM070111";
    private const string FooterSuffixCh3 = "3000000111MM070111";
    private const int LargeLineNextMax = 11;
    private const int LargeLineLastMax = 8;
    private const int Format6MaxSecondLarge = 7;
    private const int FragmentedHeaderMinLength = 15;

    public const int FrontDisplayId = 1;
    public const int SideDisplayId = 2;

    public readonly record struct TextCycle(string LargeLine, string SmallLine = "");

    public static byte[] CreateFrontTelegram(
        IReadOnlyList<TextCycle> cycles,
        string lineNumber) =>
        CreateTelegram(FrontDisplayId, cycles, lineNumber, isSide: false);

    public static byte[] CreateSideTelegram(
        IReadOnlyList<TextCycle> cycles,
        string lineNumber) =>
        CreateTelegram(SideDisplayId, cycles, lineNumber, isSide: true);

    public static (byte[] Front, byte[] Side) CreateDestinationTelegrams(
        IReadOnlyList<TextCycle> frontCycles,
        IReadOnlyList<TextCycle> sideCycles,
        string lineNumber)
    {
        var expandedFront = ExpandCycles(frontCycles);
        var expandedSide = ExpandCycles(sideCycles);
        if (expandedSide.Count == 0)
        {
            expandedSide = expandedFront;
        }

        return (
            CreateFrontTelegram(expandedFront, lineNumber),
            CreateSideTelegram(expandedSide, lineNumber));
    }

    public static (byte[] Front, byte[] Side) CreateDestinationTelegrams(
        string frontLine1,
        string frontLine2 = "",
        string sideLine1 = "",
        string sideLine2 = "",
        string lineNumber = "")
    {
        var front = new List<TextCycle> { new(frontLine1, frontLine2) };
        var sideLarge = string.IsNullOrEmpty(sideLine1) ? frontLine1 : sideLine1;
        var sideSmall = string.IsNullOrEmpty(sideLine2) ? frontLine2 : sideLine2;
        var side = new List<TextCycle> { new(sideLarge, sideSmall) };
        return CreateDestinationTelegrams(front, side, lineNumber);
    }

    /// <summary>`.Y`-Feld: `00` + letzte 2 Linienziffern + 2 Ziffern aus Sonderzeichen (028 + E05 → 002805).</summary>
    public static string ResolveYLineNumber(string? ds001Line, string? ds001Spec)
    {
        var lineDigits = Regex.Replace(ds001Line ?? string.Empty, @"\D", string.Empty);
        lineDigits = lineDigits.Length <= 2
            ? lineDigits.PadLeft(2, '0')
            : lineDigits[^2..];
        if (lineDigits.Length == 0)
        {
            lineDigits = "00";
        }

        var spec = ds001Spec?.Trim().ToUpperInvariant();
        var specDigits = !string.IsNullOrWhiteSpace(spec) && Regex.IsMatch(spec, @"^[A-Z][0-9]{2}$")
            ? spec[1..]
            : "00";
        return $"00{lineDigits}{specDigits}";
    }

    public static string ResolveLineNumberField(string? ds001Line, string? ds001Spec) =>
        ResolveYLineNumber(ds001Line, ds001Spec);

    public static bool IsFmaS1PayloadAscii(string payload) =>
        payload.StartsWith("aA", StringComparison.Ordinal) &&
        (payload.Contains(".WS", StringComparison.Ordinal) ||
         payload.Contains(".WV", StringComparison.Ordinal) ||
         payload.Contains(".XS", StringComparison.Ordinal) ||
         payload.Contains(".XV", StringComparison.Ordinal)) &&
        (payload.Contains(".CH", StringComparison.Ordinal) ||
         payload.Contains(".CA", StringComparison.Ordinal));

    public static List<TextCycle> ExpandCycles(IReadOnlyList<TextCycle> cycles)
    {
        var nonEmpty = cycles
            .Where(c => !string.IsNullOrWhiteSpace(c.LargeLine) || !string.IsNullOrWhiteSpace(c.SmallLine))
            .ToList();
        if (nonEmpty.Count == 0)
        {
            return [new TextCycle(string.Empty, string.Empty)];
        }

        if (nonEmpty.Count == 1 && !string.IsNullOrWhiteSpace(nonEmpty[0].SmallLine))
        {
            return SplitTwoLinesToCycles(nonEmpty[0].LargeLine, nonEmpty[0].SmallLine);
        }

        if (LooksLikeFragmentedTwoLineDestination(nonEmpty))
        {
            var line1 = nonEmpty[0].LargeLine;
            var line2 = ReassembleFragmentedLine2(nonEmpty);
            return SplitTwoLinesToCycles(line1, line2);
        }

        return nonEmpty
            .Select(c => new TextCycle(NormalizeText(c.LargeLine), NormalizeText(c.SmallLine)))
            .ToList();
    }

    public static List<TextCycle> SplitTwoLinesToCycles(string line1, string line2)
    {
        var normalizedLine1 = NormalizeText(line1);
        var normalizedLine2 = NormalizeText(line2);
        if (string.IsNullOrEmpty(normalizedLine2))
        {
            return [new TextCycle(normalizedLine1, string.Empty)];
        }

        var cycles = new List<TextCycle> { new(normalizedLine1, string.Empty) };
        var rest = normalizedLine2;

        if (rest.Length > 0)
        {
            cycles[^1] = cycles[^1] with { SmallLine = rest[..1] };
            rest = rest[1..];
        }

        while (rest.Length > 0 && cycles.Count < 4)
        {
            var largeMax = cycles.Count == 1 ? LargeLineNextMax : LargeLineLastMax;
            var isFinalCycle = cycles.Count >= 2;
            var large = isFinalCycle ? rest : TakeLargeChunk(rest, largeMax);
            rest = rest[large.Length..];
            cycles.Add(new TextCycle(large, string.Empty));
            if (rest.Length == 0)
            {
                break;
            }

            var smallLen = rest.StartsWith(' ') ? Math.Min(2, rest.Length) : 1;
            cycles[^1] = cycles[^1] with { SmallLine = rest[..smallLen] };
            rest = rest[smallLen..];
        }

        return cycles;
    }

    private static bool LooksLikeFragmentedTwoLineDestination(IReadOnlyList<TextCycle> cycles)
    {
        if (cycles.Count < 2)
        {
            return false;
        }

        var first = cycles[0];
        if (first.LargeLine.Length < FragmentedHeaderMinLength)
        {
            return false;
        }

        var second = cycles[1];
        if (string.IsNullOrEmpty(first.SmallLine) && string.IsNullOrEmpty(second.SmallLine))
        {
            return true;
        }

        return first.SmallLine.Length == 1;
    }

    private static string ReassembleFragmentedLine2(IReadOnlyList<TextCycle> cycles)
    {
        var first = cycles[0];
        if (string.IsNullOrEmpty(first.SmallLine))
        {
            return string.Concat(cycles.Skip(1).Select(c => c.LargeLine + c.SmallLine));
        }

        var line2 = new StringBuilder(first.SmallLine);
        foreach (var cycle in cycles.Skip(1))
        {
            line2.Append(cycle.LargeLine);
            line2.Append(cycle.SmallLine);
        }

        return line2.ToString();
    }

    private static string TakeLargeChunk(string rest, int max)
    {
        if (rest.Length <= max)
        {
            return rest;
        }

        var chunk = rest[..max];
        if (rest.Length > max && chunk[^1] != ' ')
        {
            var spaceIdx = chunk.LastIndexOf(' ');
            if (spaceIdx > 0)
            {
                return chunk[..spaceIdx];
            }
        }

        return chunk;
    }

    private static byte[] CreateTelegram(
        int displayId,
        IReadOnlyList<TextCycle> cycles,
        string lineNumber,
        bool isSide)
    {
        var nonEmptyCount = cycles.Count(c =>
            !string.IsNullOrWhiteSpace(c.LargeLine) || !string.IsNullOrWhiteSpace(c.SmallLine));
        var formatCode = ResolveFormatCode(cycles);
        var slotCount = formatCode switch
        {
            6 => 3,
            7 => Math.Clamp(nonEmptyCount, 3, 4),
            _ => 2
        };

        var slots = cycles.Take(slotCount).ToList();
        while (slots.Count < slotCount)
        {
            slots.Add(new TextCycle(string.Empty, string.Empty));
        }

        if (formatCode >= 6)
        {
            NormalizeLastContentCycleSmall(slots);
        }

        var useVariantV = formatCode == 5 && nonEmptyCount >= 2;
        var blockLetter = isSide ? 'X' : 'W';
        var variantLetter = useVariantV ? 'V' : 'S';
        var fourFullCycles = formatCode == 7 && nonEmptyCount >= 4;

        var body = new StringBuilder();
        body.Append($"aA{displayId}{formatCode}\n");
        body.Append($".{blockLetter}{variantLetter}\n");
        foreach (var cycle in slots)
        {
            body.Append($".+{cycle.LargeLine}\n\n");
            body.Append($".-{cycle.SmallLine}\n");
        }

        if (formatCode >= 6)
        {
            body.Append(" \n \n \n");
        }

        body.Append($".Y{FormatLineNumberField(lineNumber, formatCode, useVariantV, fourFullCycles)}\n\n");
        body.Append($".C{ResolveControlBlock(formatCode, isSide, useVariantV, fourFullCycles)}\r");
        return WrapWithParity(body.ToString());
    }

    private static void NormalizeLastContentCycleSmall(List<TextCycle> slots)
    {
        var lastContentIndex = -1;
        for (var i = slots.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(slots[i].LargeLine))
            {
                lastContentIndex = i;
                break;
            }
        }

        if (lastContentIndex < 0)
        {
            return;
        }

        var last = slots[lastContentIndex];
        if (string.IsNullOrEmpty(last.SmallLine))
        {
            slots[lastContentIndex] = last with { SmallLine = " " };
        }
    }

    private static int ResolveFormatCode(IReadOnlyList<TextCycle> cycles)
    {
        var nonEmptyCount = cycles.Count(c =>
            !string.IsNullOrWhiteSpace(c.LargeLine) || !string.IsNullOrWhiteSpace(c.SmallLine));
        if (nonEmptyCount <= 2)
        {
            return 5;
        }

        if (nonEmptyCount == 3)
        {
            var secondLarge = cycles.ElementAtOrDefault(1).LargeLine ?? string.Empty;
            if (secondLarge.Length <= Format6MaxSecondLarge)
            {
                return 6;
            }
        }

        return 7;
    }

    private static string FormatLineNumberField(
        string lineNumber,
        int formatCode,
        bool useVariantV,
        bool fourFullCycles)
    {
        var digits = new string(lineNumber.Where(char.IsDigit).Take(6).ToArray()).PadLeft(6, '0');
        var spaces = formatCode switch
        {
            7 when fourFullCycles => 14,
            7 => 7,
            6 => 1,
            _ when useVariantV => 1,
            _ => 8
        };
        return digits + new string(' ', spaces);
    }

    private static string ResolveControlBlock(
        int formatCode,
        bool isSide,
        bool useVariantV,
        bool fourFullCycles) =>
        formatCode == 7 && !isSide && fourFullCycles ? $"H?{FooterSuffixQuestion}" :
        formatCode is 6 or 7 ? $"H3{FooterSuffixCh3}" :
        formatCode == 5 && useVariantV ? $"H3{FooterSuffixCh3}" :
        $"A?{FooterSuffixQuestion}";

    private static string NormalizeText(string input) =>
        FoldGermanToAscii(input).ToUpper(CultureInfo.GetCultureInfo("de-DE"));

    private static string FoldGermanToAscii(string input)
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
                case 'ä':
                case 'Ä':
                    sb.Append(char.IsLower(ch) ? "ae" : "AE");
                    break;
                case 'ö':
                case 'Ö':
                    sb.Append(char.IsLower(ch) ? "oe" : "OE");
                    break;
                case 'ü':
                case 'Ü':
                    sb.Append(char.IsLower(ch) ? "ue" : "UE");
                    break;
                case 'ß':
                    sb.Append("ss");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    private static byte[] WrapWithParity(string message)
    {
        var bytes = Encoding.ASCII.GetBytes(message);
        var parity = CalculateParity(bytes);
        var result = new byte[bytes.Length + 1];
        Array.Copy(bytes, result, bytes.Length);
        result[^1] = parity;
        return result;
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
