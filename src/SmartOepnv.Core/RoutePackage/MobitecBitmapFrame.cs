namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Pixelbild einer Mobitec-Anzeige als A2-Frame.
/// Eine 16-Pixel-Anzeige steht im Protokoll auf den Y-Werten 4, 9, 14 und 19.
/// Jedes Bitmap-Byte trägt fünf Pixel in Bit 0–4 (Bit 0 oben) und setzt immer Bit 5.
/// </summary>
public static class MobitecBitmapFrame
{
    public const int Height = 16;
    public const int FrontWidth = 128;
    public const int SideWidth = 120;
    public const int LineWidth = 28;
    public const byte FrontAddress = 0x06;
    public const byte SideAddress = 0x07;
    public const byte LineAddress = 0x0B;

    /// <summary>Eine Seite. Mehrere Seiten sind ein Wechseltext (A2 F1, getrennt durch B0).</summary>
    public readonly record struct DecodedSign(IReadOnlyList<bool[,]> Pages, int IntervalSeconds);

    public static byte[]? Encode(bool[,] pixels, byte address, int width) =>
        EncodePages([pixels], address, width, intervalSeconds: 3);

    public static byte[]? EncodePages(
        IReadOnlyList<bool[,]> pages,
        byte address,
        int width,
        int intervalSeconds)
    {
        if (pages.Count == 0 || !pages.Any(page => HasPixel(page, width)))
        {
            return null;
        }

        var sequence = pages.Count > 1;
        var body = new List<byte>
        {
            0xFF,
            address,
            0xA2,
            sequence ? (byte)0xF1 : (byte)0xF0,
            0xD0,
            (byte)width,
            0xD1,
            0x10
        };
        var takt = (byte)Math.Clamp(intervalSeconds, 1, 99);
        for (var page = 0; page < pages.Count; page++)
        {
            AppendBands(body, pages[page], width);
            if (sequence && page < pages.Count - 1)
            {
                body.Add(0xB0);
                body.Add(takt);
            }
        }

        AppendChecksum(body);
        body.Add(0xFF);
        return body.ToArray();
    }

    /// <summary>
    /// Buchstaben als Schrift-Objekt, nur übrige Punkte als Bitmap.
    /// Gilt für Front, Seite und Linie, seitenweise: eine Textseite bleibt Text,
    /// eine gezeichnete Folgeseite bleibt Bitmap.
    /// Die Linienanzeige und die linken 28 Punkte der Front bleiben ein Bild,
    /// sobald die Punkte nicht vollständig eine passende Schrift sind.
    /// </summary>
    public static byte[]? EncodePreferringText(
        IReadOnlyList<bool[,]> pages,
        byte address,
        int width,
        int intervalSeconds,
        IReadOnlyList<MobitecSignFont> fonts)
    {
        if (fonts.Count == 0)
        {
            return EncodePages(pages, address, width, intervalSeconds);
        }

        if (pages.Count == 0 || !pages.Any(page => HasPixel(page, width)))
        {
            return null;
        }

        var sequence = pages.Count > 1;
        var body = new List<byte>
        {
            0xFF,
            address,
            0xA2,
            sequence ? (byte)0xF1 : (byte)0xF0,
            0xD0,
            (byte)width,
            0xD1,
            0x10
        };
        var takt = (byte)Math.Clamp(intervalSeconds, 1, 99);
        for (var page = 0; page < pages.Count; page++)
        {
            var work = Clone(pages[page]);
            var runs = new List<TextObject>();
            while (runs.Count < 12 && TryTakeRun(work, width, fonts, out var run))
            {
                runs.Add(run);
            }

            // Eine gezeichnete Linie, die nicht vollständig in einer Schrift steht,
            // bleibt das Pixelbild. Sonst setzt die Anzeige die Schrift und die
            // 28 Punkte reichen nicht mehr, zum Beispiel bei einem gezeichneten RE47X.
            // Das gilt nur für die Linienanzeige. Die Seite ist 120 Punkte breit;
            // Text, der dort links beginnt, darf nicht auf 28 Punkte beschnitten werden.
            if (width == LineWidth && HasPixel(work, width))
            {
                runs.Clear();
                work = Clone(pages[page]);
            }
            else if (address == FrontAddress && HasPixel(work, LineWidth))
            {
                runs.RemoveAll(item => item.X < LineWidth);
                if (runs.Count == 0)
                {
                    var right = Clone(pages[page]);
                    ClearColumns(right, LineWidth);
                    while (runs.Count < 12 && TryTakeRun(right, width, fonts, out var run))
                    {
                        runs.Add(run);
                    }

                    work = right;
                }

                CopyColumns(pages[page], work, LineWidth);
            }

            foreach (var run in runs.OrderBy(item => item.Baseline).ThenBy(item => item.X))
            {
                body.Add(0xD2);
                body.Add((byte)run.X);
                body.Add(0xD3);
                body.Add((byte)run.Baseline);
                body.Add(0xD4);
                body.Add(run.Font);
                body.AddRange(run.Wire);
            }

            if (HasPixel(work, width))
            {
                AppendBands(body, work, width);
            }

            if (sequence && page < pages.Count - 1)
            {
                body.Add(0xB0);
                body.Add(takt);
            }
        }

        AppendChecksum(body);
        body.Add(0xFF);
        return body.ToArray();
    }

    public static bool ContainsBitmap(byte[]? frame) =>
        FontCodes(frame).Any(code => code == 0x77);

    public static bool ContainsText(byte[]? frame) =>
        FontCodes(frame).Any(code => code is not (0x77 or 0x78));

    private readonly record struct TextObject(int X, int Baseline, byte Font, byte[] Wire);

    private readonly record struct FontRun(MobitecSignFont Font, int X, int Top, string Text, int Ink);

    private static bool TryTakeRun(
        bool[,] work,
        int width,
        IReadOnlyList<MobitecSignFont> fonts,
        out TextObject run)
    {
        run = default;
        var hits = new FontRun?[fonts.Count];
        Parallel.For(0, fonts.Count, index =>
        {
            var font = fonts[index];
            if (!font.TryBestRun(work, width, out var x, out var top, out var text))
            {
                return;
            }

            var ink = CountMask(work, width, font, text, x, top);
            if (ink <= 0 || font.BaselineForTop(top) is < 0 or > 255)
            {
                return;
            }

            hits[index] = new FontRun(font, x, top, text, ink);
        });

        MobitecSignFont? chosenFont = null;
        var chosenX = 0;
        var chosenTop = 0;
        var chosenText = string.Empty;
        var chosenInk = 0;
        var chosenFavorite = false;
        foreach (var hit in hits)
        {
            if (hit is not FontRun found)
            {
                continue;
            }

            if (found.Ink < chosenInk || (found.Ink == chosenInk && (chosenFavorite || !found.Font.IsFavorite)))
            {
                continue;
            }

            chosenInk = found.Ink;
            chosenFavorite = found.Font.IsFavorite;
            chosenFont = found.Font;
            chosenX = found.X;
            chosenTop = found.Top;
            chosenText = found.Text;
        }

        if (chosenFont is null)
        {
            return false;
        }

        var baseline = chosenFont.BaselineForTop(chosenTop);
        ClearMask(work, width, chosenFont, chosenText, chosenX, chosenTop);
        run = new TextObject(chosenX, baseline, chosenFont.WireCode, chosenFont.ToWireBytes(chosenText));
        return run.Wire.Length > 0;
    }

    private static int CountMask(bool[,] work, int width, MobitecSignFont font, string text, int x, int top)
    {
        var rows = work.GetLength(0);
        var mask = new bool[rows, width];
        font.DrawTop(mask, text, x, top);
        var ink = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (!mask[row, column])
                {
                    continue;
                }

                if (!work[row, column])
                {
                    return 0;
                }

                ink++;
            }
        }

        return ink;
    }

    private static void ClearMask(bool[,] work, int width, MobitecSignFont font, string text, int x, int top)
    {
        var rows = work.GetLength(0);
        var mask = new bool[rows, width];
        font.DrawTop(mask, text, x, top);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (mask[row, column])
                {
                    work[row, column] = false;
                }
            }
        }
    }

    private static IEnumerable<byte> FontCodes(byte[]? frame)
    {
        if (frame is not { Length: > 8 } || !TryPayload(frame, out var payload))
        {
            yield break;
        }

        for (var i = 0; i < payload.Length;)
        {
            if (IsObject(payload, i))
            {
                yield return payload[i + 5];
                i = ObjectEnd(payload, i);
                continue;
            }

            i++;
        }
    }

    /// <summary>Text, Grafik oder beides, eine Angabe je Wechsel-Seite.</summary>
    public static IReadOnlyList<string> PageKinds(byte[]? frame)
    {
        if (frame is null || frame.Length < 8 || frame[0] != 0xFF || !TryPayload(frame, out var payload))
        {
            return [];
        }

        var kinds = new List<string>();
        var start = 0;
        for (var i = 0; i + 1 < payload.Length; i++)
        {
            if (payload[i] != 0xB0 || payload[i + 1] is < 1 or > 99)
            {
                continue;
            }

            kinds.Add(KindOf(payload, start, i));
            start = i + 2;
            i++;
        }

        kinds.Add(KindOf(payload, start, payload.Length));
        return kinds;
    }

    private static string KindOf(byte[] payload, int start, int end)
    {
        var text = false;
        var bitmap = false;
        for (var i = start; i + 1 < end; i++)
        {
            if (payload[i] != 0xD4)
            {
                continue;
            }

            var font = payload[i + 1];
            if (font == 0x77)
            {
                bitmap = true;
            }
            else if (font != 0x78)
            {
                text = true;
            }
        }

        if (text && bitmap)
        {
            return "Text+Grafik";
        }

        return bitmap ? "Grafik" : "Text";
    }

    /// <summary>Nur die erste Seite, als einzelnes Bild.</summary>
    public static byte[] FirstPage(byte[] frame)
    {
        if (frame.Length < 8 || frame[0] != 0xFF || frame[2] != 0xA2 || frame[3] != 0xF1)
        {
            return (byte[])frame.Clone();
        }

        if (!TryPayload(frame, out var payload))
        {
            return (byte[])frame.Clone();
        }

        var cut = payload.Length;
        for (var i = 0; i + 1 < payload.Length; i++)
        {
            if (payload[i] == 0xB0 && payload[i + 1] is >= 1 and <= 99)
            {
                cut = i;
                break;
            }
        }

        var single = new List<byte> { frame[0], frame[1], frame[2], 0xF0 };
        single.AddRange(payload.Take(cut));
        AppendChecksum(single);
        single.Add(0xFF);
        return single.ToArray();
    }

    /// <summary>Ersetzt nur die B0-Sekunden. Die Objekte bleiben die Originalbytes.</summary>
    public static byte[] WithInterval(byte[] frame, int seconds)
    {
        if (frame.Length < 4 || !TryPayload(frame, out var payload))
        {
            return (byte[])frame.Clone();
        }

        var takt = (byte)Math.Clamp(seconds, 1, 99);
        var changed = false;
        var i = 0;
        while (i < payload.Length)
        {
            if (payload[i] == 0xB0 && i + 1 < payload.Length)
            {
                payload[i + 1] = takt;
                changed = true;
                i += 2;
                continue;
            }

            if (IsObject(payload, i))
            {
                i = ObjectEnd(payload, i);
                continue;
            }

            i++;
        }

        if (!changed)
        {
            return (byte[])frame.Clone();
        }

        var body = new List<byte> { frame[0], frame[1], frame[2], frame[3] };
        body.AddRange(payload);
        AppendChecksum(body);
        body.Add(0xFF);
        return body.ToArray();
    }

    public static bool[,] Decode(byte[]? frame, int width, IReadOnlyList<MobitecSignFont> fonts) =>
        DecodeSign(frame, width, fonts).Pages[0];

    public static DecodedSign DecodeSign(byte[]? frame, int width, IReadOnlyList<MobitecSignFont> fonts)
    {
        var pages = new List<bool[,]> { new bool[Height, width] };
        var interval = 3;
        if (frame is not { Length: > 8 } || !TryPayload(frame, out var payload))
        {
            return new DecodedSign(pages, interval);
        }

        var fontByCode = fonts.ToDictionary(font => (int)font.WireCode);
        var drawn = false;
        var i = 0;
        while (i < payload.Length)
        {
            if (payload[i] == 0xB0 && i + 1 < payload.Length)
            {
                var seconds = payload[i + 1] & 0xFF;
                if (seconds is >= 1 and <= 99)
                {
                    interval = seconds;
                }

                if (drawn)
                {
                    pages.Add(new bool[Height, width]);
                    drawn = false;
                }

                i += 2;
                continue;
            }

            if (!IsObject(payload, i))
            {
                i++;
                continue;
            }

            DrawObject(pages[^1], payload, i, width, fontByCode);
            drawn = true;
            i = ObjectEnd(payload, i);
        }

        if (!drawn && pages.Count > 1)
        {
            pages.RemoveAt(pages.Count - 1);
        }

        return new DecodedSign(pages, interval);
    }

    private static void AppendBands(List<byte> body, bool[,] pixels, int width)
    {
        for (var band = 0; band < 4; band++)
        {
            var firstRow = band * 5;
            if (firstRow >= Height)
            {
                break;
            }

            var min = width;
            var max = -1;
            for (var x = 0; x < width; x++)
            {
                if (!ColumnHasPixel(pixels, x, firstRow))
                {
                    continue;
                }

                if (x < min)
                {
                    min = x;
                }

                max = x;
            }

            if (max < 0)
            {
                continue;
            }

            body.Add(0xD2);
            body.Add((byte)min);
            body.Add(0xD3);
            body.Add((byte)(4 + band * 5));
            body.Add(0xD4);
            body.Add(0x77);
            for (var x = min; x <= max; x++)
            {
                var value = 0x20;
                for (var bit = 0; bit < 5 && firstRow + bit < Height; bit++)
                {
                    if (pixels[firstRow + bit, x])
                    {
                        value |= 1 << bit;
                    }
                }

                body.Add((byte)value);
            }
        }
    }

    private static bool IsObject(byte[] payload, int i) =>
        i + 6 <= payload.Length &&
        payload[i] == 0xD2 &&
        payload[i + 2] == 0xD3 &&
        payload[i + 4] == 0xD4;

    private static int ObjectEnd(byte[] payload, int i)
    {
        var dataEnd = i + 6;
        while (dataEnd < payload.Length && payload[dataEnd] is not (0xD0 or 0xD1 or 0xD2 or 0xB0))
        {
            dataEnd++;
        }

        return dataEnd;
    }

    private static void DrawObject(
        bool[,] pixels,
        byte[] payload,
        int i,
        int width,
        Dictionary<int, MobitecSignFont> fontByCode)
    {
        var x = payload[i + 1] & 0xFF;
        var y = payload[i + 3] & 0xFF;
        var font = payload[i + 5] & 0xFF;
        var dataStart = i + 6;
        var dataEnd = ObjectEnd(payload, i);
        if (font == 0x77)
        {
            for (var column = 0; column < dataEnd - dataStart; column++)
            {
                var px = x + column;
                if (px < 0 || px >= width)
                {
                    continue;
                }

                var bits = payload[dataStart + column];
                for (var bit = 0; bit < 5; bit++)
                {
                    var row = y - 4 + bit;
                    if (row is >= 0 and < Height && (bits & (1 << bit)) != 0)
                    {
                        pixels[row, px] = true;
                    }
                }
            }
        }
        else if (fontByCode.TryGetValue(font, out var face))
        {
            face.DrawBaseline(pixels, payload.AsSpan(dataStart, dataEnd - dataStart), x, y);
        }
    }

    public static bool Same(bool[,] left, bool[,] right)
    {
        var rows = left.GetLength(0);
        var cols = left.GetLength(1);
        if (right.GetLength(0) != rows || right.GetLength(1) != cols)
        {
            return false;
        }

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                if (left[row, col] != right[row, col])
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool[,] Clone(bool[,] source)
    {
        var copy = new bool[source.GetLength(0), source.GetLength(1)];
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private static void ClearColumns(bool[,] pixels, int columns)
    {
        var rows = pixels.GetLength(0);
        var count = Math.Min(columns, pixels.GetLength(1));
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < count; column++)
            {
                pixels[row, column] = false;
            }
        }
    }

    private static void CopyColumns(bool[,] source, bool[,] target, int columns)
    {
        var rows = Math.Min(source.GetLength(0), target.GetLength(0));
        var count = Math.Min(columns, Math.Min(source.GetLength(1), target.GetLength(1)));
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < count; column++)
            {
                target[row, column] = source[row, column];
            }
        }
    }

    private static bool HasPixel(bool[,] pixels, int width)
    {
        for (var row = 0; row < Height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[row, x])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ColumnHasPixel(bool[,] pixels, int x, int firstRow)
    {
        for (var bit = 0; bit < 5 && firstRow + bit < Height; bit++)
        {
            if (pixels[firstRow + bit, x])
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryPayload(byte[] frame, out byte[] payload)
    {
        payload = [];
        var start = frame.Length > 4 && frame[0] == 0xFF && frame[2] == 0xA2 ? 4 : -1;
        if (start < 0)
        {
            return false;
        }

        var raw = new List<byte>(frame.Length - start);
        var i = start;
        while (i < frame.Length)
        {
            var value = frame[i] & 0xFF;
            if (value == 0xFE && i + 1 < frame.Length)
            {
                var next = frame[i + 1] & 0xFF;
                raw.Add(next == 0x01 ? (byte)0xFF : (byte)0xFE);
                i += 2;
                continue;
            }

            if (value == 0xFF)
            {
                break;
            }

            raw.Add((byte)value);
            i++;
        }

        if (raw.Count == 0)
        {
            return false;
        }

        raw.RemoveAt(raw.Count - 1);
        payload = raw.ToArray();
        return true;
    }

    private static void AppendChecksum(List<byte> body)
    {
        var sum = 0;
        for (var i = 1; i < body.Count; i++)
        {
            sum = (sum + body[i]) & 0xFF;
        }

        switch (sum)
        {
            case 0xFE:
                body.Add(0xFE);
                body.Add(0x00);
                break;
            case 0xFF:
                body.Add(0xFE);
                body.Add(0x01);
                break;
            default:
                body.Add((byte)sum);
                break;
        }
    }
}
