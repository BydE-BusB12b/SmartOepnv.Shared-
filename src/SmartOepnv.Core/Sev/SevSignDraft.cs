using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Sev;

/// <summary>Lokal gespeicherte SEV-Schild-Vorlage (Planer).</summary>
public sealed class SevSignDraft
{
    public const int FileVersion = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public long UpdatedAtUtcMs { get; set; }

    public string Line { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    public List<string> Stops { get; set; } = [];

    public List<SevOperatorKind> Operators { get; set; } = [SevOperatorKind.RegioBahn];

    public string? SourceRoute { get; set; }

    public bool ImportRouteReverse { get; set; }

    [JsonIgnore]
    public string Summary => BuildSummary();

    public string BuildSummary()
    {
        var when = UpdatedAtUtcMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs).ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : "–";
        var line = string.IsNullOrWhiteSpace(Line) ? "?" : Line.Trim();
        var dest = string.IsNullOrWhiteSpace(Destination) ? "?" : Destination.Trim();
        return $"{line} → {dest} · {Stops.Count} Haltestelle(n) · {when}";
    }

    public static string SuggestName(string line, string destination)
    {
        var linePart = string.IsNullOrWhiteSpace(line) ? "SEV" : line.Trim();
        var destPart = destination.Trim();
        if (destPart.Length == 0)
        {
            return linePart;
        }

        var comma = destPart.IndexOf(',');
        if (comma >= 0)
        {
            destPart = destPart[..comma].Trim();
        }

        return $"{linePart} {destPart}";
    }
}
