namespace SmartOepnv.Core.Maengelkarte;

public static class MaengelkarteStatus
{
    public const string New = "new";
    public const string InProgress = "in_progress";
    public const string Resolved = "resolved";

    public static string Label(string status) => status switch
    {
        InProgress => "In Bearbeitung",
        Resolved => "Erledigt",
        _ => "Neu"
    };
}

public sealed class MaengelkarteEntry
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public long CreatedAtMs { get; set; }
    public string CreatedAtIso { get; set; } = string.Empty;
    public string AuthorPersonnel { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorDevicePhone { get; set; } = string.Empty;
    public string AuthorVehicleName { get; set; } = string.Empty;
    public string Status { get; set; } = MaengelkarteStatus.New;
    public long UpdatedAtMs { get; set; }
    public long? ResolvedAtMs { get; set; }

    public string AuthorDisplay =>
        string.IsNullOrWhiteSpace(AuthorName)
            ? (string.IsNullOrWhiteSpace(AuthorPersonnel) ? "?" : AuthorPersonnel)
            : AuthorName;

    public string CreatedDisplay =>
        CreatedAtMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).LocalDateTime.ToString("dd.MM.yyyy HH:mm")
            : CreatedAtIso.Replace('T', ' ').TrimEnd('Z').Trim();

    public string StatusLabel => MaengelkarteStatus.Label(Status);

    [System.Text.Json.Serialization.JsonIgnore]
    public string VehicleDisplay { get; set; } = string.Empty;
}

public sealed class MaengelkarteDocument
{
    public string Type { get; set; } = MaengelkarteMergeService.DocumentType;
    public long UpdatedAtMs { get; set; }
    public List<MaengelkarteEntry> Entries { get; set; } = [];
}
