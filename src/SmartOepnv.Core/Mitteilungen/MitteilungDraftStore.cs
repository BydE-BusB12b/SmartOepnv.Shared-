using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Mitteilungen;

public sealed class MitteilungDraftStore
{
    public const string CatalogFileName = "mitteilung_drafts.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _catalogPath;

    public MitteilungDraftStore(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _catalogPath = Path.Combine(workspaceDir, CatalogFileName);
    }

    public string CatalogFilePath => _catalogPath;

    public IReadOnlyList<MitteilungDraft> LoadAll()
    {
        if (!File.Exists(_catalogPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_catalogPath);
            var catalog = JsonSerializer.Deserialize<MitteilungDraftCatalog>(json, JsonOptions);
            return catalog?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    public MitteilungDraft Save(MitteilungDraft draft)
    {
        draft.UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var items = LoadAll().ToList();
        var index = items.FindIndex(d => d.Id == draft.Id);
        if (index >= 0)
        {
            items[index] = draft;
        }
        else
        {
            items.Add(draft);
        }

        WriteCatalog(items);
        return draft;
    }

    public void ReplaceAll(IEnumerable<MitteilungDraft> drafts) =>
        WriteCatalog(drafts.OrderByDescending(d => d.UpdatedAtUtcMs).ToList());

    public void MergeIncoming(IEnumerable<MitteilungDraft> incoming)
    {
        var incomingList = incoming.ToList();
        if (incomingList.Count == 0)
        {
            return;
        }

        var merged = LoadAll().ToDictionary(d => d.Id, d => d);
        foreach (var draft in incomingList)
        {
            if (string.IsNullOrWhiteSpace(draft.Id))
            {
                continue;
            }

            if (!merged.TryGetValue(draft.Id, out var existing) ||
                draft.UpdatedAtUtcMs >= existing.UpdatedAtUtcMs)
            {
                merged[draft.Id] = draft;
            }
        }

        WriteCatalog(merged.Values.OrderByDescending(d => d.UpdatedAtUtcMs).ToList());
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var items = LoadAll().ToList();
        var removed = items.RemoveAll(d => d.Id == id);
        if (removed == 0)
        {
            return false;
        }

        WriteCatalog(items);
        return true;
    }

    private void WriteCatalog(IReadOnlyList<MitteilungDraft> items)
    {
        var catalog = new MitteilungDraftCatalog
        {
            Version = MitteilungDraft.FileVersion,
            Items = items.OrderByDescending(d => d.UpdatedAtUtcMs).ToList()
        };

        SafeDataFileStore.WriteAllText(_catalogPath, JsonSerializer.Serialize(catalog, JsonOptions));
    }

    private sealed class MitteilungDraftCatalog
    {
        public int Version { get; set; } = MitteilungDraft.FileVersion;

        public List<MitteilungDraft> Items { get; set; } = [];
    }
}
