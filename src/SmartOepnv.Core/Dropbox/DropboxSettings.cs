namespace SmartOepnv.Core.Dropbox;

public sealed class DropboxSettings
{
    public string AppKey { get; set; } = DropboxConstants.DefaultAppKey;
    public string AppSecret { get; set; } = DropboxConstants.DefaultAppSecret;
    public string FolderPath { get; set; } = DropboxConstants.DefaultFolderPath;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? ConnectedAccountName { get; set; }
    public string? ConnectedAccountEmail { get; set; }

    public bool IsConnected =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(RefreshToken);
}
