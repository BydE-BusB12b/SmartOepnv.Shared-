namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Ansagen-Rohdatei in planer_workspace.json: entweder Legacy (Base64 in <see cref="Data"/>)
/// oder externe Referenz (leeres <see cref="Data"/>, Datei liegt unter planer_ansagen_roh/ in Dropbox).
/// </summary>
public sealed class PlanerWorkspaceBinaryPayload
{
    public string Data { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>SHA-256 (hex, Kleinbuchstaben) – bei externem Sync Pflicht.</summary>
    public string? Sha256 { get; set; }

    public bool IsExternalReference =>
        string.IsNullOrWhiteSpace(Data) && Size > 0 && !string.IsNullOrWhiteSpace(Sha256);
}
