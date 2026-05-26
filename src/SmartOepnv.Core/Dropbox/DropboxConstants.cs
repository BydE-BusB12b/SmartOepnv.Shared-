namespace SmartOepnv.Core.Dropbox;

public static class DropboxConstants
{
    public const string DefaultAppKey = "zl4jd0tyuqjwkxp";
    public const string DefaultAppSecret = "lzer62tixqyzpc3";
    public const string DefaultFolderPath = "/App/Smart ÖPNV";
    public const string RouteFileName = "routes_export.json";
    public const string OAuthRedirectUri = "https://www.dropbox.com";

    public const string AuthorizeUrl = "https://www.dropbox.com/oauth2/authorize";
    public const string TokenUrl = "https://api.dropbox.com/oauth2/token";
    public const string UploadUrl = "https://content.dropboxapi.com/2/files/upload";
    public const string DownloadUrl = "https://content.dropboxapi.com/2/files/download";
    public const string ListFolderUrl = "https://api.dropboxapi.com/2/files/list_folder";
    public const string GetMetadataUrl = "https://api.dropboxapi.com/2/files/get_metadata";
    public const string CurrentAccountUrl = "https://api.dropboxapi.com/2/users/get_current_account";
}
