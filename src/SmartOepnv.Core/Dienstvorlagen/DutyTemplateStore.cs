using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Dienstvorlagen;

public sealed class DutyTemplateStore
{
    public const string CatalogFileName = "duty_templates.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _catalogPath;

    public DutyTemplateStore(string appSubfolder)
    {
        var workspaceDir = Path.Combine(AppPaths.GetRoamingDataDirectory(appSubfolder), "workspace");
        Directory.CreateDirectory(workspaceDir);
        _catalogPath = Path.Combine(workspaceDir, CatalogFileName);
    }

    public string CatalogFilePath => _catalogPath;

    public IReadOnlyList<DutyTemplate> LoadAll()
    {
        if (!File.Exists(_catalogPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_catalogPath);
            var catalog = JsonSerializer.Deserialize<DutyTemplateCatalog>(json, JsonOptions);
            return catalog?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    public DutyTemplate Save(DutyTemplate template)
    {
        template.UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var items = LoadAll().ToList();
        var index = items.FindIndex(d => d.Id == template.Id);
        if (index >= 0)
        {
            items[index] = template;
        }
        else
        {
            items.Add(template);
        }

        WriteCatalog(items);
        return template;
    }

    public void ReplaceAll(IEnumerable<DutyTemplate> templates)
    {
        WriteCatalog(templates.OrderByDescending(d => d.UpdatedAtUtcMs).ToList());
    }

    public void MergeIncoming(IEnumerable<DutyTemplate> incoming)
    {
        var incomingList = incoming.ToList();
        if (incomingList.Count == 0)
        {
            return;
        }

        var local = LoadAll();
        if (local.Count == 0)
        {
            WriteCatalog(incomingList);
            return;
        }

        var merged = local.ToDictionary(d => d.Id, d => d);
        foreach (var template in incomingList)
        {
            if (string.IsNullOrWhiteSpace(template.Id))
            {
                continue;
            }

            if (!merged.TryGetValue(template.Id, out var existing) ||
                template.UpdatedAtUtcMs >= existing.UpdatedAtUtcMs)
            {
                merged[template.Id] = template;
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

    private void WriteCatalog(IReadOnlyList<DutyTemplate> items)
    {
        var catalog = new DutyTemplateCatalog
        {
            Version = DutyTemplate.FileVersion,
            Items = items.OrderByDescending(d => d.UpdatedAtUtcMs).ToList()
        };

        SafeDataFileStore.WriteAllText(_catalogPath, JsonSerializer.Serialize(catalog, JsonOptions));
    }

    private sealed class DutyTemplateCatalog
    {
        public int Version { get; set; } = DutyTemplate.FileVersion;

        public List<DutyTemplate> Items { get; set; } = [];
    }
}
