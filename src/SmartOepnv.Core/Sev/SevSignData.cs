namespace SmartOepnv.Core.Sev;

public sealed class SevSignData
{
    public required string Line { get; init; }

    public required string Destination { get; init; }

    public required IReadOnlyList<string> Stops { get; init; }

    public IReadOnlyList<SevOperatorKind> Operators { get; init; } = [SevOperatorKind.RegioBahn];

    public SevOperatorKind Operator => Operators.Count > 0 ? Operators[0] : SevOperatorKind.RegioBahn;

    public string FormattedLine => FormatLine(Line);

    public (string Line1, string Line2) DestinationLines => SplitDestination(Destination);

    public SevDestinationLayout DestinationLayout => SevDestinationLayout.FromDestination(Destination);

    public static string FormatLine(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0].Length <= 3 && char.IsLetter(parts[0][0]))
        {
            return $"{parts[0].ToUpperInvariant()} {string.Join(' ', parts.Skip(1))}";
        }

        return trimmed;
    }

    public static (string Line1, string Line2) SplitDestination(string raw)
    {
        var layout = SevDestinationLayout.FromDestination(raw);
        return (layout.PrimaryLine, layout.SecondaryLine);
    }

    public static bool TryStripExpressBusPrefix(string raw, out string remainder)
    {
        remainder = string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!trimmed.StartsWith(SevDestinationLayout.ExpressBusLabel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        remainder = trimmed[SevDestinationLayout.ExpressBusLabel.Length..].TrimStart();
        if (remainder.StartsWith(','))
        {
            remainder = remainder[1..].TrimStart();
        }

        return true;
    }

    public static (string Line1, string Line2) SplitDestinationBody(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (string.Empty, string.Empty);
        }

        var comma = trimmed.IndexOf(',');
        if (comma >= 0)
        {
            var city = trimmed[..comma].Trim();
            var rest = trimmed[(comma + 1)..].Trim();
            return (city + ",", rest);
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
        {
            return (trimmed, string.Empty);
        }

        var mid = words.Length / 2;
        return (
            string.Join(' ', words.Take(mid)),
            string.Join(' ', words.Skip(mid)));
    }

    public string SuggestFileName()
    {
        var linePart = Line.Trim().Replace(' ', '_');
        var stops = Stops
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (stops.Count >= 2)
        {
            var first = ShortStopName(stops[0]);
            var last = ShortStopName(stops[^1]);
            return $"{linePart} {first}-{last}.pdf";
        }

        var dest = Destination.Trim();
        if (dest.Length > 0)
        {
            var shortDest = ShortStopName(dest);
            return $"{linePart} {shortDest}.pdf";
        }

        return $"{linePart} SEV.pdf";
    }

    private static string ShortStopName(string name)
    {
        var trimmed = name.Trim();
        var comma = trimmed.IndexOf(',');
        if (comma >= 0)
        {
            trimmed = trimmed[..comma].Trim();
        }

        if (trimmed.EndsWith(" Hbf", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4].Trim();
        }

        return trimmed
            .Replace('/', '-')
            .Replace('\\', '-')
            .Replace(':', '-');
    }
}
