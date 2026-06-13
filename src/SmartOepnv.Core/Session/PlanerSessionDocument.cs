namespace SmartOepnv.Core.Session;

public static class PlanerSessionStatus
{
    public const string Available = "available";
    public const string InUse = "in_use";
}

public sealed class PlanerSessionDocument
{
    public string Version { get; set; } = "1.0";
    public string Status { get; set; } = PlanerSessionStatus.Available;
    public string Username { get; set; } = string.Empty;
    public long UpdatedAtMs { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
}

public enum PlanerSessionAvailability
{
    Available,
    InUseByOther,
    DropboxUnavailable
}

public sealed class PlanerSessionLoginResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static PlanerSessionLoginResult Ok() => new() { Success = true, Message = "Angemeldet." };

    public static PlanerSessionLoginResult Fail(string message) => new() { Success = false, Message = message };
}
