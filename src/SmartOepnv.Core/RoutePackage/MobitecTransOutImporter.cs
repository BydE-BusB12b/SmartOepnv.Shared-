using System.Buffers.Binary;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ein aus ZEdit TRANS.OUT / *.out extrahiertes Mobitec-Ziel.</summary>
public sealed class MobitecOutImportDestination
{
    /// <summary>
    /// ZEdit-/ICU-Zielnummer (0–9999). Dient im Planer als stabile ID:
    /// erneuter OUT-Import aktualisiert dasselbe Ziel statt es zu verdoppeln.
    /// </summary>
    public int DestinationNumber { get; set; }

    public string Line { get; init; } = string.Empty;
    public string FrontText { get; init; } = string.Empty;
    public string SideText { get; init; } = string.Empty;
    public int IntervalSeconds { get; init; } = 3;
    /// <summary>Nur Bitmap (Smile) ohne nutzbaren Zieltext – wird als Danke/Smile-Programm angelegt.</summary>
    public bool IsSmileGraphic { get; init; }
    /// <summary>RS485-Wire-Frame Front (FF 06 A2 …), inkl. 0x77-Bitmaps aus der OUT.</summary>
    public byte[]? FrontFrame { get; init; }
    /// <summary>RS485-Wire-Frame Seite (FF 07 A2 …).</summary>
    public byte[]? SideFrame { get; init; }
    /// <summary>RS485-Wire-Frame Linie (FF 0B A2 …) – Tasse, Schraubenschlüssel, Logo, Smile, …</summary>
    public byte[]? LineFrame { get; init; }

    /// <summary>Dateioffset des ersten Frames dieses Ziels (für Index→Zielnummer).</summary>
    internal int SourceFileOffset { get; set; } = -1;

    public bool HasLineBitmap => MobitecTransOutImporter.FrameHasBitmap(LineFrame);
    public bool HasFrontBitmap => MobitecTransOutImporter.FrameHasBitmap(FrontFrame);
    public bool HasSideBitmap => MobitecTransOutImporter.FrameHasBitmap(SideFrame);

    /// <summary>Anzeige für Import-Dialog: „Grafik“ oder Linien-Text.</summary>
    public string LinePreview =>
        HasLineBitmap
            ? "Grafik"
            : string.IsNullOrWhiteSpace(Line) ? "—" : Line.Trim();

    public string FrontPreview => MobitecTransOutImporter.FormatTextPreview(FrontText);
    public string SidePreview => MobitecTransOutImporter.FormatTextPreview(SideText);
}

/// <summary>
/// Liest ZEdit32-/MIE-OUT-Dateien und extrahiert lesbare Zieltexte
/// aus A2-F0/F1-Frames (Adressen 06/07/0B).
/// Unterstützt Wire-Form <c>FF addr A2 … CS FF</c> und Container-Form
/// <c>… 04 03 addr A2 … FF</c> (ohne Checksumme, z. B. hambloch.out).
/// </summary>
public static class MobitecTransOutImporter
{
    private static readonly HashSet<int> TextFonts =
    [
        0x36, 0x37, 0x64, 0x65, 0x66, 0x69, 0x6C,
        0x70 // u. a. „Mc Donalds“ / Jingle-Zeile in hambloch.out
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
        var pendingFrontSmile = false;
        var pendingSideSmile = false;
        var pendingLineSmile = false;
        byte[]? pendingFrontFrame = null;
        byte[]? pendingSideFrame = null;
        byte[]? pendingLineFrame = null;
        var pendingSourceOffset = -1;
        // Nach einem fertigen Ziel dürfen Clear-Frames ein Blank-Ziel („Löschen“) eröffnen
        var allowBlankDestinationStart = false;

        void NoteSourceOffset(int fileOffset)
        {
            if (fileOffset < 0)
            {
                return;
            }

            if (pendingSourceOffset < 0 || fileOffset < pendingSourceOffset)
            {
                pendingSourceOffset = fileOffset;
            }
        }

        void Flush()
        {
            var hasRawFrames = pendingFrontFrame is { Length: > 0 } ||
                               pendingSideFrame is { Length: > 0 } ||
                               pendingLineFrame is { Length: > 0 };

            // Ohne Text und ohne Frames: verwerfen (aber Pending immer leeren)
            if (string.IsNullOrWhiteSpace(pendingFront) &&
                string.IsNullOrWhiteSpace(pendingSide) &&
                string.IsNullOrWhiteSpace(pendingLine) &&
                !pendingFrontSmile && !pendingSideSmile && !pendingLineSmile &&
                !hasRawFrames)
            {
                ClearPending();
                return;
            }

            var front = (pendingFront ?? string.Empty).Trim();
            var side = (pendingSide ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(side))
            {
                side = front;
            }

            var line = (pendingLine ?? string.Empty).Trim();
            var smileGraphic = (pendingFrontSmile || pendingLineSmile) &&
                IsNoiseText(front) &&
                (IsNoiseText(side) || LooksLikeDanke(side));

            // Leere Textziele mit Wire-Frames behalten (z. B. „Löschen“ / Blank)
            if (string.IsNullOrEmpty(front) && string.IsNullOrEmpty(side) && !smileGraphic && !hasRawFrames)
            {
                ClearPending();
                return;
            }

            // Clear-only überspringen; reine Smile-Grafik → Danke-Eintrag
            if (!smileGraphic && !hasRawFrames && IsNoiseText(front) && IsNoiseText(side))
            {
                ClearPending();
                return;
            }

            if (smileGraphic)
            {
                results.Add(new MobitecOutImportDestination
                {
                    DestinationNumber = results.Count + 1,
                    SourceFileOffset = pendingSourceOffset,
                    Line = string.Empty,
                    FrontText = "Danke",
                    SideText = "Danke",
                    IntervalSeconds = pendingInterval,
                    IsSmileGraphic = true,
                    FrontFrame = pendingFrontFrame,
                    SideFrame = pendingSideFrame,
                    LineFrame = pendingLineFrame
                });
                allowBlankDestinationStart = true;
            }
            else
            {
                var frontText = front;
                var embedded = ExtractEmbeddedLineLetter(frontText);
                if (!string.IsNullOrEmpty(embedded) &&
                    (string.IsNullOrWhiteSpace(line) ||
                     string.Equals(line, embedded, StringComparison.OrdinalIgnoreCase)))
                {
                    line = embedded;
                    frontText = frontText.Trim()[3..].TrimStart(); // nach „X  “
                    if (string.IsNullOrWhiteSpace(side) ||
                        side.Equals(front, StringComparison.OrdinalIgnoreCase))
                    {
                        side = frontText;
                    }
                }

                // Nur bei Bitmap ohne Text „Grafik“ setzen – leere Textziele bleiben leer
                var hasBitmap = FrameHasBitmap(pendingFrontFrame) ||
                                FrameHasBitmap(pendingSideFrame) ||
                                FrameHasBitmap(pendingLineFrame);
                if (string.IsNullOrWhiteSpace(frontText) && hasRawFrames && hasBitmap)
                {
                    frontText = line.Length > 0 ? line : "Grafik";
                }

                results.Add(new MobitecOutImportDestination
                {
                    DestinationNumber = results.Count + 1,
                    SourceFileOffset = pendingSourceOffset,
                    Line = line,
                    FrontText = frontText,
                    SideText = string.IsNullOrWhiteSpace(side) ? frontText : side,
                    IntervalSeconds = pendingInterval,
                    IsSmileGraphic = false,
                    FrontFrame = pendingFrontFrame,
                    SideFrame = pendingSideFrame,
                    LineFrame = pendingLineFrame
                });
                allowBlankDestinationStart = true;
            }

            ClearPending();
        }

        void ClearPending()
        {
            pendingLine = null;
            pendingFront = null;
            pendingSide = null;
            pendingInterval = 3;
            pendingFrontSmile = false;
            pendingSideSmile = false;
            pendingLineSmile = false;
            pendingFrontFrame = null;
            pendingSideFrame = null;
            pendingLineFrame = null;
            pendingSourceOffset = -1;
        }

        bool HasPendingSideOrFront() =>
            !string.IsNullOrWhiteSpace(pendingFront) ||
            !string.IsNullOrWhiteSpace(pendingSide) ||
            pendingFrontSmile ||
            pendingSideSmile ||
            pendingFrontFrame is { Length: > 0 } ||
            pendingSideFrame is { Length: > 0 };

        bool HasPendingLine() =>
            !string.IsNullOrWhiteSpace(pendingLine) ||
            pendingLineSmile ||
            pendingLineFrame is { Length: > 0 };

        // Blank-/Clear-Ziel ohne Text (Frames dürfen gesetzt sein)
        bool IsPendingBlankContent() =>
            string.IsNullOrWhiteSpace(pendingFront) &&
            string.IsNullOrWhiteSpace(pendingSide) &&
            string.IsNullOrWhiteSpace(pendingLine) &&
            !pendingFrontSmile &&
            !pendingSideSmile &&
            !pendingLineSmile;

        foreach (var frame in frames)
        {
            // Führende Clear-Frames ohne offenes Ziel ignorieren
            if (frame.IsClearOrEmpty && !frame.HasBitmap &&
                !HasPendingLine() && !HasPendingSideOrFront() &&
                !allowBlankDestinationStart)
            {
                continue;
            }

            if (frame.IsClearOrEmpty && !frame.HasBitmap && frame.WireFrame is not { Length: > 0 })
            {
                continue;
            }

            switch (frame.Address)
            {
                case 0x0B:
                    // Zwei vorkommende ZEdit-Ordnungen:
                    // A) LINE → SIDE → FRONT  (hambloch*.out) – LINE startet das Ziel
                    // B) SIDE → FRONT → LINE – LINE schließt das Ziel ab
                    // Bitmap-Linie (Logo/Tasse/Baum): Text im Frame ignorieren („!“ / Restzeichen).
                    var lineText = frame.HasBitmap ? string.Empty : frame.Text;
                    if (HasPendingSideOrFront() &&
                        !HasPendingLine() &&
                        IsPendingBlankContent())
                    {
                        // Offenes Blank (Clear Front/Seite) ohne eigene Linie:
                        // nicht die nächste echte Linie „klauen“ – Blank zuerst abschließen.
                        Flush();
                        pendingLineSmile = frame.HasBitmap && string.IsNullOrWhiteSpace(frame.Text);
                        pendingLine = lineText;
                        pendingLineFrame = frame.WireFrame;
                        NoteSourceOffset(frame.FileOffset);
                        if (frame.IntervalSeconds is int ivBlank)
                        {
                            pendingInterval = ivBlank;
                        }
                    }
                    else if (HasPendingSideOrFront())
                    {
                        // B: Linie gehört zum aktuellen (offenen) Ziel
                        pendingLineSmile = frame.HasBitmap && string.IsNullOrWhiteSpace(frame.Text);
                        pendingLine = lineText;
                        pendingLineFrame = frame.WireFrame;
                        NoteSourceOffset(frame.FileOffset);
                        if (frame.IntervalSeconds is int ivB)
                        {
                            pendingInterval = ivB;
                        }

                        Flush();
                    }
                    else
                    {
                        // A: neue Linie startet das nächste Ziel
                        // (vorherige Linie ohne Side/Front verwerfen – ZEdit-Artefakt)
                        pendingLineSmile = frame.HasBitmap && string.IsNullOrWhiteSpace(frame.Text);
                        pendingLine = lineText;
                        pendingLineFrame = frame.WireFrame;
                        NoteSourceOffset(frame.FileOffset);
                        if (frame.IntervalSeconds is int ivA)
                        {
                            pendingInterval = ivA;
                        }
                    }

                    break;
                case 0x06:
                    var newFrontBlank = frame.IsClearOrEmpty || string.IsNullOrWhiteSpace(frame.Text);
                    if (pendingFrontFrame is { Length: > 0 } ||
                        !string.IsNullOrWhiteSpace(pendingFront) ||
                        pendingFrontSmile)
                    {
                        // Aufeinanderfolgende Blank-Fronts zusammenführen (kein Extra-„Löschen“)
                        if (!(IsPendingBlankContent() && newFrontBlank))
                        {
                            Flush();
                        }
                    }

                    pendingFrontSmile = frame.HasBitmap && string.IsNullOrWhiteSpace(frame.Text);
                    pendingFront = frame.Text;
                    pendingFrontFrame = frame.WireFrame;
                    NoteSourceOffset(frame.FileOffset);
                    if (frame.IntervalSeconds is int fi)
                    {
                        pendingInterval = fi;
                    }

                    // A: nach FRONT ist das Ziel komplett, wenn LINE (und ggf. SIDE) schon da
                    if (HasPendingLine())
                    {
                        Flush();
                    }

                    break;
                case 0x07:
                    pendingSideSmile = frame.HasBitmap && string.IsNullOrWhiteSpace(frame.Text);
                    pendingSide = frame.Text;
                    pendingSideFrame = frame.WireFrame;
                    NoteSourceOffset(frame.FileOffset);
                    if (frame.IntervalSeconds is int si)
                    {
                        pendingInterval = si;
                    }

                    break;
            }
        }

        // Trailing Blank ohne eigene Linie verwerfen (nur Zwischen-Löschen behalten)
        if (IsPendingBlankContent() && !HasPendingLine())
        {
            ClearPending();
        }
        else
        {
            Flush();
        }

        var list = Deduplicate(CollapseAdjacentBlankDestinations(results));
        AssignDestinationNumbersFromIndex(data, list);
        return list;
    }

    /// <summary>
    /// Vergibt ICU-/ZEdit-Zielnummern aus der Sparse-Index-Tabelle der OUT-Datei
    /// (Slot 1…1000, Lücken = leer, z. B. 991/992/993/1000). Fallback: 1…n.
    /// </summary>
    private static void AssignDestinationNumbersFromIndex(
        byte[] data,
        IReadOnlyList<MobitecOutImportDestination> destinations)
    {
        var index = TryParseDestinationIndex(data);
        if (index.Count == 0)
        {
            for (var i = 0; i < destinations.Count; i++)
            {
                destinations[i].DestinationNumber = i + 1;
            }

            return;
        }

        // Index nach Dateioffset sortiert: Ziel belegt [offset, nextOffset)
        var byOffset = index
            .Select(kv => (Number: kv.Key, Offset: kv.Value))
            .OrderBy(e => e.Offset)
            .ToList();

        var usedNumbers = new HashSet<int>();
        foreach (var dest in destinations)
        {
            var number = ResolveNumberForOffset(byOffset, dest.SourceFileOffset);
            if (number is >= 0 and <= 9999 && usedNumbers.Add(number))
            {
                dest.DestinationNumber = number;
            }
            else
            {
                // Fallback: nächste freie Nummer ab 1 (sollte selten nötig sein)
                var n = 1;
                while (usedNumbers.Contains(n) && n < 9999)
                {
                    n++;
                }

                dest.DestinationNumber = n;
                usedNumbers.Add(n);
            }
        }
    }

    private static int ResolveNumberForOffset(
        IReadOnlyList<(int Number, int Offset)> byOffset,
        int sourceOffset)
    {
        if (sourceOffset < 0 || byOffset.Count == 0)
        {
            return 0;
        }

        // Index zeigt oft auf „04 03“ vor dem A2-Frame → Frame-Offset kann leicht dahinter liegen.
        for (var i = byOffset.Count - 1; i >= 0; i--)
        {
            if (byOffset[i].Offset <= sourceOffset + 4)
            {
                return byOffset[i].Number;
            }
        }

        return 0;
    }

    /// <summary>
    /// Liest die ZEdit-Sparse-Tabelle: maxSlots × u32 ab IndexStart.
    /// Leer = 0x0FF9; belegt = absoluter Offset oder page*4096+offset (page in High-Byte).
    /// Zielnummer = Slot-Index (0-basiert, ICU 0000–9999).
    /// </summary>
    private static Dictionary<int, int> TryParseDestinationIndex(byte[] data)
    {
        var result = new Dictionary<int, int>();
        if (data.Length < 0x20)
        {
            return result;
        }

        var maxSlots = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x10));
        var indexStart = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x12));
        if (maxSlots is < 1 or > 4096 || indexStart < 0x20)
        {
            return result;
        }

        var tableBytes = (long)maxSlots * 4;
        if (indexStart + tableBytes > data.Length)
        {
            return result;
        }

        const uint emptySentinel = 0x0FF9;
        for (var slot = 0; slot < maxSlots; slot++)
        {
            var pos = indexStart + slot * 4;
            var val = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            if (val == 0 || val == emptySentinel)
            {
                continue;
            }

            var fileOffset = ResolveIndexEntryOffset(val);
            if (fileOffset is null || fileOffset.Value < 0 || fileOffset.Value >= data.Length)
            {
                continue;
            }

            // ICU-/ZEdit-Zielnummer = 0-basierter Tabellenindex (nicht slot+1).
            result[slot] = fileOffset.Value;
        }

        // Plausibilität: mindestens ein paar Einträge, die in den Datenteil zeigen
        if (result.Count < 8)
        {
            result.Clear();
        }

        return result;
    }

    private static int? ResolveIndexEntryOffset(uint value)
    {
        // Absoluter Zeiger (z. B. erstes Ziel direkt auf Datenstart 0x0FFA)
        if (value is >= 4090 and < 0x10000)
        {
            return (int)value;
        }

        // Struktur: u16 offset | u8 0x00 | u8 page  →  page * 4096 + offset
        var page = (int)((value >> 24) & 0xFF);
        var offset = (int)(value & 0xFFFF);
        if (page >= 1)
        {
            return page * 4096 + offset;
        }

        return null;
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
            var name = SuggestDisplayName(dest, nameCount);
            programs.Add(ToProgram(dest, name, usedIds));
        }

        return programs;
    }

    /// <summary>Einzelnes OUT-Ziel → Planer-Programm (Name 1:1 wie im Dialog gewählt).</summary>
    public static OutsideDisplayProgram ToProgram(
        MobitecOutImportDestination dest,
        string displayName,
        ISet<string> usedIds)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? SuggestDisplayName(dest) : displayName.Trim();

        if (dest.IsSmileGraphic ||
            (string.IsNullOrWhiteSpace(dest.Line) &&
             LooksLikeDanke(dest.FrontText) &&
             LooksLikeDanke(dest.SideText)))
        {
            var smile = OutsideDisplayProgram.CreateMobitecSmile();
            smile.Id = OutsideDisplayId.NewUniqueId(usedIds);
            usedIds.Add(smile.Id);
            smile.DestinationNumber = NormalizeDestinationNumber(dest.DestinationNumber);
            smile.Name = name;
            smile.IsListEnabled = true;
            ApplyRawFrames(smile, dest);
            return smile;
        }

        var program = OutsideDisplayProgram.CreateMobitec(name);
        // App-ID bleibt unabhängig von der ICU-Zielnummer (Upsert läuft über DestinationNumber).
        program.Id = OutsideDisplayId.NewUniqueId(usedIds);
        usedIds.Add(program.Id);
        program.DestinationNumber = NormalizeDestinationNumber(dest.DestinationNumber);
        // Linien-Bitmap (Tasse, Baum, …) ersetzt den Linientext – kein RE10/001 im Feld.
        program.Ds001Value = dest.HasLineBitmap
            ? string.Empty
            : dest.Line?.Trim() ?? string.Empty;
        program.IntervalSeconds = Math.Clamp(dest.IntervalSeconds, 1, 99);
        program.AutoFitFonts = false;
        program.IsListEnabled = true;

        ApplyTextToCycles(program.FrontCycles, dest.FrontText);
        ApplyTextToCycles(program.SideCycles, dest.SideText);
        program.SyncLegacyLinesFromCycles();
        ApplyRawFrames(program, dest);
        return program;
    }

    /// <summary>OUT-Zielnummer → App-ID nur noch als Fallback, wenn die Nummer als ID frei ist.</summary>
    public static string ResolveDestinationId(int destinationNumber, ISet<string> usedIds)
    {
        if (destinationNumber is >= 0 and <= 9999)
        {
            var id = destinationNumber.ToString("D4");
            if (!usedIds.Contains(id))
            {
                return id;
            }
        }

        return OutsideDisplayId.NewUniqueId(usedIds);
    }

    private static int? NormalizeDestinationNumber(int destinationNumber) =>
        destinationNumber is >= 0 and <= 9999 ? destinationNumber : null;

    /// <summary>Übernimmt OUT-Inhalt in ein bestehendes Mobitec-Programm (gleiche Zielnummer).</summary>
    public static void ApplyToExistingProgram(OutsideDisplayProgram program, MobitecOutImportDestination dest, string displayName)
    {
        var keepId = program.Id;
        // Frisches Programm ohne ID-Konflikt aufbauen, dann Inhalt übernehmen.
        var fresh = ToProgram(dest, displayName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        program.Name = fresh.Name;
        program.Protocol = OutsideDisplayProtocolKind.Mobitec;
        program.Ds001Value = fresh.Ds001Value;
        program.IntervalSeconds = fresh.IntervalSeconds;
        program.AutoFitFonts = fresh.AutoFitFonts;
        program.IsListEnabled = true;
        program.DestinationNumber = fresh.DestinationNumber;
        program.MobitecFrontFrame = fresh.MobitecFrontFrame;
        program.MobitecSideFrame = fresh.MobitecSideFrame;
        program.MobitecLineFrame = fresh.MobitecLineFrame;
        for (var i = 0; i < program.FrontCycles.Count; i++)
        {
            if (i < fresh.FrontCycles.Count)
            {
                program.FrontCycles[i].SetFromPair(fresh.FrontCycles[i].Line1, fresh.FrontCycles[i].Line2);
            }
            else
            {
                program.FrontCycles[i].SetFromPair(string.Empty, string.Empty);
            }
        }

        for (var i = 0; i < program.SideCycles.Count; i++)
        {
            if (i < fresh.SideCycles.Count)
            {
                program.SideCycles[i].SetFromPair(fresh.SideCycles[i].Line1, fresh.SideCycles[i].Line2);
            }
            else
            {
                program.SideCycles[i].SetFromPair(string.Empty, string.Empty);
            }
        }

        program.SyncLegacyLinesFromCycles();
        program.Id = keepId;
        program.NotifyMobitecGraphicsChanged();
        program.RefreshListDisplayProperties();
    }

    public static string SuggestDisplayName(MobitecOutImportDestination dest) =>
        SuggestDisplayName(dest, nameCount: null);

    public static string SuggestDisplayName(
        MobitecOutImportDestination dest,
        Dictionary<string, int>? nameCount)
    {
        var front = FirstPageText(dest.FrontText);
        var side = FirstPageText(dest.SideText);
        var baseName = !string.IsNullOrWhiteSpace(front)
            ? string.Join(' ', front.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : !string.IsNullOrWhiteSpace(side)
                ? string.Join(' ', side.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                : !string.IsNullOrWhiteSpace(dest.Line)
                    ? dest.Line.Trim()
                    : dest.HasLineBitmap || dest.HasFrontBitmap
                        ? "Grafik"
                        : dest.FrontFrame is { Length: > 0 } ||
                          dest.SideFrame is { Length: > 0 } ||
                          dest.LineFrame is { Length: > 0 }
                            ? "Löschen"
                            : "Import";

        if (baseName.Length > 48)
        {
            baseName = baseName[..48].Trim();
        }

        if (nameCount is null)
        {
            return baseName;
        }

        nameCount.TryGetValue(baseName, out var n);
        nameCount[baseName] = n + 1;
        return n == 0 ? baseName : $"{baseName} ({n + 1})";
    }

    public static string FormatTextPreview(string text)
    {
        var t = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrEmpty(t))
        {
            return "—";
        }

        // Wechseltext-Seiten: „Seite1 · Seite2“
        var pages = t.Split([" | "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" → ", pages.Select(p =>
            string.Join(" · ", p.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))));
    }

    private static string FirstPageText(string text)
    {
        var t = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrEmpty(t))
        {
            return string.Empty;
        }

        var pipe = t.IndexOf(" | ", StringComparison.Ordinal);
        return pipe < 0 ? t : t[..pipe].Trim();
    }

    public static bool FrameHasBitmap(byte[]? frame)
    {
        if (frame is null || frame.Length < 6)
        {
            return false;
        }

        for (var i = 0; i < frame.Length - 1; i++)
        {
            if (frame[i] == 0xD4 && frame[i + 1] == 0x77)
            {
                return true;
            }
        }

        return false;
    }

    private static bool FrameContainsBitmap(byte[]? frame) => FrameHasBitmap(frame);

    private static void ApplyRawFrames(OutsideDisplayProgram program, MobitecOutImportDestination dest)
    {
        program.MobitecFrontFrame = CloneFrame(dest.FrontFrame);
        program.MobitecSideFrame = CloneFrame(dest.SideFrame);
        program.MobitecLineFrame = CloneFrame(dest.LineFrame);
        // UI-Binding (HasMobitecRawFrames / MobitecGraphicsLabel)
        program.NotifyMobitecGraphicsChanged();
    }

    private static byte[]? CloneFrame(byte[]? frame) =>
        frame is { Length: > 0 } ? (byte[])frame.Clone() : null;

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
        Dictionary<string, int> nameCount) =>
        SuggestDisplayName(dest, nameCount);

    private static IReadOnlyList<MobitecOutImportDestination> Deduplicate(
        List<MobitecOutImportDestination> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<MobitecOutImportDestination>();
        foreach (var item in items)
        {
            // Frames mit einbeziehen – sonst fallen leere/Blank-Ziele mit gleichem Text weg
            var key =
                $"{item.IsSmileGraphic}\n{item.Line}\n{item.FrontText}\n{item.SideText}\n" +
                $"{FrameFingerprint(item.FrontFrame)}|{FrameFingerprint(item.SideFrame)}|{FrameFingerprint(item.LineFrame)}";
            if (seen.Add(key))
            {
                list.Add(item);
            }
        }

        return list;
    }

    /// <summary>
    /// Aufeinanderfolgende Blank-/Löschen-Ziele zu einem Eintrag zusammenführen
    /// (ZEdit liefert oft mehrere Clear-Frames hintereinander).
    /// </summary>
    private static List<MobitecOutImportDestination> CollapseAdjacentBlankDestinations(
        List<MobitecOutImportDestination> items)
    {
        var list = new List<MobitecOutImportDestination>();
        foreach (var item in items)
        {
            if (IsBlankDeleteDestination(item) &&
                list.Count > 0 &&
                IsBlankDeleteDestination(list[^1]))
            {
                var prev = list[^1];
                list[^1] = new MobitecOutImportDestination
                {
                    DestinationNumber = prev.DestinationNumber > 0 ? prev.DestinationNumber : item.DestinationNumber,
                    SourceFileOffset = prev.SourceFileOffset >= 0 ? prev.SourceFileOffset : item.SourceFileOffset,
                    Line = string.Empty,
                    FrontText = string.Empty,
                    SideText = string.Empty,
                    IntervalSeconds = prev.IntervalSeconds,
                    IsSmileGraphic = false,
                    FrontFrame = prev.FrontFrame is { Length: > 0 } ? prev.FrontFrame : item.FrontFrame,
                    SideFrame = prev.SideFrame is { Length: > 0 } ? prev.SideFrame : item.SideFrame,
                    LineFrame = prev.LineFrame is { Length: > 0 } ? prev.LineFrame : item.LineFrame
                };
                continue;
            }

            list.Add(item);
        }

        return list;
    }

    private static bool IsBlankDeleteDestination(MobitecOutImportDestination item) =>
        !item.IsSmileGraphic &&
        string.IsNullOrWhiteSpace(item.Line) &&
        string.IsNullOrWhiteSpace(item.FrontText) &&
        string.IsNullOrWhiteSpace(item.SideText) &&
        !item.HasLineBitmap &&
        !item.HasFrontBitmap &&
        !item.HasSideBitmap &&
        (item.FrontFrame is { Length: > 0 } ||
         item.SideFrame is { Length: > 0 } ||
         item.LineFrame is { Length: > 0 });

    private static string FrameFingerprint(byte[]? frame)
    {
        if (frame is null || frame.Length == 0)
        {
            return "-";
        }

        // Länge + einfache Prüfsumme reicht, um Blank-/Grafik-Frames zu unterscheiden
        var sum = 0;
        for (var i = 0; i < frame.Length; i++)
        {
            sum = (sum + frame[i] * (i + 1)) & 0x7FFFFFFF;
        }

        return $"{frame.Length}:{sum}";
    }

    private static bool IsNoiseText(string text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim() is " " or ".";

    private static bool LooksLikeDanke(string text) =>
        text.Trim().Equals("Danke", StringComparison.OrdinalIgnoreCase) ||
        text.Trim().Equals("Dank", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Front-Text „D  Dienstfahrt“ / „S  Sonderfahrt“ → eingebetteter Linienbuchstabe.
    /// </summary>
    private static string? ExtractEmbeddedLineLetter(string front)
    {
        var t = front.Trim();
        if (t.Length < 4)
        {
            return null;
        }

        // Ein Buchstabe, dann ≥2 Leerzeichen, dann Zieltext
        if (char.IsLetter(t[0]) && t[1] == ' ' && t[2] == ' ' && !char.IsWhiteSpace(t[3]))
        {
            return t[0].ToString();
        }

        return null;
    }

    private sealed class ParsedFrame
    {
        public int Address { get; init; }
        public string Text { get; init; } = string.Empty;
        public int? IntervalSeconds { get; init; }
        public bool IsClearOrEmpty { get; init; }
        public bool HasBitmap { get; init; }
        /// <summary>Dateioffset des A2-Frames (addr-Byte).</summary>
        public int FileOffset { get; init; } = -1;
        /// <summary>Vollständiger RS485-Frame inkl. FF … Checksumme … FF.</summary>
        public byte[]? WireFrame { get; init; }
    }

    private static List<ParsedFrame> ExtractFrames(byte[] data)
    {
        var frames = new List<ParsedFrame>();
        var i = 0;
        while (i < data.Length - 3)
        {
            var addr = data[i] & 0xFF;
            if (addr is not (0x06 or 0x07 or 0x0B) ||
                (data[i + 1] & 0xFF) != 0xA2)
            {
                i++;
                continue;
            }

            var mode = data[i + 2] & 0xFF;
            if (mode is not (0xF0 or 0xF1))
            {
                i++;
                continue;
            }

            // Wire: FF addr A2 … CS FF  |  Container (hambloch): … addr A2 … Text FF (ohne CS)
            var wireStyle = i > 0 && (data[i - 1] & 0xFF) == 0xFF;
            var payloadStart = i + 3;
            var end = FindFrameEnd(data, payloadStart);
            if (end < 0)
            {
                i++;
                continue;
            }

            var payloadForParse = DecodeEscapes(data, payloadStart, end, stripTrailingChecksum: wireStyle);
            var parsed = ParsePayload(addr, payloadForParse);

            byte[] wireFrame;
            if (wireStyle)
            {
                // Originalbytes FF…FF unverändert
                wireFrame = data[(i - 1)..(end + 1)];
            }
            else
            {
                // Container-Payload (mit FE-Escapes wie in der Datei) + Checksumme
                var rawPayload = new byte[end - payloadStart];
                Buffer.BlockCopy(data, payloadStart, rawPayload, 0, rawPayload.Length);
                wireFrame = BuildWireFrame(addr, mode, rawPayload);
            }

            frames.Add(new ParsedFrame
            {
                Address = parsed.Address,
                Text = parsed.Text,
                IntervalSeconds = parsed.IntervalSeconds,
                IsClearOrEmpty = parsed.IsClearOrEmpty,
                HasBitmap = parsed.HasBitmap,
                FileOffset = i,
                WireFrame = wireFrame
            });
            i = end + 1;
        }

        return frames;
    }

    /// <summary>Baut FF addr A2 mode + payload + CS + FF (Payload bereits escaped wie auf dem Bus).</summary>
    private static byte[] BuildWireFrame(int addr, int mode, byte[] payload)
    {
        var body = new List<byte>(4 + payload.Length + 3)
        {
            0xFF,
            (byte)addr,
            0xA2,
            (byte)mode
        };
        body.AddRange(payload);
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

        body.Add(0xFF);
        return body.ToArray();
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

    private static byte[] DecodeEscapes(
        byte[] data,
        int start,
        int endExclusive,
        bool stripTrailingChecksum)
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

        // Wire-Frames: letztes Byte vor End-FF ist Checksumme.
        // ZEdit-Container (hambloch.out): Text endet direkt vor FF – nicht abschneiden.
        if (stripTrailingChecksum && list.Count > 0)
        {
            list.RemoveAt(list.Count - 1);
        }

        return list.ToArray();
    }

    private static ParsedFrame ParsePayload(int address, byte[] payload)
    {
        // F1-Wechseltexte: B0 trennt Seiten (Ziel 1 | Ziel 2 | …), Intervall steckt im B0-Byte.
        var pages = new List<List<(int Y, string Text)>> { new() };
        var hasClear = false;
        var hasBitmap = false;
        var hasText = false;
        int? interval = null;
        var i = 0;
        while (i < payload.Length)
        {
            var b = payload[i] & 0xFF;
            if (b == 0xB0 && i + 1 < payload.Length)
            {
                interval = payload[i + 1] & 0xFF;
                // Nach vorhandenem Seiteninhalt: neue Wechseltext-Seite beginnen
                if (pages[^1].Count > 0)
                {
                    pages.Add([]);
                }

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
                    hasBitmap = true;
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

                hasText = true;
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
                    pages[^1].Add((y, text));
                }

                continue;
            }

            i++;
        }

        var combined = CombinePages(pages);
        return new ParsedFrame
        {
            Address = address,
            Text = combined,
            IntervalSeconds = interval is >= 1 and <= 99 ? interval : null,
            HasBitmap = hasBitmap,
            IsClearOrEmpty = hasClear || (!hasText && !hasBitmap && string.IsNullOrWhiteSpace(combined))
        };
    }

    /// <summary>
    /// F1-Seiten (durch B0 getrennt) mit <c> | </c> verbinden – <see cref="ApplyTextToCycles"/> mappt das auf Ziel 1–4.
    /// </summary>
    private static string CombinePages(List<List<(int Y, string Text)>> pages)
    {
        var pageTexts = new List<string>();
        foreach (var page in pages)
        {
            var combined = CombineTexts(page);
            if (string.IsNullOrWhiteSpace(combined))
            {
                continue;
            }

            // Identische Folgeseiten (ZEdit speichert oft dieselbe Seite 2× für längere Anzeige) zusammenfassen
            if (pageTexts.Count > 0 &&
                string.Equals(pageTexts[^1], combined, StringComparison.Ordinal))
            {
                continue;
            }

            pageTexts.Add(combined);
        }

        return string.Join(" | ", pageTexts);
    }

    private static string CombineTexts(List<(int Y, string Text)> texts)
    {
        if (texts.Count == 0)
        {
            return string.Empty;
        }

        // Zwei Y-Bänder → Zeilenumbruch (Zeile 1 / Zeile 2), sonst Leerzeichen
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
        0x7D => 'ü',
        0x5B => 'Ä',
        0x5C => 'Ö',
        0x5D => 'Ü',
        0x7E => 'ß',
        >= 0x20 and <= 0x7E => (char)code,
        _ => '?'
    };
}
