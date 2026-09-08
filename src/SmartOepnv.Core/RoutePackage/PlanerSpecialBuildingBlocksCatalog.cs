namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Feste Spezialbausteine für Begrüßung/Ansage-Sequenzen (und, Nach, über, Gong, Linien).
/// Werden im Planer als Extrabibliothek gepflegt – nicht nur über den Dropbox-Ordner.
/// </summary>
public static class PlanerSpecialBuildingBlocksCatalog
{
    public sealed record Block(
        string Id,
        string Title,
        string FileName,
        string Description,
        bool IsLineBlock = false,
        bool IsUserAdded = false);

    public static IReadOnlyList<Block> FixedBlocks { get; } =
    [
        new("und", "und", PlanerUndSoundResolver.FileName,
            "Verbindung in der Haltestellen-Aufzählung (A, B und C)"),
        new("nach", "Nach", PlanerNachSoundResolver.FileName,
            "Baustein „Nach“ vor dem Endziel"),
        new("bus_ueber", "Dieser Bus fährt über", PlanerBusDrivesViaSoundResolver.FileName,
            "Intro vor der Zwischenhalte-Liste"),
        new("gong", "Hambloch Gong", PlanerGongSoundResolver.GongFileName,
            "Gong vor normalen Haltestellenansagen"),
        new("naechste", "Nächste Haltestelle", PlanerNextStopSoundResolver.FileName,
            "Präfix vor dem Haltestellennamen"),
        new("next_stop", "Next Stop", PlanerNextStopMp3SoundResolver.FileName,
            "Englisches Präfix (optional) – bevorzugt WAV, Fallback Next Stop.mp3"),
        new("folgende", "Folgende Halte", PlanerFollowingStopsSoundResolver.FileName,
            "Präfix „Folgende Halte“ (optional) vor dem Haltestellennamen")
    ];

    public static IReadOnlyList<Block> BuildAll(
        EditableRoutePackage? package,
        IEnumerable<string>? extraLines = null,
        IEnumerable<string>? hiddenLines = null)
    {
        var list = new List<Block>(FixedBlocks);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hiddenLines is not null)
        {
            foreach (var h in hiddenLines)
            {
                var t = h?.Trim() ?? string.Empty;
                if (t.Length == 0)
                {
                    continue;
                }

                hidden.Add(t);
                var fn = PlanerLineSoundResolver.BuildFileName(t);
                if (!string.IsNullOrWhiteSpace(fn))
                {
                    hidden.Add(fn);
                    hidden.Add(Path.GetFileNameWithoutExtension(fn));
                }
            }
        }

        bool IsHidden(string line, string fileName) =>
            hidden.Contains(line) ||
            hidden.Contains(fileName) ||
            hidden.Contains(Path.GetFileNameWithoutExtension(fileName));

        var lineBlocks = new List<Block>();

        void TryAddLine(string line, bool userAdded)
        {
            var trimmed = line?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            var fileName = PlanerLineSoundResolver.BuildFileName(trimmed);
            if (string.IsNullOrWhiteSpace(fileName) || !seen.Add(fileName))
            {
                return;
            }

            if (IsHidden(trimmed, fileName))
            {
                return;
            }

            lineBlocks.Add(new Block(
                Id: $"line:{trimmed}",
                Title: trimmed,
                FileName: fileName,
                Description: userAdded
                    ? $"Manuell hinzugefügte Linienansage „{trimmed}“"
                    : $"Linienansage für Fahrgastanzeige „{trimmed}“",
                IsLineBlock: true,
                IsUserAdded: userAdded));
        }

        if (package is not null)
        {
            foreach (var line in CollectPassengerDisplayLines(package))
            {
                TryAddLine(line, userAdded: false);
            }
        }

        if (extraLines is not null)
        {
            foreach (var line in extraLines
                         .Select(l => l.Trim())
                         .Where(l => l.Length > 0))
            {
                TryAddLine(line, userAdded: true);
            }
        }

        // Alle Linien gemeinsam natürlich sortieren (S2 vor S19), unabhängig von Quelle
        list.AddRange(lineBlocks.OrderBy(b => b.Title, LineLabelNaturalComparer.Instance));

        return list;
    }

    /// <summary>
    /// Natürliche Sortierung: S2 vor S19/S20 (nicht lexikografisch S19, S2, S20).
    /// </summary>
    private sealed class LineLabelNaturalComparer : IComparer<string>
    {
        public static LineLabelNaturalComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var i = 0;
            var j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    var numX = 0L;
                    var startI = i;
                    while (i < x.Length && char.IsDigit(x[i]))
                    {
                        numX = numX * 10 + (x[i] - '0');
                        i++;
                    }

                    var numY = 0L;
                    var startJ = j;
                    while (j < y.Length && char.IsDigit(y[j]))
                    {
                        numY = numY * 10 + (y[j] - '0');
                        j++;
                    }

                    var cmp = numX.CompareTo(numY);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    // gleiche Zahl: kürzere Ziffernfolge zuerst (z. B. 2 vor 02)
                    cmp = (i - startI).CompareTo(j - startJ);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    continue;
                }

                var cx = char.ToUpperInvariant(x[i]);
                var cy = char.ToUpperInvariant(y[j]);
                if (cx != cy)
                {
                    return cx.CompareTo(cy);
                }

                i++;
                j++;
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }
    }

    public static IEnumerable<string> CollectPassengerDisplayLines(EditableRoutePackage package)
    {
        var lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var routeName in package.RouteNames)
        {
            var parsed = RouteDisplayHelper.Parse(routeName);
            var displayLine = (parsed.PassengerDisplayLine ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(displayLine))
            {
                displayLine = RouteDisplayHelper.NormalizeLineCourse(parsed.LineCourse);
            }

            if (!string.IsNullOrWhiteSpace(displayLine))
            {
                lines.Add(displayLine);
            }
        }

        return lines;
    }

    /// <summary>Alle Linien-Stämme für Export/Staging (Routen + manuell, ohne ausgeblendete).</summary>
    public static IEnumerable<string> CollectAllLineStems(
        EditableRoutePackage? package,
        IEnumerable<string>? extraLines = null,
        IEnumerable<string>? hiddenLines = null)
    {
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hiddenLines is not null)
        {
            foreach (var h in hiddenLines)
            {
                var t = h?.Trim() ?? string.Empty;
                if (t.Length == 0) continue;
                hidden.Add(t);
                var fn = PlanerLineSoundResolver.BuildFileName(t);
                if (!string.IsNullOrWhiteSpace(fn))
                {
                    hidden.Add(fn);
                    hidden.Add(Path.GetFileNameWithoutExtension(fn));
                }
            }
        }

        bool IsHidden(string line)
        {
            var fn = PlanerLineSoundResolver.BuildFileName(line);
            return hidden.Contains(line) ||
                   hidden.Contains(fn) ||
                   hidden.Contains(Path.GetFileNameWithoutExtension(fn));
        }

        var lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (package is not null)
        {
            foreach (var line in CollectPassengerDisplayLines(package))
            {
                if (!IsHidden(line))
                {
                    lines.Add(line);
                }
            }
        }

        if (extraLines is not null)
        {
            foreach (var line in extraLines)
            {
                var trimmed = line?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(trimmed) && !IsHidden(trimmed))
                {
                    lines.Add(trimmed);
                }
            }
        }

        return lines;
    }

    public static string? TryResolvePath(
        LocalWorkspaceStore workspace,
        string fileName,
        string? dropboxApiFolderPath = null) =>
        PlanerHamblochAnsagenSoundResolver.TryResolve(workspace, fileName, dropboxApiFolderPath);
}
