using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Mitteilungen;

/// <summary>Lokal gespeicherte Mitteilungs-Vorlage (Planer).</summary>
public sealed class MitteilungDraft
{
    public const int FileVersion = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public long UpdatedAtUtcMs { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string ValidFrom { get; set; } = string.Empty;

    public string ValidTo { get; set; } = string.Empty;

    public bool UntilRevoked { get; set; }

    public bool ShowSmartOepnvLogo { get; set; } = true;

    public string? CompanyLogoId { get; set; }

    public string SignerNameAndDate { get; set; } = string.Empty;

    public string? SignatureId { get; set; }

    [JsonIgnore]
    public string Summary => BuildSummary();

    public string BuildSummary()
    {
        var when = UpdatedAtUtcMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs).ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : "–";
        var headline = string.IsNullOrWhiteSpace(Title) ? "(Ohne Überschrift)" : Title.Trim();
        if (headline.Length > 48)
        {
            headline = headline[..45] + "…";
        }

        return $"{headline} · {when}";
    }

    public static string SuggestName(string title)
    {
        var t = title.Trim();
        return string.IsNullOrWhiteSpace(t) ? "Mitteilung" : t;
    }
}
