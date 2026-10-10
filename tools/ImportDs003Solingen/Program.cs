using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SmartOepnv.Core.RoutePackage;

var jsonPath = args.Length > 0
    ? args[0]
    : @"c:\Users\hkx18\Downloads\stw_solingen_ds003.json";
var workspace = args.Length > 1
    ? args[1]
    : @"c:\Users\hkx18\AppData\Roaming\Smart-OEPNV\Planer\betriebe\dcb4b3167b2e4d469b2227449e4b6389\workspace";

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var rows = JsonSerializer.Deserialize<List<DestRow>>(File.ReadAllText(jsonPath), jsonOpts)
           ?? throw new InvalidOperationException("Keine Ziele in JSON.");

var entries = new List<string>(rows.Count);
foreach (var row in rows)
{
    var program = OutsideDisplayProgram.CreateDs003(row.Name);
    program.Id = row.Id;
    program.FrontLine1 = OutsideDisplayTelegramFactory.NormalizeZielnummer(row.Zielnummer);
    var line = (row.Line ?? string.Empty).Trim();
    program.Ds001Value = string.IsNullOrEmpty(line) ? "000" : line;
    // Seite z999 Zeile1/Zeile2 → ein Beschreibungsfeld mit „/“
    program.SideLine1 = OutsideDisplayProgram.FormatDs003Beschreibung(row.Side1, row.Side2);
    program.SideLine2 = string.Empty;
    program.IsListEnabled = true;
    entries.Add(program.ToStorageEntry());
}

Console.WriteLine($"Erzeugt: {entries.Count} DS003-Einträge (ID {rows[0].Id}…{rows[^1].Id}).");

foreach (var fileName in new[] { "routes_cache.json", "planer_routes.json" })
{
    var path = Path.Combine(workspace, fileName);
    if (!File.Exists(path))
    {
        Console.WriteLine($"Übersprungen (fehlt): {path}");
        continue;
    }

    var bak = path + $".bak_before_ds003_{DateTime.Now:yyyyMMdd_HHmmss}";
    File.Copy(path, bak, overwrite: false);
    Console.WriteLine($"Backup: {bak}");

    var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
               ?? throw new InvalidOperationException(path);
    var existing = root["outsideDisplays"] as JsonArray ?? [];
    var kept = new JsonArray();
    var removed = 0;
    foreach (var node in existing)
    {
        var entry = node?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(entry))
        {
            continue;
        }

        var program = OutsideDisplayProgram.TryParse(entry);
        if (program?.Protocol == OutsideDisplayProtocolKind.Ds003)
        {
            removed++;
            continue;
        }

        kept.Add(entry);
    }

    foreach (var entry in entries)
    {
        kept.Add(entry);
    }

    root["outsideDisplays"] = kept;
    File.WriteAllText(path, root.ToJsonString());
    Console.WriteLine($"{fileName}: {removed} alte DS003 entfernt, {entries.Count} neu, gesamt {kept.Count}.");
}

Console.WriteLine("Fertig. Bitte Planer neu laden / Speichern+Dropbox-Sync.");

internal sealed class DestRow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("zielnummer")]
    public string Zielnummer { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Front z999 (Liniennummer / Anzeigecode aus der Stw.-Liste).</summary>
    [JsonPropertyName("line")]
    public string Line { get; set; } = "";

    [JsonPropertyName("side1")]
    public string Side1 { get; set; } = "";

    [JsonPropertyName("side2")]
    public string Side2 { get; set; } = "";
}
