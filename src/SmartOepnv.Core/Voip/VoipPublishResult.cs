namespace SmartOepnv.Core.Voip;

public sealed class VoipPublishResult
{
    public bool DispatchPublished { get; init; }
    public int VehicleCount { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public string? Warning { get; init; }

    public bool IsSuccess => DispatchPublished && string.IsNullOrWhiteSpace(Warning);

    public string Summary =>
        string.IsNullOrWhiteSpace(Warning)
            ? $"VoIP-Konfiguration nach Dropbox geschrieben ({VehicleCount} Fahrzeug(e), {PublishedAt:HH:mm:ss})."
            : Warning;
}
