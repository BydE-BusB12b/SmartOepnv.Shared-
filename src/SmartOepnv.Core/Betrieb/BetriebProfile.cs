using System.Text.Json.Serialization;

namespace SmartOepnv.Core.Betrieb;

public sealed class BetriebProfile
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Anzeigename in der Auswahl, z. B. „smart öpnv“.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Dropbox-Ordnerpfad, z. B. „/smart öpnv“.</summary>
    public string DropboxFolderPath { get; set; } = string.Empty;

    public long CreatedAtUtcMs { get; set; }

    [JsonIgnore]
    public string ListLabel
    {
        get
        {
            var name = DisplayName.Trim();
            var path = DropboxFolderPath.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return string.IsNullOrEmpty(path) ? Id : path;
            }

            return string.IsNullOrEmpty(path) ||
                   string.Equals(name, path.TrimStart('/'), StringComparison.OrdinalIgnoreCase)
                ? name
                : $"{name}  ·  {path}";
        }
    }
}

public sealed class BetriebRegistry
{
    public string ActiveId { get; set; } = string.Empty;

    public List<BetriebProfile> Profiles { get; set; } = [];
}
