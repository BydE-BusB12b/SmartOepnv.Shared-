using System.Text.Json;
using SmartOepnv.Core;

namespace SmartOepnv.Core.Session;

/// <summary>
/// Lokale Kopie der aktiven Planer-Sitzung – für zuverlässiges Freigeben beim Beenden.
/// </summary>
internal static class PlanerSessionLocalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string FilePath =>
        Path.Combine(AppPaths.GetLocalDataDirectory("Planer"), "planer_active_session.json");

    public static void Save(string sessionId, string username)
    {
        var payload = new ActiveSession
        {
            SessionId = sessionId,
            Username = username,
            MachineName = Environment.MachineName,
            SavedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch
        {
            // optional
        }
    }

    public static string? TryReadSessionId()
    {
        var payload = Read();
        return string.IsNullOrWhiteSpace(payload?.SessionId) ? null : payload.SessionId.Trim();
    }

    public static bool HasPendingRelease()
    {
        return !string.IsNullOrWhiteSpace(TryReadSessionId());
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // optional
        }
    }

    private static ActiveSession? Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ActiveSession>(File.ReadAllText(FilePath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private sealed class ActiveSession
    {
        public string SessionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public long SavedAtMs { get; set; }
    }
}
