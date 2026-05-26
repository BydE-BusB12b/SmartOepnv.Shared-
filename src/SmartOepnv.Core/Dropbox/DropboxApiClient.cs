using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

public sealed class DropboxApiClient
{
    private readonly DropboxSettingsStore _store;
    private readonly HttpClient _http = new();

    public DropboxApiClient(DropboxSettingsStore store)
    {
        _store = store;
    }

    public DropboxSettings Settings => _store.Load();

    public string GetRouteFilePath(string? fileName = null)
    {
        var folder = Settings.FolderPath.TrimEnd('/');
        return $"{folder}/{fileName ?? DropboxConstants.RouteFileName}";
    }

    public string BuildAuthorizeUrl()
    {
        var s = Settings;
        var query = new Dictionary<string, string>
        {
            ["client_id"] = s.AppKey,
            ["response_type"] = "code",
            ["token_access_type"] = "offline",
            ["redirect_uri"] = DropboxConstants.OAuthRedirectUri
        };
        var qs = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{DropboxConstants.AuthorizeUrl}?{qs}";
    }

    public async Task ExchangeCodeForTokensAsync(string code, CancellationToken ct = default)
    {
        var s = Settings;
        var body = new Dictionary<string, string>
        {
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["client_id"] = s.AppKey,
            ["client_secret"] = s.AppSecret,
            ["redirect_uri"] = DropboxConstants.OAuthRedirectUri
        };
        using var content = new FormUrlEncodedContent(body);
        using var response = await _http.PostAsync(DropboxConstants.TokenUrl, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Dropbox OAuth fehlgeschlagen: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        s.AccessToken = root.GetProperty("access_token").GetString();
        s.RefreshToken = root.GetProperty("refresh_token").GetString();
        _store.Save(s);
        await RefreshAccountInfoAsync(ct);
    }

    public async Task<bool> RefreshAccessTokenAsync(CancellationToken ct = default)
    {
        var s = Settings;
        if (string.IsNullOrWhiteSpace(s.RefreshToken))
        {
            return false;
        }

        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = s.RefreshToken,
            ["client_id"] = s.AppKey,
            ["client_secret"] = s.AppSecret
        };
        using var content = new FormUrlEncodedContent(body);
        using var response = await _http.PostAsync(DropboxConstants.TokenUrl, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        using var doc = JsonDocument.Parse(json);
        s.AccessToken = doc.RootElement.GetProperty("access_token").GetString();
        if (doc.RootElement.TryGetProperty("refresh_token", out var rt) &&
            !string.IsNullOrWhiteSpace(rt.GetString()))
        {
            s.RefreshToken = rt.GetString();
        }

        _store.Save(s);
        return true;
    }

    public async Task RefreshAccountInfoAsync(CancellationToken ct = default)
    {
        var token = await GetValidAccessTokenAsync(ct);
        using var request = CreateJsonPost(DropboxConstants.CurrentAccountUrl, "null", token);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var s = Settings;
        s.ConnectedAccountName = root.GetProperty("name").GetProperty("display_name").GetString();
        s.ConnectedAccountEmail = root.GetProperty("email").GetString();
        _store.Save(s);
    }

    public async Task<DropboxConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var result = new DropboxConnectionTestResult { FolderPath = Settings.FolderPath.TrimEnd('/') };
        try
        {
            var token = await GetValidAccessTokenAsync(ct);
            var folderPath = result.FolderPath;

            var folderMeta = await GetMetadataAsync(folderPath, token, ct);
            if (folderMeta is null)
            {
                result.Success = false;
                result.Message = $"Ordner nicht gefunden: {folderPath}";
                return result;
            }

            result.FolderExists = true;
            result.FilesInFolder = await ListFileNamesAsync(folderPath, token, ct);

            var routePath = GetRouteFilePath();
            var routeMeta = await GetMetadataAsync(routePath, token, ct);
            if (routeMeta.HasValue)
            {
                result.RouteFileExists = true;
                result.RouteFileServerModified = routeMeta.Value.ServerModified;
                result.RouteFileSizeBytes = routeMeta.Value.Size;
            }

            var s = Settings;
            result.AccountDisplay = string.IsNullOrWhiteSpace(s.ConnectedAccountEmail)
                ? s.ConnectedAccountName ?? "Verbunden"
                : $"{s.ConnectedAccountName} ({s.ConnectedAccountEmail})";

            result.Success = true;
            result.Message = result.RouteFileExists
                ? $"OK – {DropboxConstants.RouteFileName} gefunden ({result.RouteFileSizeBytes / 1024} KB)"
                : $"OK – Ordner erreichbar, {DropboxConstants.RouteFileName} noch nicht vorhanden";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
            return result;
        }
    }

    public async Task<string> DownloadRouteFileAsync(CancellationToken ct = default)
    {
        return await DownloadNamedFileAsync(DropboxConstants.RouteFileName, ct);
    }

    public async Task<string> DownloadNamedFileAsync(string fileName, CancellationToken ct = default)
    {
        return await DownloadNamedFileInternalAsync(fileName, await GetValidAccessTokenAsync(ct), ct);
    }

    public async Task<IReadOnlyList<string>> ListLocationChatFilesAsync(CancellationToken ct = default)
    {
        var folder = Settings.FolderPath.TrimEnd('/');
        var token = await GetValidAccessTokenAsync(ct);
        var names = await ListFileNamesAsync(folder, token, ct);
        return names
            .Where(n => n.StartsWith("location_chat_", StringComparison.OrdinalIgnoreCase) &&
                        n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string> DownloadRouteFileInternalAsync(string token, CancellationToken ct)
    {
        return await DownloadNamedFileInternalAsync(DropboxConstants.RouteFileName, token, ct);
    }

    private async Task<string> DownloadNamedFileInternalAsync(string fileName, string token, CancellationToken ct)
    {
        var folder = Settings.FolderPath.TrimEnd('/');
        var path = $"{folder}/{fileName}";
        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxConstants.DownloadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { path }));
        request.Content = new ByteArrayContent(Array.Empty<byte>());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct))
        {
            return await DownloadNamedFileInternalAsync(fileName, Settings.AccessToken!, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Download fehlgeschlagen ({fileName}): {err}");
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task UploadRouteFileAsync(string jsonContent, CancellationToken ct = default)
    {
        await UploadNamedFileAsync(DropboxConstants.RouteFileName, jsonContent, ct);
    }

    public async Task UploadNamedFileAsync(string fileName, string content, CancellationToken ct = default)
    {
        await UploadNamedFileInternalAsync(fileName, content, await GetValidAccessTokenAsync(ct), ct);
    }

    public async Task TriggerRemoteManualUpdateAsync(string vehiclePhone, CancellationToken ct = default)
    {
        var fileName = RemoteManualUpdateService.BuildCommandFileName(vehiclePhone);
        var payload = RemoteManualUpdateService.BuildPayloadJson();
        await UploadNamedFileAsync(fileName, payload, ct);
    }

    private async Task UploadNamedFileInternalAsync(string fileName, string jsonContent, string token, CancellationToken ct)
    {
        var folder = Settings.FolderPath.TrimEnd('/');
        var path = $"{folder}/{fileName}";
        var apiArg = JsonSerializer.Serialize(new
        {
            path,
            mode = "overwrite",
            autorename = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxConstants.UploadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Dropbox-API-Arg", apiArg);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/octet-stream");

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct))
        {
            await UploadNamedFileInternalAsync(fileName, jsonContent, Settings.AccessToken!, ct);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Upload fehlgeschlagen ({fileName}): {err}");
        }
    }

    private async Task UploadRouteFileInternalAsync(string jsonContent, string token, CancellationToken ct)
    {
        await UploadNamedFileInternalAsync(DropboxConstants.RouteFileName, jsonContent, token, ct);
    }

    public void Disconnect()
    {
        _store.Save(new DropboxSettings
        {
            AppKey = Settings.AppKey,
            AppSecret = Settings.AppSecret,
            FolderPath = Settings.FolderPath
        });
    }

    public void SaveSettings(DropboxSettings settings) => _store.Save(settings);

    private Task<string> GetValidAccessTokenAsync(CancellationToken ct)
    {
        var s = Settings;
        if (string.IsNullOrWhiteSpace(s.AccessToken))
        {
            throw new InvalidOperationException("Nicht mit Dropbox verbunden.");
        }

        return Task.FromResult(s.AccessToken!);
    }

    private async Task<DropboxFileMetadata?> GetMetadataAsync(string path, string token, CancellationToken ct)
    {
        using var request = CreateJsonPost(DropboxConstants.GetMetadataUrl,
            JsonSerializer.Serialize(new { path }), token);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        DateTime? modified = null;
        if (root.TryGetProperty("server_modified", out var sm) &&
            DateTime.TryParse(sm.GetString(), out var dt))
        {
            modified = dt;
        }

        long size = root.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
        return new DropboxFileMetadata(modified, size);
    }

    private async Task<IReadOnlyList<string>> ListFileNamesAsync(string folderPath, string token, CancellationToken ct)
    {
        using var request = CreateJsonPost(DropboxConstants.ListFolderUrl,
            JsonSerializer.Serialize(new { path = folderPath }), token);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<string>();
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var names = new List<string>();
        if (!doc.RootElement.TryGetProperty("entries", out var entries))
        {
            return names;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.TryGetProperty(".tag", out var tag) && tag.GetString() == "file" &&
                entry.TryGetProperty("name", out var name))
            {
                names.Add(name.GetString() ?? string.Empty);
            }
        }

        return names;
    }

    private static HttpRequestMessage CreateJsonPost(string url, string jsonBody, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return request;
    }

    private readonly record struct DropboxFileMetadata(DateTime? ServerModified, long Size);
}

public sealed class DropboxConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public bool FolderExists { get; set; }
    public bool RouteFileExists { get; set; }
    public DateTime? RouteFileServerModified { get; set; }
    public long RouteFileSizeBytes { get; set; }
    public string AccountDisplay { get; set; } = string.Empty;
    public IReadOnlyList<string> FilesInFolder { get; set; } = Array.Empty<string>();
}
