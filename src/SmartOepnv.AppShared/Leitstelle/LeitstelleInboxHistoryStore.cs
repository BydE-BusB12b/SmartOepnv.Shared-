using System.IO;
using System.Text.Json;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.Leitstelle;

/// <summary>
/// Lokaler Verlauf aller gesehenen MailChat/SOS-Nachrichten (Schlüssel: Dateiname + JSON-timestamp).
/// Dropbox überschreibt pro Nummer nur eine Datei – der Verlauf bleibt hier erhalten.
/// </summary>
public sealed class LeitstelleInboxHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private LeitstelleInboxHistoryFile _data = new();

    public LeitstelleInboxHistoryStore()
    {
        var dir = Path.Combine(AppPaths.GetRoamingDataDirectory("Leitstelle"), "inbox");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "message_history.json");
        Load();
    }

    public IReadOnlyList<LeitstelleInboxHistoryRecord> GetActiveRecords() =>
        _data.Records
            .Where(r => !_data.DismissedKeys.Contains(r.DedupeKey))
            .OrderByDescending(r => r.TimestampEpochMs)
            .ToList();

    public bool Contains(string dedupeKey) =>
        _data.Records.Any(r => string.Equals(r.DedupeKey, dedupeKey, StringComparison.Ordinal));

    public bool IsDismissed(string dedupeKey) =>
        _data.DismissedKeys.Contains(dedupeKey);

    public LeitstelleInboxHistoryRecord Add(LeitstelleInboxHistoryRecord record)
    {
        if (IsDismissed(record.DedupeKey) || Contains(record.DedupeKey))
        {
            return record;
        }

        _data.Records.Add(record);
        TrimIfNeeded();
        Save();
        return record;
    }

    public void UpdateUnread(string dedupeKey, bool isUnread)
    {
        var rec = _data.Records.FirstOrDefault(r =>
            string.Equals(r.DedupeKey, dedupeKey, StringComparison.Ordinal));
        if (rec is null)
        {
            return;
        }

        rec.IsUnread = isUnread;
        Save();
    }

    public void MarkAllMailRead()
    {
        foreach (var rec in _data.Records.Where(r => !r.IsSos))
        {
            rec.IsUnread = false;
        }

        Save();
    }

    public void Dismiss(string dedupeKey)
    {
        _data.Records.RemoveAll(r => string.Equals(r.DedupeKey, dedupeKey, StringComparison.Ordinal));
        _data.DismissedKeys.Add(dedupeKey);
        TrimDismissedKeys();
        Save();
    }

    private void TrimIfNeeded()
    {
        const int maxRecords = 500;
        if (_data.Records.Count <= maxRecords)
        {
            return;
        }

        _data.Records = _data.Records
            .OrderByDescending(r => r.TimestampEpochMs)
            .Take(maxRecords)
            .ToList();
    }

    private void TrimDismissedKeys()
    {
        const int maxDismissed = 2000;
        if (_data.DismissedKeys.Count <= maxDismissed)
        {
            return;
        }

        _data.DismissedKeys = _data.DismissedKeys
            .OrderBy(x => x, StringComparer.Ordinal)
            .TakeLast(maxDismissed)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            _data = new LeitstelleInboxHistoryFile();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            _data = JsonSerializer.Deserialize<LeitstelleInboxHistoryFile>(json, JsonOptions) ?? new();
        }
        catch
        {
            _data = new LeitstelleInboxHistoryFile();
        }
    }

    private void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOptions));
    }

    private sealed class LeitstelleInboxHistoryFile
    {
        public List<LeitstelleInboxHistoryRecord> Records { get; set; } = [];

        public HashSet<string> DismissedKeys { get; set; } = new(StringComparer.Ordinal);
    }
}

public sealed class LeitstelleInboxHistoryRecord
{
    public string DedupeKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool IsSos { get; set; }

    public bool IsSprechwunsch { get; set; }

    public bool IsUnread { get; set; }

    public long TimestampEpochMs { get; set; }

    public string PhoneNormalized { get; set; } = string.Empty;

    public string VehicleName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
