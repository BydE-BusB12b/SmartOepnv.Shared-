namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Eine ZEdit-Schrift aus Zstndrd.bib. Bit 0 einer Spalte ist die oberste Pixelreihe.
/// Ab Schrift-Höhe 8 liegen die Spalten als 16-Bit-Werte vor, darunter als ein Byte.
/// </summary>
public sealed class MobitecSignFont
{
    public const string ResourceName = "SmartOepnv.Core.Assets.Zstndrd.bib";

    private readonly Dictionary<int, Glyph> _glyphs;
    private readonly MatchGlyph[] _matches;

    private MobitecSignFont(char id, string label, int catalogWidth, int catalogHeight, Dictionary<int, Glyph> glyphs)
    {
        Id = id;
        WireCode = (byte)id;
        Label = label;
        CatalogWidth = catalogWidth;
        CatalogHeight = catalogHeight;
        _glyphs = glyphs;
        BaselineBit = ComputeBaselineBit(glyphs);
        _matches = BuildMatches(glyphs);
    }

    /// <summary>Schriften, die auf den Mobitec-Anzeigen tatsächlich verwendet werden: i 13×9, d 7×8, l 10×7.</summary>
    public static readonly char[] FavoriteIds = ['i', 'd', 'l'];

    public char Id { get; }

    /// <summary>Font-Byte im Mobitec-Frame, z. B. „i“ = 0x69.</summary>
    public byte WireCode { get; }

    public string Label { get; }

    public bool IsFavorite => Id is 'i' or 'd' or 'l';

    /// <summary>Anzeige in der Schriftliste. Favoriten stehen mit Stern ganz oben.</summary>
    public string ChoiceLabel => IsFavorite ? $"★ {Label}" : Label;

    public override string ToString() => Label;

    public int CatalogWidth { get; }

    public int CatalogHeight { get; }

    /// <summary>
    /// Bitmap-Zeile, die auf der Mobitec-Y-Koordinate liegt.
    /// Schrift i: Bit 12, damit Y=0x0C oben bündig ist und Y=0x0F drei Zeilen für Umlaute lässt.
    /// Schrift d: die Zellhöhe 8, damit Y=0x08 oben und Y=0x11 unten bündig ist.
    /// Schrift l: die Zellhöhe 9, damit Y=0x0C in der Mitte der 16 Zeilen steht.
    /// </summary>
    private int BaselineBit { get; }

    private static IReadOnlyList<MobitecSignFont>? _enabled;

    public static IReadOnlyList<MobitecSignFont> LoadEnabled() => _enabled ??= LoadEnabledCore();

    private static IReadOnlyList<MobitecSignFont> LoadEnabledCore()
    {
        var data = ReadResource();
        var starts = FindFontStarts(data);
        var byId = new Dictionary<char, (int Start, int End)>();
        for (var i = 0; i < starts.Count; i++)
        {
            if (byId.ContainsKey(starts[i].Id))
            {
                continue;
            }

            var end = i + 1 < starts.Count ? starts[i + 1].Offset : data.Length;
            byId[starts[i].Id] = (starts[i].Offset, end);
        }

        var fonts = new List<MobitecSignFont>(Catalog.Length);
        foreach (var face in Catalog)
        {
            if (!byId.TryGetValue(face.Id, out var span))
            {
                continue;
            }

            var glyphs = ParseGlyphs(data, span.Start, span.End);
            if (glyphs.Count == 0)
            {
                continue;
            }

            fonts.Add(new MobitecSignFont(face.Id, SizeLabel(face), face.Width, face.Height, glyphs));
        }

        return fonts;
    }

    /// <summary>Grundlinien-Y im Mobitec-Frame für einen Text, dessen Oberkante auf <paramref name="top"/> liegt.</summary>
    public int BaselineForTop(int top) => top + BaselineBit;

    /// <summary>Zeichenbytes, wie sie ZEdit hinter die Schriftart schreibt.</summary>
    public byte[] ToWireBytes(string text) => Encode(text);

    /// <summary>Text mit der linken oberen Ecke bei <paramref name="top"/>.</summary>
    public int DrawTop(bool[,] grid, string text, int x, int top) =>
        Draw(grid, Encode(text), x, top, baseline: false);

    /// <summary>
    /// Längste Textzeile, die pixelgenau in dieser Schrift im Bild liegt.
    /// Die Oberkante ist dieselbe wie bei <see cref="DrawTop"/>.
    /// </summary>
    public bool TryBestRun(bool[,] pixels, int width, out int x, out int top, out string text)
    {
        x = 0;
        top = 0;
        text = string.Empty;
        if (_matches.Length == 0 || width <= 0)
        {
            return false;
        }

        var rows = pixels.GetLength(0);
        var columns = PackColumns(pixels, width);
        var bestInk = 0;
        var bestLength = int.MaxValue;
        for (var origin = 0; origin < rows; origin++)
        {
            var score = ScoreOrigin(columns, rows, width, origin);
            for (var column = 0; column < width; column++)
            {
                foreach (var glyph in _matches)
                {
                    if (glyph.Code == 32 || column + glyph.Width > width || !Fits(columns, rows, origin, column, glyph))
                    {
                        continue;
                    }

                    var next = column + glyph.Advance;
                    var rest = next <= width ? score[next].Text : string.Empty;
                    var candidate = LongestExactPrefix(
                        pixels,
                        width,
                        string.Concat(GlyphChar(glyph.Code - 32), rest),
                        column,
                        origin);

                    if (candidate.Length == 0)
                    {
                        continue;
                    }

                    var length = candidate.Length;
                    var ink = CountRunInk(pixels, width, candidate, column, origin);
                    if (ink <= 0 || ink < bestInk || (ink == bestInk && length >= bestLength))
                    {
                        continue;
                    }

                    // Ein Symbol enthält oft ein paar Spalten, die einem Buchstaben gleichen.
                    // Schrift steht frei: neben der Zeile ist eine leere Spalte, im Kasten kein Fremdpixel.
                    if (!IsIsolated(pixels, width, candidate, column, origin))
                    {
                        continue;
                    }

                    // Umlautpunkte sind nur ein bis zwei Zeilen. Sonst werden sie als „i“ gelesen
                    // und der Buchstabe darunter zerfällt, zum Beispiel Düsseldorf zu „D“ und „sseIdorf“.
                    if (InkRowSpan(pixels, width, candidate, column, origin) < 3)
                    {
                        continue;
                    }

                    bestInk = ink;
                    bestLength = length;
                    x = column;
                    top = origin;
                    text = candidate;
                }
            }
        }

        return bestInk > 0;
    }

    /// <summary>
    /// Mobitec-Bytes. <paramref name="y"/> ist die Grundlinie (0 = oberste Reihe), nicht die Oberkante.
    /// 0x11 bleibt 17: die untere Zeile einer 16-Zeilen-Anzeige sitzt damit auf der letzten Reihe.
    /// </summary>
    public int DrawBaseline(bool[,] grid, ReadOnlySpan<byte> codes, int x, int y) =>
        Draw(grid, codes, x, y, baseline: true);

    public int MeasureWidth(string text)
    {
        var width = 0;
        foreach (var code in Encode(text))
        {
            if (!TryDisplayGlyph(code, out var glyph, out _))
            {
                continue;
            }

            width += glyph.Width + (code == 32 ? 0 : 1);
        }

        return width;
    }

    public int MeasureHeight(string text)
    {
        var codes = Encode(text);
        var height = 0;
        foreach (var code in codes)
        {
            if (TryDisplayGlyph(code, out var glyph, out _))
            {
                height = Math.Max(height, glyph.Height);
            }
        }

        var head = UmlautHeadroom(codes);
        if (height + head > MobitecBitmapFrame.Height)
        {
            head = Math.Max(0, MobitecBitmapFrame.Height - height);
        }

        return height + head;
    }

    /// <summary>
    /// Liest Text aus einem Anzeigenbild, das mit dieser Schrift gezeichnet wurde.
    /// Zusatzpixel (von Hand gesetzte Umlautpunkte) bleiben am Buchstaben darunter hängen.
    /// </summary>
    public string ReadBitmapText(bool[,] pixels) =>
        TryReadBitmapText(pixels, out var text, out _) ? text : string.Empty;

    /// <summary>
    /// Liest den Text nur, wenn die Schrift den größten Teil der Pixel erklärt.
    /// <paramref name="coverage"/> ist der Anteil der Bildpunkte, die zu einem Buchstaben gehören.
    /// </summary>
    public bool TryReadBitmapText(bool[,] pixels, out string text, out double coverage)
    {
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        var lines = new List<string>();
        var covered = 0;
        var ink = 0;
        foreach (var (top, bottom) in TextBands(pixels, rows, cols))
        {
            var line = ReadBand(pixels, cols, top, bottom);
            covered += line.Covered;
            ink += line.Ink;
            if (line.Text.Length > 0)
            {
                lines.Add(line.Text);
            }
        }

        text = string.Join('\n', lines);
        coverage = ink == 0 ? 0 : covered / (double)ink;
        var letters = 0;
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                letters++;
            }
        }

        return letters >= 4 && coverage >= 0.8;
    }

    private (string Text, int Covered, int Ink) ReadBand(bool[,] pixels, int cols, int top, int bottom)
    {
        var chars = new List<char>();
        var covered = 0;
        var ink = 0;
        for (var col = 0; col < cols; col++)
        {
            for (var row = top; row <= bottom; row++)
            {
                if (pixels[row, col])
                {
                    ink++;
                }
            }
        }

        var x = 0;
        while (x < cols)
        {
            if (!ColumnHasInk(pixels, x, top, bottom))
            {
                var gap = 0;
                while (x < cols && !ColumnHasInk(pixels, x, top, bottom))
                {
                    gap++;
                    x++;
                }

                if (chars.Count > 0 && x < cols && gap >= 2)
                {
                    chars.Add(' ');
                }

                continue;
            }

            var match = FindGlyph(pixels, cols, x, top, bottom);
            if (match is null)
            {
                x++;
                continue;
            }

            chars.Add(ApplyDrawnUmlaut(pixels, cols, x, top, match.Value));
            covered += match.Value.Pixels;
            x += match.Value.Width + 1;
        }

        return (new string(chars.ToArray()).Trim(), covered, ink);
    }

    private (char Character, int Width, int Origin, int Body, int Pixels)? FindGlyph(bool[,] pixels, int cols, int x, int top, int bottom)
    {
        (char Character, int Width, int Origin, int Body, int Pixels, int Leftover)? best = null;
        foreach (var (index, glyph) in _glyphs)
        {
            if (glyph.Width <= 0 || x + glyph.Width > cols)
            {
                continue;
            }

            var depth = PixelDepth(glyph);
            var glyphPixels = CountBits(glyph);
            if (glyphPixels == 0)
            {
                continue;
            }

            var body = FirstInkBit(glyph);
            for (var origin = top; origin <= bottom; origin++)
            {
                if (!GlyphFits(pixels, glyph, x, origin, depth))
                {
                    continue;
                }

                var leftover = LeftoverInk(pixels, glyph, x, origin, depth, top, bottom);
                var character = GlyphChar(index);
                var better = best is null
                    || glyphPixels > best.Value.Pixels
                    || (glyphPixels == best.Value.Pixels && leftover < best.Value.Leftover)
                    || (glyphPixels == best.Value.Pixels && leftover == best.Value.Leftover
                        && char.IsLower(character) && char.IsUpper(best.Value.Character));
                if (better)
                {
                    best = (character, glyph.Width, origin, body, glyphPixels, leftover);
                }
            }
        }

        return best is null ? null : (best.Value.Character, best.Value.Width, best.Value.Origin, best.Value.Body, best.Value.Pixels);
    }

    /// <summary>Punkte über a, o oder u, die nicht zur Schrift gehören, werden zum Umlaut.</summary>
    private static char ApplyDrawnUmlaut(
        bool[,] pixels,
        int cols,
        int x,
        int top,
        (char Character, int Width, int Origin, int Body, int Pixels) match)
    {
        var ch = match.Character;
        var baseChar = ch switch
        {
            'a' or 'A' or 'ä' or 'Ä' => 'a',
            'o' or 'O' or 'ö' or 'Ö' => 'o',
            'u' or 'U' or 'ü' or 'Ü' => 'u',
            _ => '\0'
        };
        var letterTop = match.Origin + match.Body;
        if (baseChar == '\0' || letterTop <= top)
        {
            return ch;
        }

        var dots = 0;
        var left = Math.Max(0, x - 1);
        var right = Math.Min(cols, x + match.Width + 1);
        for (var row = top; row < letterTop; row++)
        {
            for (var col = left; col < right; col++)
            {
                if (pixels[row, col])
                {
                    dots++;
                }
            }
        }

        if (dots < 2)
        {
            return ch;
        }

        return baseChar switch
        {
            'a' => char.IsUpper(ch) ? 'Ä' : 'ä',
            'o' => char.IsUpper(ch) ? 'Ö' : 'ö',
            _ => char.IsUpper(ch) ? 'Ü' : 'ü'
        };
    }

    private static int LeftoverInk(bool[,] pixels, Glyph glyph, int x, int origin, int depth, int top, int bottom)
    {
        var leftover = 0;
        for (var column = 0; column < glyph.Width; column++)
        {
            var bits = glyph.Columns[column];
            for (var row = top; row <= bottom; row++)
            {
                if (!pixels[row, x + column])
                {
                    continue;
                }

                var bit = row - origin;
                if (bit < 0 || bit >= depth || (bits & (1 << bit)) == 0)
                {
                    leftover++;
                }
            }
        }

        return leftover;
    }

    private static bool GlyphFits(bool[,] pixels, Glyph glyph, int x, int origin, int depth)
    {
        var rows = pixels.GetLength(0);
        for (var column = 0; column < glyph.Width; column++)
        {
            var bits = glyph.Columns[column];
            for (var bit = 0; bit < depth; bit++)
            {
                if ((bits & (1 << bit)) == 0)
                {
                    continue;
                }

                var row = origin + bit;
                if (row < 0 || row >= rows || !pixels[row, x + column])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int FirstInkBit(Glyph glyph)
    {
        var first = PixelDepth(glyph);
        foreach (var column in glyph.Columns)
        {
            for (var bit = 0; bit < first; bit++)
            {
                if ((column & (1 << bit)) != 0)
                {
                    first = bit;
                    break;
                }
            }
        }

        return first;
    }

    private static int CountBits(Glyph glyph)
    {
        var count = 0;
        foreach (var column in glyph.Columns)
        {
            var bits = column;
            while (bits != 0)
            {
                count += bits & 1;
                bits >>= 1;
            }
        }

        return count;
    }

    private static bool ColumnHasInk(bool[,] pixels, int x, int top, int bottom)
    {
        for (var row = top; row <= bottom; row++)
        {
            if (pixels[row, x])
            {
                return true;
            }
        }

        return false;
    }

    private static List<(int Top, int Bottom)> TextBands(bool[,] pixels, int rows, int cols)
    {
        var ink = new bool[rows];
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                if (pixels[row, col])
                {
                    ink[row] = true;
                    break;
                }
            }
        }

        var bands = new List<(int Top, int Bottom)>();
        var start = -1;
        for (var row = 0; row <= rows; row++)
        {
            var on = row < rows && ink[row];
            if (on && start < 0)
            {
                start = row;
            }
            else if (!on && start >= 0)
            {
                bands.Add((start, row - 1));
                start = -1;
            }
        }

        // Zwei Zeilen, die sich über Umlautpunkte berühren, an der dünnsten Reihe trennen.
        if (bands.Count == 1 && bands[0].Bottom - bands[0].Top >= 12)
        {
            var (bandTop, bandBottom) = bands[0];
            var split = bandTop + (bandBottom - bandTop) / 2;
            var thinnest = split;
            var least = int.MaxValue;
            for (var row = bandTop + 4; row <= bandBottom - 4; row++)
            {
                var count = 0;
                for (var col = 0; col < cols; col++)
                {
                    if (pixels[row, col])
                    {
                        count++;
                    }
                }

                if (count < least)
                {
                    least = count;
                    thinnest = row;
                }
            }

            return [(bandTop, thinnest), (thinnest + 1, bandBottom)];
        }

        return bands;
    }

    private static char GlyphChar(int index)
    {
        var code = index + 32;
        return code switch
        {
            91 => 'Ä',
            92 => 'Ö',
            93 => 'Ü',
            123 => 'ä',
            124 => 'ö',
            125 => 'ü',
            >= 32 and <= 126 => (char)code,
            _ => '?'
        };
    }

    private int Draw(bool[,] grid, ReadOnlySpan<byte> codes, int x, int y, bool baseline)
    {
        var rows = grid.GetLength(0);
        var cols = grid.GetLength(1);
        var cursor = x;
        // Im Grafiktool wird ü/Ü als Grundbuchstabe mit Punkten gezeichnet.
        // Importierte Schrift-Bytes bleiben unverändert.
        var head = baseline ? 0 : UmlautHeadroom(codes);
        if (!baseline && head > 0)
        {
            var body = 0;
            foreach (var code in codes)
            {
                if (TryDisplayGlyph(code, out var measured, out _))
                {
                    body = Math.Max(body, measured.Height);
                }
            }

            if (y + body + head > rows)
            {
                head = Math.Max(0, rows - y - body);
            }
        }

        foreach (var code in codes)
        {
            if (code < 32 || !TryGlyph(code, substituteUmlaut: !baseline, out var glyph, out var umlaut))
            {
                continue;
            }

            var top = (baseline ? y - BaselineBit : y) + head;
            var depth = PixelDepth(glyph);
            for (var column = 0; column < glyph.Width; column++)
            {
                var px = cursor + column;
                if (px < 0 || px >= cols)
                {
                    continue;
                }

                var bits = glyph.Columns[column];
                for (var bit = 0; bit < depth; bit++)
                {
                    if ((bits & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    var row = top + bit;
                    if (row >= 0 && row < rows)
                    {
                        grid[row, px] = true;
                    }
                }
            }

            if (umlaut)
            {
                PaintUmlautDots(grid, glyph, cursor, top);
            }

            cursor += glyph.Width + (code == 32 ? 0 : 1);
        }

        return cursor;
    }

    /// <summary>ü und Ü nutzen den Grundbuchstaben. Die Anzeigen haben kein ü.</summary>
    private bool TryDisplayGlyph(byte code, out Glyph glyph, out bool umlaut) =>
        TryGlyph(code, substituteUmlaut: true, out glyph, out umlaut);

    private bool TryGlyph(byte code, bool substituteUmlaut, out Glyph glyph, out bool umlaut)
    {
        umlaut = substituteUmlaut && code is 93 or 125;
        var shown = umlaut ? (code == 93 ? (byte)'U' : (byte)'u') : code;
        if (shown < 32 || !_glyphs.TryGetValue(shown - 32, out glyph))
        {
            glyph = default;
            return false;
        }

        return true;
    }

    /// <summary>Zeilen über dem Grundbuchstaben, damit die Punkte nicht in den Buchstaben fallen.</summary>
    private int UmlautHeadroom(ReadOnlySpan<byte> codes)
    {
        var head = 0;
        foreach (var code in codes)
        {
            if (code is not (93 or 125) || !TryDisplayGlyph(code, out var glyph, out _))
            {
                continue;
            }

            var needed = DotThickness(glyph) + 1;
            head = Math.Max(head, Math.Max(0, needed - FirstInkBit(glyph)));
        }

        return head;
    }

    private static int DotThickness(Glyph glyph) => glyph.Height >= 12 ? 2 : 1;

    /// <summary>Zwei Punkte über dem Buchstaben, mit einer freien Zeile dazwischen, wenn Platz ist.</summary>
    private static void PaintUmlautDots(bool[,] grid, Glyph glyph, int x, int origin)
    {
        var rows = grid.GetLength(0);
        var cols = grid.GetLength(1);
        var body = FirstInkBit(glyph);
        var thickness = DotThickness(glyph);
        var dotRow = origin + body - thickness - 1;
        if (dotRow < 0)
        {
            dotRow = 0;
        }

        var dot = glyph.Width >= 6 ? 2 : 1;
        var inner = Math.Max(1, glyph.Width - 2 * dot);
        var pattern = dot * 2 + inner;
        var offset = x + Math.Max(0, (glyph.Width - pattern) / 2);
        for (var layer = 0; layer < thickness; layer++)
        {
            var row = dotRow + layer;
            if (row < 0 || row >= rows || row >= origin + body)
            {
                continue;
            }

            FillDot(grid, cols, row, offset, dot);
            FillDot(grid, cols, row, offset + dot + inner, dot);
        }
    }

    private static void FillDot(bool[,] grid, int cols, int row, int start, int width)
    {
        for (var i = 0; i < width; i++)
        {
            var col = start + i;
            if (col >= 0 && col < cols)
            {
                grid[row, col] = true;
            }
        }
    }

    /// <summary>
    /// Großbuchstaben mit mindestens zwei freien Zeilen unter dem Schriftbild nutzen deren Unterkante.
    /// Sonst liegt die Grundlinie unter der gemeldeten Zelle, damit oben- und untenbündig die Matrixkante treffen.
    /// </summary>
    private static int ComputeBaselineBit(Dictionary<int, Glyph> glyphs)
    {
        var height = 0;
        var capLast = -1;
        foreach (var (index, glyph) in glyphs)
        {
            height = Math.Max(height, glyph.Height);
            var ch = (char)(index + 32);
            if (ch is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            {
                continue;
            }

            foreach (var column in glyph.Columns)
            {
                for (var bit = 15; bit >= 0; bit--)
                {
                    if ((column & (1 << bit)) != 0)
                    {
                        capLast = Math.Max(capLast, bit);
                        break;
                    }
                }
            }
        }

        if (capLast < 0)
        {
            return Math.Max(0, height - 1);
        }

        var blankBelowCaps = (height - 1) - capLast;
        return blankBelowCaps >= 2 ? capLast : height;
    }

    /// <summary>Zeilen der Spalte. Unterlängen können unter der gemeldeten Höhe liegen.</summary>
    private static int PixelDepth(Glyph glyph)
    {
        var depth = glyph.Height;
        foreach (var column in glyph.Columns)
        {
            for (var bit = depth; bit < 16; bit++)
            {
                if ((column & (1 << bit)) != 0)
                {
                    depth = bit + 1;
                }
            }
        }

        return depth;
    }

    private readonly record struct RunScore(int Ink, string Text);

    private readonly record struct MatchGlyph(byte Code, int Width, int Advance, int Ink, int Depth, int FirstInk, int[] Columns);

    private RunScore[] ScoreOrigin(ushort[] columns, int rows, int width, int origin)
    {
        var score = new RunScore[width + 1];
        for (var column = width - 1; column >= 0; column--)
        {
            var ink = 0;
            var text = string.Empty;
            foreach (var glyph in _matches)
            {
                if (column + glyph.Width > width || !Fits(columns, rows, origin, column, glyph))
                {
                    continue;
                }

                var next = column + glyph.Advance;
                var restInk = next <= width ? score[next].Ink : 0;
                var rest = next <= width ? score[next].Text : string.Empty;
                if (glyph.Code == 32 && restInk == 0)
                {
                    continue;
                }

                var total = glyph.Ink + restInk;
                var combined = string.Concat(GlyphChar(glyph.Code - 32), rest);
                if (total > ink || (total == ink && combined.Length > text.Length))
                {
                    ink = total;
                    text = combined;
                }
            }

            score[column] = new RunScore(ink, text);
        }

        return score;
    }

    private string LongestExactPrefix(bool[,] pixels, int width, string candidate, int column, int origin)
    {
        if (candidate.Length == 0 || PaintsExactly(pixels, width, candidate, column, origin))
        {
            return candidate;
        }

        // Ein längerer Text malt nur zusätzliche Punkte. Schlägt er fehl, bleibt der
        // längste kürzere Anfang, solange das erste Zeichen ein Buchstabe oder eine Ziffer ist.
        if (char.IsLetterOrDigit(candidate[0]))
        {
            var low = 1;
            var high = candidate.Length - 1;
            var best = 0;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                if (PaintsExactly(pixels, width, candidate[..mid], column, origin))
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return best == 0 ? string.Empty : candidate[..best];
        }

        while (candidate.Length > 0 && !PaintsExactly(pixels, width, candidate, column, origin))
        {
            candidate = candidate[..^1];
        }

        return candidate;
    }

    private bool PaintsExactly(bool[,] pixels, int width, string text, int x, int top)
    {
        if (text.Length == 0 || !text.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        var rows = pixels.GetLength(0);
        var mask = new bool[rows, width];
        DrawTop(mask, text, x, top);
        var painted = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (!mask[row, column])
                {
                    continue;
                }

                if (!pixels[row, column])
                {
                    return false;
                }

                painted++;
            }
        }

        return painted > 0;
    }

    /// <summary>Zeilen mit Farbe im gezeichneten Text. Umlautpunkte allein sind kürzer als ein Buchstabe.</summary>
    private int InkRowSpan(bool[,] pixels, int width, string text, int x, int top)
    {
        var rows = pixels.GetLength(0);
        var mask = new bool[rows, width];
        DrawTop(mask, text, x, top);
        var min = rows;
        var max = -1;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (!mask[row, column])
                {
                    continue;
                }

                if (row < min)
                {
                    min = row;
                }

                if (row > max)
                {
                    max = row;
                }
            }
        }

        return max < 0 ? 0 : max - min + 1;
    }

    private int CountRunInk(bool[,] pixels, int width, string text, int x, int top)
    {
        var rows = pixels.GetLength(0);
        var mask = new bool[rows, width];
        DrawTop(mask, text, x, top);
        var painted = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (mask[row, column] && pixels[row, column])
                {
                    painted++;
                }
            }
        }

        return painted;
    }

    /// <summary>
    /// Die Zeile erklärt ihren Kasten vollständig und grenzt links und rechts an eine leere Spalte.
    /// Sonst sind die gleichen Pixel Teil einer Grafik.
    /// </summary>
    private bool IsIsolated(bool[,] pixels, int width, string text, int x, int top)
    {
        var rows = pixels.GetLength(0);
        var mask = new bool[rows, width];
        var end = DrawTop(mask, text, x, top);
        if (end <= x)
        {
            return false;
        }

        var minRow = rows;
        var maxRow = -1;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (!mask[row, column])
                {
                    continue;
                }

                if (row < minRow)
                {
                    minRow = row;
                }

                if (row > maxRow)
                {
                    maxRow = row;
                }
            }
        }

        if (maxRow < 0)
        {
            return false;
        }

        var last = Math.Min(end, width);
        for (var row = minRow; row <= maxRow; row++)
        {
            for (var column = x; column < last; column++)
            {
                if (pixels[row, column] != mask[row, column])
                {
                    return false;
                }
            }
        }

        if (x > 0 && BandHasInk(pixels, x - 1, minRow, maxRow))
        {
            return false;
        }

        if (end < width && BandHasInk(pixels, end, minRow, maxRow))
        {
            return false;
        }

        return true;
    }

    private static bool BandHasInk(bool[,] pixels, int column, int top, int bottom)
    {
        for (var row = top; row <= bottom; row++)
        {
            if (pixels[row, column])
            {
                return true;
            }
        }

        return false;
    }

    private static int FirstInkBit(MatchGlyph glyph)
    {
        var first = glyph.Depth;
        foreach (var column in glyph.Columns)
        {
            for (var bit = 0; bit < glyph.Depth; bit++)
            {
                if ((column & (1 << bit)) != 0)
                {
                    first = Math.Min(first, bit);
                    break;
                }
            }
        }

        return first == glyph.Depth ? 0 : first;
    }

    /// <summary>Bit 0 einer Spalte ist die oberste Zeile. Nur die 16 Zeilen einer Anzeige.</summary>
    private static ushort[] PackColumns(bool[,] pixels, int width)
    {
        var rows = Math.Min(pixels.GetLength(0), 16);
        var packed = new ushort[width];
        for (var column = 0; column < width; column++)
        {
            ushort bits = 0;
            for (var row = 0; row < rows; row++)
            {
                if (pixels[row, column])
                {
                    bits |= (ushort)(1 << row);
                }
            }

            packed[column] = bits;
        }

        return packed;
    }

    private static bool Fits(ushort[] columns, int rows, int origin, int x, MatchGlyph glyph)
    {
        if (rows > 16)
        {
            return false;
        }

        var depth = glyph.Depth;
        var mask = depth >= 31 ? uint.MaxValue : (1u << depth) - 1u;
        var allowedExtra = glyph.FirstInk <= 0 ? 0u : (1u << glyph.FirstInk) - 1u;
        var glyphColumns = glyph.Columns;
        for (var column = 0; column < glyph.Width; column++)
        {
            var actual = ((uint)columns[x + column] >> origin) & mask;
            var expected = (uint)glyphColumns[column] & mask;
            if ((expected & ~actual) != 0)
            {
                return false;
            }

            // Punkte über dem Grundbuchstaben (ü) liegen in den leeren Zeilen der Zelle.
            if (((actual & ~expected) & ~allowedExtra) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static MatchGlyph[] BuildMatches(Dictionary<int, Glyph> glyphs)
    {
        var list = new List<MatchGlyph>();
        foreach (var (index, glyph) in glyphs.OrderBy(pair => pair.Key))
        {
            var code = index + 32;
            if (code is < 32 or > 126 || glyph.Width <= 0)
            {
                continue;
            }

            var depth = PixelDepth(glyph);
            var ink = 0;
            foreach (var column in glyph.Columns)
            {
                for (var bit = 0; bit < depth; bit++)
                {
                    if ((column & (1 << bit)) != 0)
                    {
                        ink++;
                    }
                }
            }

            if (ink == 0 && code != 32)
            {
                continue;
            }

            var advance = glyph.Width + (code == 32 ? 0 : 1);
            var match = new MatchGlyph((byte)code, glyph.Width, advance, ink, depth, 0, glyph.Columns);
            list.Add(match with { FirstInk = FirstInkBit(match) });
        }

        return list.ToArray();
    }

    private static byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case 'Ä': bytes.Add(91); break;
                case 'Ö': bytes.Add(92); break;
                case 'Ü': bytes.Add(93); break;
                case 'ä': bytes.Add(123); break;
                case 'ö': bytes.Add(124); break;
                case 'ü': bytes.Add(125); break;
                case 'ß':
                    bytes.Add((byte)'s');
                    bytes.Add((byte)'s');
                    break;
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

    private static Dictionary<int, Glyph> ParseGlyphs(byte[] data, int start, int end)
    {
        var glyphs = new Dictionary<int, Glyph>();
        if (end - start < 65 + 96 * 2)
        {
            return glyphs;
        }

        var offsets = new int[96];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = data[start + 65 + i * 2] | (data[start + 65 + i * 2 + 1] << 8);
        }

        var blockLength = end - start;
        for (var index = 0; index < offsets.Length; index++)
        {
            var offset = offsets[index];
            if (offset <= 0 || offset >= blockLength)
            {
                continue;
            }

            var next = blockLength;
            for (var j = index + 1; j < offsets.Length; j++)
            {
                if (offsets[j] > offset)
                {
                    next = offsets[j];
                    break;
                }
            }

            if (next > blockLength || next - offset < 2)
            {
                continue;
            }

            var height = data[start + offset];
            var width = data[start + offset + 1];
            if (height is 0 or > 24 || width is 0 or > 32)
            {
                continue;
            }

            var stride = height >= 8 ? 2 : 1;
            if (2 + width * stride > next - offset)
            {
                continue;
            }

            var columns = new int[width];
            var position = start + offset + 2;
            for (var column = 0; column < width; column++)
            {
                var value = (int)data[position];
                if (stride == 2)
                {
                    value |= data[position + 1] << 8;
                }

                columns[column] = value;
                position += stride;
            }

            glyphs[index] = new Glyph(height, width, columns);
        }

        return glyphs;
    }

    private static List<(int Offset, char Id)> FindFontStarts(byte[] data)
    {
        var starts = new List<(int Offset, char Id)>();
        for (var i = 0; i < data.Length - 80; i++)
        {
            var id = (char)data[i];
            if (id is not (>= '1' and <= '9' or >= 'a' and <= 'z'))
            {
                continue;
            }

            var classic = true;
            for (var z = 1; z < 40; z++)
            {
                if (data[i + z] != 0)
                {
                    classic = false;
                    break;
                }
            }

            var newer = data[i + 1] == 0x03 && data[i + 2] == 0x01;
            if (newer)
            {
                for (var z = 3; z < 40; z++)
                {
                    if (data[i + z] != 0)
                    {
                        newer = false;
                        break;
                    }
                }
            }

            if (classic || newer)
            {
                starts.Add((i, id));
            }
        }

        return starts;
    }

    private static byte[] ReadResource()
    {
        using var stream = typeof(MobitecSignFont).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Mobitec-Schriften (Zstndrd.bib) fehlen im Programm.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Alle Schriften aus ZEDIT.LIB, in derselben Reihenfolge. Die Bezeichnung ist die Größe.</summary>
    private static readonly Face[] Catalog =
    [
        new('a', 13, 11),
        new('b', 9, 10),
        new('c', 10, 9),
        new('d', 7, 8),
        new('e', 7, 7),
        new('f', 6, 6),
        new('g', 0, 0),
        new('h', 16, 9),
        new('i', 13, 9),
        new('l', 10, 7),
        new('m', 7, 5),
        new('n', 7, 3),
        new('o', 14, 6),
        new('q', 15, 8),
        new('r', 5, 5),
        new('s', 13, 8),
        new('t', 16, 10),
        new('v', 6, 7),
        new('x', 16, 9),
        new('y', 13, 10),
        new('p', 7, 6),
        new('1', 8, 3),
        new('2', 8, 4),
        new('3', 8, 5),
        new('j', 8, 6),
        new('5', 8, 7),
        new('6', 11, 8),
        new('7', 11, 7)
    ];

    private static string SizeLabel(Face face)
    {
        if (face.Width == 0)
        {
            return "Grafik";
        }

        var size = $"{face.Width}×{face.Height}";
        var shared = 0;
        foreach (var other in Catalog)
        {
            if (other.Width == face.Width && other.Height == face.Height)
            {
                shared++;
            }
        }

        return shared > 1 ? $"{size} ({face.Id})" : size;
    }

    private readonly record struct Face(char Id, int Width, int Height);

    private readonly record struct Glyph(int Height, int Width, int[] Columns);
}
