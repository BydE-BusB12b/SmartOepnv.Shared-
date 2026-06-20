using System.Text.Json;

namespace SmartOepnv.Core.Updates;

/// <summary>
/// Dropbox-Datei <c>software_versions.json</c> – verfügbare Desktop-Versionen und Setup-Dateiname.
/// </summary>
public sealed class SoftwareVersionsManifest
{
    public SoftwareVersionEntry? Planer { get; init; }
    public SoftwareVersionEntry? Leitstelle { get; init; }

    public SoftwareVersionEntry? ResolveForApp(bool isLeitstelle) =>
        isLeitstelle ? Leitstelle : Planer;

    public static SoftwareVersionsManifest? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new SoftwareVersionsManifest
            {
                Planer = ReadEntry(root, "planer"),
                Leitstelle = ReadEntry(root, "leitstelle")
            };
        }
        catch
        {
            return null;
        }
    }

    private static SoftwareVersionEntry? ReadEntry(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var version = node.TryGetProperty("version", out var v) ? v.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var message = node.TryGetProperty("message", out var m) ? m.GetString()?.Trim() : null;
        var setupFile = ReadSetupFileName(node);
        return new SoftwareVersionEntry(version, message, setupFile);
    }

    private static string? ReadSetupFileName(JsonElement node)
    {
        if (node.TryGetProperty("setupFile", out var setup) &&
            !string.IsNullOrWhiteSpace(setup.GetString()))
        {
            return Path.GetFileName(setup.GetString()!.Trim());
        }

        if (node.TryGetProperty("fileName", out var file) &&
            !string.IsNullOrWhiteSpace(file.GetString()))
        {
            return Path.GetFileName(file.GetString()!.Trim());
        }

        return null;
    }
}

public sealed record SoftwareVersionEntry(string Version, string? Message, string? SetupFileName);
