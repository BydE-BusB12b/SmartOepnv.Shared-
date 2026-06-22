namespace SmartOepnv.Core.RoutePackage;

/// <summary>Binärdaten (Base64) für planer_workspace.json – z. B. Ansagen-Rohdateien.</summary>
public sealed class PlanerWorkspaceBinaryPayload
{
    public string Data { get; set; } = string.Empty;

    public long Size { get; set; }
}
