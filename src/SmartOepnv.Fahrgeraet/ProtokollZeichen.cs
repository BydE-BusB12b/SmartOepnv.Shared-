using System.Globalization;
using System.Text;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Zeichenbytes wie in der Tablet-App: Mobitec-ü/Ü als Bitmap plus Basisbuchstabe,
/// TFT-SWS-Text mit Cp850-Umschaltung.
/// </summary>
public static class MobitecUmlaut
{
    private const int BitmapFont = 0x77;

    private sealed record Glyph(byte[][] OverlaysX, byte[][] OverlaysY, byte[][] Data, int LetterY);

    private static readonly Glyph Lower69 = One(0x00, 0x04, 0x0F, "23 23 20 20 20 23 23");
    private static readonly Glyph Lower36 = One(0x00, 0x04, 0x0C, "26 26 20 20 20 20 26 26");
    private static readonly Glyph Lower6C = Two(0x00, 0x04, 0x04, 0x09, 0x0C, "2C 2C 20 20 24 24", "21 21");
    private static readonly Glyph Lower64 = Two(0x00, 0x04, 0x04, 0x09, 0x0B, "2C 2C 20 20 24 24", "21 21");
    private static readonly Glyph Lower65 = One(0x00, 0x04, 0x0B, "28 20 20 20 28");
    private static readonly Glyph Upper36 = One(0x00, 0x04, 0x0C, "32 32 20 20 20 20 32 32");
    private static readonly Glyph Upper6C = Two(0x00, 0x04, 0x04, 0x09, 0x0C, "2C 2C 20 20 24 24", "21 21");
    private static readonly Glyph Upper64 = Two(0x00, 0x04, 0x05, 0x09, 0x0B, "34 34 20 20 20 34 34", "21 21");

    public static void Append(List<byte> body, int x, int y, int font, string text)
    {
        foreach (var piece in Expand(x, y, font, text))
        {
            body.Add(0xD2);
            body.Add((byte)piece.X);
            body.Add(0xD3);
            body.Add((byte)piece.Y);
            body.Add(0xD4);
            body.Add((byte)piece.Font);
            body.AddRange(piece.Data);
        }
    }

    private static List<Piece> Expand(int x, int y, int font, string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormC);
        if (!normalized.Contains('ü') && !normalized.Contains('Ü'))
        {
            return [new Piece(x, y, font, Encode(normalized))];
        }

        var pieces = new List<Piece>();
        var buffer = new StringBuilder();
        var runX = x;
        var cursor = x;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var run = buffer.ToString();
            pieces.Add(new Piece(runX, y, font, Encode(run)));
            cursor = runX + EstimateWidth(run, font);
            buffer.Clear();
        }

        foreach (var ch in normalized)
        {
            if (ch is not ('ü' or 'Ü'))
            {
                if (buffer.Length == 0)
                {
                    runX = cursor;
                }

                buffer.Append(ch);
                continue;
            }

            Flush();
            var glyph = ch == 'ü' ? Lower(font) : Upper(font);
            if (glyph is null)
            {
                var fallback = ch == 'ü' ? "ue" : "Ue";
                pieces.Add(new Piece(cursor, y, font, Encode(fallback)));
                cursor += EstimateWidth(fallback, font);
            }
            else
            {
                var dy = y - glyph.LetterY;
                for (var i = 0; i < glyph.Data.Length; i++)
                {
                    pieces.Add(new Piece(
                        cursor + glyph.OverlaysX[i][0],
                        glyph.OverlaysY[i][0] + dy,
                        BitmapFont,
                        glyph.Data[i]));
                }

                var basis = ch == 'ü' ? "u" : "U";
                pieces.Add(new Piece(cursor, y, font, Encode(basis)));
                cursor += EstimateWidth(basis, font);
            }

            runX = cursor;
        }

        Flush();
        return pieces;
    }

    private static Glyph? Lower(int font) => (font & 0xFF) switch
    {
        0x69 => Lower69,
        0x36 or 0x37 => Lower36,
        0x6C => Lower6C,
        0x64 => Lower64,
        0x65 => Lower65,
        _ => null
    };

    private static Glyph? Upper(int font) => (font & 0xFF) switch
    {
        0x69 => Lower69,
        0x36 or 0x37 => Upper36,
        0x6C => Upper6C,
        0x64 => Upper64,
        0x65 => Lower65,
        _ => null
    };

    private static byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case 'ö': bytes.Add(0x7C); break;
                case 'ä': bytes.Add(0x7B); break;
                case 'Ö': bytes.Add(0x5C); break;
                case 'Ä': bytes.Add(0x5B); break;
                case 'ü': bytes.Add((byte)'u'); bytes.Add((byte)'e'); break;
                case 'Ü': bytes.Add((byte)'U'); bytes.Add((byte)'e'); break;
                case 'ß': bytes.Add((byte)'s'); bytes.Add((byte)'s'); break;
                default:
                    if (ch is >= ' ' and <= '~')
                    {
                        bytes.Add((byte)ch);
                    }
                    break;
            }
        }

        return bytes.ToArray();
    }

    public static int EstimateWidth(string text, int font)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var nominal = (font & 0xFF) switch
        {
            0x69 => 13,
            0x6C => 10,
            0x64 => 7,
            0x77 or 0x78 => 0,
            0x36 => 11,
            _ => 8
        };
        var step = Math.Max(4, nominal * 3 / 4);
        var sum = 0.0;
        foreach (var ch in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sum += step * Factor(ch);
        }

        return Math.Max(0, (int)sum);
    }

    private static double Factor(char ch) => ch switch
    {
        'i' or 'l' or 'I' or 'j' or 't' or 'f' or 'r' or '.' or ',' or ':' or ';' or '!' or '\'' or '`' or '|' or ' ' => 0.45,
        '1' => 0.55,
        'ü' or 'Ü' or 'ä' or 'Ä' or 'ö' or 'Ö' => 1.0,
        'm' or 'w' or 'M' or 'W' => 1.15,
        _ => 1.0
    };

    private static Glyph One(byte x, byte y, int letterY, string hex)
    {
        var data = Hex(hex);
        return new Glyph([[x]], [[y]], [data], letterY);
    }

    private static Glyph Two(byte x1, byte y1, byte x2, byte y2, int letterY, string first, string second) =>
        new([[x1], [x2]], [[y1], [y2]], [Hex(first), Hex(second)], letterY);

    private static byte[] Hex(string hex) =>
        hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => Convert.ToByte(part, 16)).ToArray();

    private readonly record struct Piece(int X, int Y, int Font, byte[] Data);
}

/// <summary>TFT-SWS-Textblock: Großbuchstaben und Cp850-Shift, ohne Kleinbuchstaben-Umschaltung.</summary>
public static class TftSwsText
{
    private static readonly Encoding Cp850 = CreateCp850();

    public static byte[] Encode(string value)
    {
        var text = Sanitize(value);
        var bytes = new List<byte>(text.Length);
        foreach (var ch in text)
        {
            var upper = ch is >= 'a' and <= 'z' ? char.ToUpperInvariant(ch) : ch;
            var target = EncodedByte(upper);
            if (target is >= 0x20 and <= 0x7E)
            {
                bytes.Add((byte)target);
            }
            else if (target is >= 0x80 and <= 0xBF)
            {
                bytes.Add(0x01);
                bytes.Add((byte)(target - 0x60));
            }
            else if (target is >= 0xC0 and <= 0xFF)
            {
                bytes.Add(0x02);
                bytes.Add((byte)(target - 0xA0));
            }
            else
            {
                bytes.Add((byte)' ');
            }
        }

        return bytes.ToArray();
    }

    private static int EncodedByte(char ch)
    {
        if (ch is >= ' ' and <= '~')
        {
            return ch;
        }

        var encoded = Cp850.GetBytes(ch.ToString());
        if (encoded.Length > 0)
        {
            return encoded[0];
        }

        var normalized = ch.ToString().Normalize(NormalizationForm.FormD);
        var stripped = new StringBuilder();
        foreach (var item in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(item) != UnicodeCategory.NonSpacingMark)
            {
                stripped.Append(item);
            }
        }

        var fallback = stripped.ToString().Replace("ß", "ss", StringComparison.Ordinal);
        var code = fallback.Length == 0 ? ' ' : fallback[0];
        return code is >= ' ' and <= '~' ? code : ' ';
    }

    private static string Sanitize(string value)
    {
        var compact = System.Text.RegularExpressions.Regex.Replace(
            value.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' '),
            @"\s+",
            " ").Trim();
        if (compact.Length == 0)
        {
            return " ";
        }

        var builder = new StringBuilder(compact.Length);
        foreach (var ch in compact)
        {
            builder.Append(ch switch
            {
                '–' or '—' or '>' or '→' or '›' or '»' or '«' => '-',
                '„' or '“' or '”' => '"',
                '‚' or '’' or '‘' => '\'',
                _ => ch
            });
        }

        return builder.ToString();
    }

    private static Encoding CreateCp850()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(850);
    }
}
