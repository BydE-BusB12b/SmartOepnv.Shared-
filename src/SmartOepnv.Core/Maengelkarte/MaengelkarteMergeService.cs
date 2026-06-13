using System.Text.Json;
using System.Text.Json.Nodes;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.Maengelkarte;

public static class MaengelkarteMergeService
{
    public const string DocumentType = "maengelkarte";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static MaengelkarteDocument EmptyDocument() => new()
    {
        Type = DocumentType,
        UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Entries = []
    };

    public static MaengelkarteDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(json)?.AsObject();
            if (root is null || root["type"]?.GetValue<string>() != DocumentType)
            {
                return null;
            }

            return FromJsonObject(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static MaengelkarteDocument Merge(MaengelkarteDocument? local, MaengelkarteDocument? remote)
    {
        var byId = new Dictionary<string, MaengelkarteEntry>(StringComparer.Ordinal);

        void Ingest(MaengelkarteDocument? doc)
        {
            if (doc?.Entries is null)
            {
                return;
            }

            foreach (var entry in doc.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    continue;
                }

                if (!byId.TryGetValue(entry.Id, out var existing))
                {
                    byId[entry.Id] = CloneEntry(entry);
                    continue;
                }

                var existingUpdated = existing.UpdatedAtMs > 0 ? existing.UpdatedAtMs : existing.CreatedAtMs;
                var incomingUpdated = entry.UpdatedAtMs > 0 ? entry.UpdatedAtMs : entry.CreatedAtMs;
                if (incomingUpdated >= existingUpdated)
                {
                    byId[entry.Id] = CloneEntry(entry);
                }
            }
        }

        Ingest(remote);
        Ingest(local);

        return new MaengelkarteDocument
        {
            Type = DocumentType,
            UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Entries = byId.Values
                .OrderByDescending(e => e.CreatedAtMs)
                .ToList()
        };
    }

    public static string Serialize(MaengelkarteDocument document)
    {
        document.Type = DocumentType;
        document.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static void SetStatus(MaengelkarteEntry entry, string status)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        entry.Status = status;
        entry.UpdatedAtMs = now;
        if (status == MaengelkarteStatus.Resolved)
        {
            entry.ResolvedAtMs = now;
        }
        else
        {
            entry.ResolvedAtMs = null;
        }
    }

    public static int CountNew(MaengelkarteDocument? document) =>
        document?.Entries.Count(e => e.Status == MaengelkarteStatus.New) ?? 0;

    public static string LocalWorkspacePath(string appSubfolder) =>
        Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace", DropboxConstants.MaengelkarteFileName);

    public static MaengelkarteDocument? TryLoadLocal(string appSubfolder)
    {
        var path = LocalWorkspacePath(appSubfolder);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return TryParse(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLocal(string appSubfolder, MaengelkarteDocument document)
    {
        var path = LocalWorkspacePath(appSubfolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafeDataFileStore.WriteAllText(path, Serialize(document));
    }

    private static MaengelkarteDocument FromJsonObject(JsonObject root)
    {
        var doc = new MaengelkarteDocument
        {
            Type = DocumentType,
            UpdatedAtMs = root["updatedAtMs"]?.GetValue<long>() ?? 0
        };

        if (root["entries"] is not JsonArray arr)
        {
            return doc;
        }

        foreach (var node in arr)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            var resolvedMs = obj["resolvedAtMs"]?.GetValue<long>() ?? 0;
            doc.Entries.Add(new MaengelkarteEntry
            {
                Id = obj["id"]?.GetValue<string>() ?? string.Empty,
                Text = obj["text"]?.GetValue<string>() ?? string.Empty,
                CreatedAtMs = obj["createdAtMs"]?.GetValue<long>() ?? 0,
                CreatedAtIso = obj["createdAtIso"]?.GetValue<string>() ?? string.Empty,
                AuthorPersonnel = obj["authorPersonnel"]?.GetValue<string>() ?? string.Empty,
                AuthorName = obj["authorName"]?.GetValue<string>() ?? string.Empty,
                AuthorDevicePhone = obj["authorDevicePhone"]?.GetValue<string>() ?? string.Empty,
                AuthorVehicleName = obj["authorVehicleName"]?.GetValue<string>() ?? string.Empty,
                Status = obj["status"]?.GetValue<string>() ?? MaengelkarteStatus.New,
                UpdatedAtMs = obj["updatedAtMs"]?.GetValue<long>() ??
                              obj["createdAtMs"]?.GetValue<long>() ?? 0,
                ResolvedAtMs = resolvedMs > 0 ? resolvedMs : null
            });
        }

        return doc;
    }

    private static MaengelkarteEntry CloneEntry(MaengelkarteEntry entry) => new()
    {
        Id = entry.Id,
        Text = entry.Text,
        CreatedAtMs = entry.CreatedAtMs,
        CreatedAtIso = entry.CreatedAtIso,
        AuthorPersonnel = entry.AuthorPersonnel,
        AuthorName = entry.AuthorName,
        AuthorDevicePhone = entry.AuthorDevicePhone,
        AuthorVehicleName = entry.AuthorVehicleName,
        Status = entry.Status,
        UpdatedAtMs = entry.UpdatedAtMs,
        ResolvedAtMs = entry.ResolvedAtMs
    };
}
