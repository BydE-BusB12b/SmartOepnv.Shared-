using System.Text;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>DS021neu Außenanzeigen (wie GPSAnsagen <c>Ds021NeuMessage</c>).</summary>
public static class Ds021NeuProgramBuilder
{
    public const int FrontDisplayId = 1;
    public const int SideDisplayId = 2;

    public static byte[] CreateFrontTelegram(
        string line1,
        string line2 = "",
        Ds021NeuFontControl? fontControl = null) =>
        CreateTelegram(FrontDisplayId, line1, line2, isSide: false, fontControl ?? Ds021NeuFontControl.Default);

    public static byte[] CreateSideTelegram(
        string line1,
        string line2 = "",
        Ds021NeuFontControl? fontControl = null) =>
        CreateTelegram(SideDisplayId, line1, line2, isSide: true, fontControl ?? Ds021NeuFontControl.Default);

    public static (byte[] Front, byte[] Side) CreateDestinationTelegrams(
        string frontLine1,
        string frontLine2,
        string sideLine1,
        string sideLine2,
        Ds021NeuFontControl? frontFontControl = null)
    {
        var font = frontFontControl ?? Ds021NeuFontControl.Default;
        return (
            CreateFrontTelegram(frontLine1, frontLine2, font),
            CreateSideTelegram(sideLine1, sideLine2));
    }

    public static bool IsDs021NeuPayloadAscii(string payload) =>
        payload.StartsWith("aA", StringComparison.Ordinal) && payload.Contains(".CM", StringComparison.Ordinal);

    private static byte[] CreateTelegram(
        int displayId,
        string line1,
        string line2,
        bool isSide,
        Ds021NeuFontControl fontControl)
    {
        if (displayId is < 1 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(displayId), "DS021neu-Anzeige-ID muss 1–9 sein");
        }

        var mapped = (Transliterate(line1), Transliterate(line2));
        var controlSuffix = isSide ? Ds021NeuFontControl.Default.ControlSuffix() : fontControl.ControlSuffix();
        var linesCode = ResolveLinesCode(isSide, mapped, fontControl);
        var paddingSpaces = ResolvePaddingSpaces(isSide, mapped, fontControl);
        var body = BuildBody(isSide, mapped, linesCode);

        var message = new StringBuilder();
        message.Append($"aA{displayId}{linesCode}");
        message.Append(body);
        message.Append(new string(' ', paddingSpaces));
        message.Append('\n');
        message.Append(controlSuffix);
        message.Append('\r');

        return WrapWithParity(message.ToString());
    }

    private static string BuildBody(bool isSide, (string L1, string L2) goal, int linesCode)
    {
        var (l1, l2) = goal;
        var sb = new StringBuilder();
        if (isSide && string.IsNullOrEmpty(l1) && string.IsNullOrEmpty(l2) && linesCode == 1)
        {
            sb.Append('\n');
            sb.Append('\n');
            return sb.ToString();
        }

        if (string.IsNullOrEmpty(l2))
        {
            sb.Append(l1);
            sb.Append('\n');
            sb.Append('\n');
        }
        else
        {
            sb.Append(l1);
            sb.Append('\n');
            sb.Append(l2);
            sb.Append('\n');
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static int ResolveLinesCode(bool isSide, (string L1, string L2) goal, Ds021NeuFontControl fontControl)
    {
        var (l1, l2) = goal;
        var isSingleLine = string.IsNullOrEmpty(l2);
        var allEmpty = string.IsNullOrEmpty(l1) && isSingleLine;

        if (!isSide && !fontControl.IsDefaultNormal())
        {
            return 2;
        }

        if (isSide)
        {
            return allEmpty ? 1 : 2;
        }

        return isSingleLine ? 2 : 4;
    }

    private static int ResolvePaddingSpaces(bool isSide, (string L1, string L2) goal, Ds021NeuFontControl fontControl)
    {
        var isSingleLine = string.IsNullOrEmpty(goal.L2);
        if (!isSide && !fontControl.IsDefaultNormal())
        {
            return isSingleLine ? 15 : 2;
        }

        if (isSide)
        {
            var allEmpty = string.IsNullOrEmpty(goal.L1) && isSingleLine;
            return allEmpty ? 6 : isSingleLine ? 14 : 5;
        }

        return isSingleLine ? 15 : 4;
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
                case 'ä':
                case 'Ä':
                    sb.Append('{');
                    break;
                case 'ö':
                case 'Ö':
                    sb.Append('|');
                    break;
                case 'ü':
                case 'Ü':
                    sb.Append('$');
                    sb.Append('\u0001');
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
