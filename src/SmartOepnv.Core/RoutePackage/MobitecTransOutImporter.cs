namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ein aus ZEdit TRANS.OUT / *.out extrahiertes Mobitec-Ziel.</summary>
public sealed class MobitecOutImportDestination
{
    public string Line { get; init; } = string.Empty;
    public string FrontText { get; init; } = string.Empty;
    public string SideText { get; init; } = string.Empty;
    public int IntervalSeconds { get; init; } = 3;
}

/// <summary>
/// Liest ZEdit32-/MIE-OUT-Dateien und extrahiert lesbare Zieltexte
/// aus A2-F0/F1-Frames (Adressen 06/07/0B). Wrapper-Bytes werden übersprungen.
/// </summary>
public static class MobitecTransOutImporter
{
    private static readonly HashSet<int> TextFonts =
    [
        0x36, 0x37, 0x64, 0x65, 0x69, 0x6C
    ];

    public static IReadOnlyList<MobitecOutImportDestination> Parse(byte[] data)
    {
        if (data is null || data.Length < 8)
        {
            return [];
        }

        var frames = ExtractFrames(data);
        if (frames.Count == 0)
        {
            return [];
        }

        var results = new List<MobitecOutImportDestination>();
        string? pendingLine = null;
        string? pendingFront = null;
        string? pendingSide = null;
        var pendingInterval = 3;

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(pendingFront) &&
                string.IsNullOrWhiteSpace(pendingSide) &&
                string.IsNullOrWhiteSpace(pendingLine))
            {
                return;
            }

            var front = (pendingFront ?? string.Empty).Trim();
            var side = (pendingSide ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(side))
            {
                side = front;
            }

            var line = (pendingLine ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(front) && string.IsNullOrEmpty(side))
            {
                ClearPending();
                return;
            }

            // Clear-/Smile-only überspringen
            if (IsNoiseText(front) && IsNoiseText(side))
            {
                ClearPending();
                return;
            }

            results.Add(new MobitecOutImportDestination
            {
                Line = line,
                FrontText = front,
                SideText = side,
                IntervalSeconds = pendingInterval
            });
            ClearPending();
        }

        void ClearPending()
        {
            pendingLine = null;
            pendingFront = null;
            pendingSide = null;
            pendingInterval = 3;
        }

        foreach (var frame in frames)
        {
            if (frame.IsClearOrEmpty)
            {
                continue;
            }

            switch (frame.Address)
            {
                case 0x0B:
                    if (!string.IsNullOrWhiteSpace(pendingFront) || !string.IsNullOrWhiteSpace(pendingSide))
                    {
                        pendingLine = frame.Text;
                        if (frame.IntervalSeconds is int iv)
                        {
                            pendingInterval = iv;
                        }

                        Flush();
                    }
                    else
                    {
                        pendingLine = frame.Text;
                    }

                    break;
                case 0x06:
                    if (!string.IsNullOrWhiteSpace(pendingFront))
                    {
                        Flush();
                    }

                    pendingFront = frame.Text;
                    if (frame.IntervalSeconds is int fi)
                    {
                        pendingInterval = fi;
                    }

                    break;
                case 0x07:
                    pendingSide = frame.Text;
                    if (frame.IntervalSeconds is int si)
                    {
                        pendingInterval = si;
                    }

                    break;
            }
        }

        Flush();

        return Deduplicate(results);
    }

    public static IReadOnlyList<OutsideDisplayProgram> ToPrograms(
        IReadOnlyList<MobitecOutImportDestination> destinations,
        IEnumerable<string>? existingIds = null)
    {
        var usedIds = new HashSet<string>(
            existingIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        var programs = new List<OutsideDisplayProgram>();
        var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var dest in destinations)
        {
            var displayName = BuildDisplayName(dest, nameCount);
            var program = OutsideDisplayProgram.CreateMobitec(displayName);
            program.Id = OutsideDisplayId.NewUniqueId(usedIds);
            usedIds.Add(program.Id);
            program.Ds001Value = string.IsNullOrWhiteSpace(dest.Line) ? "S8" : dest.Line.Trim();
            program.IntervalSeconds = Math.Clamp(dest.IntervalSeconds, 1, 99);
            program.AutoFitFonts = false;
            program.IsListEnabled = true;

            ApplyTextToCycles(program.FrontCycles, dest.FrontText);
            ApplyTextToCycles(program.SideCycles, dest.SideText);
            program.SyncLegacyLinesFromCycles();
            programs.Add(program);
        }

        return programs;
    }

    private static void ApplyTextToCycles(IList<OutsideDisplayTextCycle> cycles, string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        // F1-Seiten oft durch " | " getrennt vom Parser
        var pages = normalized.Split([" | "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < Math.Min(pages.Length, cycles.Count); i++)
        {
            var page = pages[i];
            var lines = page.Split('\n', StringSplitOptions.TrimEntries);
            var l1 = lines.ElementAtOrDefault(0) ?? string.Empty;
            var l2 = lines.Length > 1
                ? string.Join(" ", lines.Skip(1).Where(s => !string.IsNullOrWhiteSpace(s)))
                : string.Empty;
            cycles[i].SetFromPair(l1, l2);
        }
    }

    private static string BuildDisplayName(
        MobitecOutImportDestination dest,
        Dictionary<string, int> nameCount)
    {
        var frontFirst = dest.FrontText.Replace("\r\n", "\n").Split('\n')[0].Trim();
        var baseName = string.IsNullOrWhiteSpace(frontFirst)
            ? (string.IsNullOrWhiteSpace(dest.Line) ? "Import" : dest.Line.Trim())
            : frontFirst;
        if (!string.IsNullOrWhiteSpace(dest.Line) &&
            !baseName.StartsWith(dest.Line, StringComparison.OrdinalIgnoreCase))
        {
            baseName = $"{dest.Line.Trim()} {baseName}".Trim();
        }

        if (baseName.Length > 48)
        {
            baseName = baseName[..48].Trim();
        }

        nameCount.TryGetValue(baseName, out var n);
        nameCount[baseName] = n + 1;
        return n == 0 ? baseName : $"{baseName} ({n + 1})";
    }

    private static IReadOnlyList<MobitecOutImportDestination> Deduplicate(
        List<MobitecOutImportDestination> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<MobitecOutImportDestination>();
        foreach (var item in items)
        {
            var key = $"{item.Line}\n{item.FrontText}\n{item.SideText}";
            if (seen.Add(key))
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static bool IsNoiseText(string text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim() is " " or ".";

    private sealed class ParsedFrame
    {
        public int Address { get; init; }
        public string Text { get; init; } = string.Empty;
        public int? IntervalSeconds { get; init; }
        public bool IsClearOrEmpty { get; init; }
    }

    private static List<ParsedFrame> ExtractFrames(byte[] data)
    {
        var frames = new List<ParsedFrame>();
        var i = 0;
        while (i < data.Length - 4)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            var addr = data[i + 1] & 0xFF;
            if (addr is not (0x06 or 0x07 or 0x0B) || (data[i + 2] & 0xFF) != 0xA2)
            {
                i++;
                continue;
            }

            var mode = data[i + 3] & 0xFF;
            if (mode is not (0xF0 or 0xF1))
            {
                i++;
                continue;
            }

            var payloadStart = i + 4;
            var end = FindFrameEnd(data, payloadStart);
            if (end < 0)
            {
                i++;
                continue;
            }

            var payload = DecodeEscapes(data, payloadStart, end);
            frames.Add(ParsePayload(addr, payload));
            i = end + 1;
        }

        return frames;
    }

    private static int FindFrameEnd(byte[] data, int start)
    {
        var i = start;
        while (i < data.Length)
        {
            var b = data[i] & 0xFF;
            if (b == 0xFE && i + 1 < data.Length)
            {
                i += 2;
                continue;
            }

            if (b == 0xFF)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    private static byte[] DecodeEscapes(byte[] data, int start, int endExclusive)
    {
        var list = new List<byte>(endExclusive - start);
        var i = start;
        while (i < endExclusive)
        {
            var b = data[i] & 0xFF;
            if (b == 0xFE && i + 1 < endExclusive)
            {
                var n = data[i + 1] & 0xFF;
                list.Add(n == 0x01 ? (byte)0xFF : (byte)0xFE);
                i += 2;
                continue;
            }

            list.Add((byte)b);
            i++;
        }

        // Letztes Byte vor End-FF ist Checksumme – entfernen falls vorhanden
        if (list.Count > 0)
        {
            list.RemoveAt(list.Count - 1);
        }

        return list.ToArray();
    }

    private static ParsedFrame ParsePayload(int address, byte[] payload)
    {
        var texts = new List<(int Y, string Text)>();
        var hasClear = false;
        var hasOnlyBitmap = true;
        int? interval = null;
        var i = 0;
        while (i < payload.Length)
        {
            var b = payload[i] & 0xFF;
            if (b == 0xB0 && i + 1 < payload.Length)
            {
                interval = payload[i + 1] & 0xFF;
                i += 2;
                continue;
            }

            if (b == 0xD2 && i + 5 < payload.Length &&
                (payload[i + 2] & 0xFF) == 0xD3 &&
                (payload[i + 4] & 0xFF) == 0xD4)
            {
                var y = payload[i + 3] & 0xFF;
                var font = payload[i + 5] & 0xFF;
                i += 6;
                if (font is 0x78)
                {
                    hasClear = true;
                    continue;
                }

                if (font is 0x77)
                {
                    // Bitmap: bis zum nächsten Steuerbyte überspringen
                    while (i < payload.Length)
                    {
                        var n = payload[i] & 0xFF;
                        if (n is 0xD0 or 0xD1 or 0xD2 or 0xB0)
                        {
                            break;
                        }

                        i++;
                    }

                    continue;
                }

                if (!TextFonts.Contains(font))
                {
                    continue;
                }

                hasOnlyBitmap = false;
                var chars = new List<char>();
                while (i < payload.Length)
                {
                    var n = payload[i] & 0xFF;
                    if (n is 0xD0 or 0xD1 or 0xD2 or 0xB0)
                    {
                        break;
                    }

                    chars.Add(DecodeMobitecChar(n));
                    i++;
                }

                var text = new string(chars.ToArray()).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(text))
                {
                    texts.Add((y, text));
                }

                continue;
            }

            i++;
        }

        var combined = CombineTexts(texts);
        return new ParsedFrame
        {
            Address = address,
            Text = combined,
            IntervalSeconds = interval is >= 1 and <= 99 ? interval : null,
            IsClearOrEmpty = hasClear || (hasOnlyBitmap && string.IsNullOrWhiteSpace(combined))
        };
    }

    private static string CombineTexts(List<(int Y, string Text)> texts)
    {
        if (texts.Count == 0)
        {
            return string.Empty;
        }

        // Zwei Y-Bänder → Zeilenumbruch, sonst Leerzeichen / F1-Seiten mit |
        var distinctY = texts.Select(t => t.Y).Distinct().OrderBy(y => y).ToList();
        if (distinctY.Count >= 2)
        {
            var topY = distinctY[0];
            var top = string.Join(" ", texts.Where(t => t.Y == topY).Select(t => t.Text.Trim()));
            var bottom = string.Join(" ", texts.Where(t => t.Y != topY).Select(t => t.Text.Trim()));
            return string.Join('\n', new[] { top, bottom }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        // Mehrere Objekte gleiche Baseline: oft Linie + Ziel → Leerzeichen
        return string.Join(" ", texts.Select(t => t.Text.Trim()).Where(s => s.Length > 0));
    }

    private static char DecodeMobitecChar(int code) => code switch
    {
        0x7B => 'ä',
        0x7C => 'ö',
        0x5B => 'Ä',
        0x5C => 'Ö',
        >= 0x20 and <= 0x7E => (char)code,
        _ => '?'
    };
}
