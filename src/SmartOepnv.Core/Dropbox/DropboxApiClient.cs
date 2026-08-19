using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartOepnv.Core;

namespace SmartOepnv.Core.Dropbox;

public readonly record struct DropboxNamedFileMetadata(
    long? ServerModifiedUtcMs,
    long SizeBytes,
    string? ContentHash = null);

public sealed class DropboxApiClient
{
    private readonly DropboxSettingsStore _store;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly HttpClient _uploadHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(DropboxConstants.UploadTimeoutMinutes)
    };

    public DropboxApiClient(DropboxSettingsStore store)
    {
        _store = store;
    }

    public DropboxSettings Settings => _store.Load();

    private string ActiveFolderPath => DropboxConstants.NormalizeFolderPath(Settings.FolderPath).TrimEnd('/');

    public string GetRouteFilePath(string? fileName = null)
    {
        return $"{ActiveFolderPath}/{fileName ?? DropboxConstants.RouteFileName}";
    }

    public string GetNamedFilePath(string fileName) => GetRouteFilePath(fileName);

    public async Task<bool> FolderExistsAsync(CancellationToken ct = default)
    {
        var token = await GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        var folder = ActiveFolderPath;
        return await GetMetadataAsync(folder, token, ct).ConfigureAwait(false) is not null;
    }

    public async Task<bool> NamedFileExistsAsync(string fileName, CancellationToken ct = default)
    {
        var token = await GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        return await GetMetadataAsync(GetNamedFilePath(fileName), token, ct).ConfigureAwait(false) is not null;
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
        var result = new DropboxConnectionTestResult { FolderPath = ActiveFolderPath };
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

            if (AppServices.IsInitialized && AppServices.IsPlannerApp)
            {
                var planerValidation = await DropboxPlanerFolderValidator
                    .ValidateAsync(this, ct)
                    .ConfigureAwait(false);
                result.PlanerFolderValid = planerValidation.IsValid;
                result.PlanerWorkspaceFileExists = planerValidation.WorkspaceFileExists;
                result.PlanerSessionFileExists = planerValidation.SessionFileExists;
                result.PlanerFolderValidationMessage = planerValidation.Message;
                if (!planerValidation.IsValid)
                {
                    result.Success = false;
                    result.Message = planerValidation.Message;
                }
            }

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

    public async Task<string?> TryDownloadLeitstelleStandAsync(CancellationToken ct = default)
    {
        try
        {
            return await DownloadNamedFileAsync(DropboxConstants.LeitstelleStandFileName, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> TryDownloadNamedFileAsync(string fileName, CancellationToken ct = default)
    {
        try
        {
            return await DownloadNamedFileAsync(fileName, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDropboxNotFound(ex))
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> TryDownloadNamedBinaryFileAsync(string fileName, CancellationToken ct = default)
    {
        try
        {
            return await DownloadNamedBinaryFileAsync(fileName, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDropboxNotFound(ex))
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]> DownloadNamedBinaryFileAsync(string fileName, CancellationToken ct = default)
    {
        return await DownloadNamedBinaryFileInternalAsync(fileName, await GetValidAccessTokenAsync(ct), ct);
    }

    private static bool IsDropboxNotFound(Exception ex) =>
        ex.Message.Contains("not_found", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("path/not_found", StringComparison.OrdinalIgnoreCase);

    public async Task UploadLeitstelleStandAsync(
        string jsonContent,
        CancellationToken ct = default,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        await UploadNamedFileAsync(DropboxConstants.LeitstelleStandFileName, jsonContent, ct, progress)
            .ConfigureAwait(false);
    }

    public async Task<DropboxNamedFileMetadata?> TryGetNamedFileMetadataAsync(
        string fileName,
        CancellationToken ct = default)
    {
        var token = await GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        var meta = await GetMetadataAsync(GetNamedFilePath(fileName), token, ct).ConfigureAwait(false);
        if (meta is null)
        {
            return null;
        }

        long? modifiedUtcMs = null;
        if (meta.Value.ServerModified is { } modified)
        {
            modifiedUtcMs = new DateTimeOffset(
                DateTime.SpecifyKind(modified, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        return new DropboxNamedFileMetadata(modifiedUtcMs, meta.Value.Size, meta.Value.ContentHash);
    }

    public async Task<string> DownloadNamedFileAsync(
        string fileName,
        CancellationToken ct = default,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        return await DownloadNamedFileInternalAsync(
            fileName,
            await GetValidAccessTokenAsync(ct),
            ct,
            progress).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListLocationChatFilesAsync(CancellationToken ct = default)
    {
        var folder = ActiveFolderPath;
        var token = await GetValidAccessTokenAsync(ct);
        var names = await ListFileNamesAsync(folder, token, ct);
        return names
            .Where(n => n.StartsWith("location_chat_", StringComparison.OrdinalIgnoreCase) &&
                        n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListGpsTraceFilesAsync(CancellationToken ct = default)
    {
        var folder = ActiveFolderPath;
        var token = await GetValidAccessTokenAsync(ct);
        var names = await ListFileNamesAsync(folder, token, ct);
        return names
            .Where(n => n.StartsWith(DropboxConstants.GpsTraceFilePrefix, StringComparison.OrdinalIgnoreCase) &&
                        n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListZblMessageFilesAsync(CancellationToken ct = default)
    {
        var folder = ActiveFolderPath;
        var token = await GetValidAccessTokenAsync(ct);
        var names = await ListFileNamesAsync(folder, token, ct);
        return names
            .Where(n => n.StartsWith("zbl_message(", StringComparison.OrdinalIgnoreCase) &&
                        n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListMailAndSosChatFilesAsync(CancellationToken ct = default)
    {
        var folder = ActiveFolderPath;
        var token = await GetValidAccessTokenAsync(ct);
        var names = await ListFileNamesAsync(folder, token, ct);
        return names
            .Where(n =>
                n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                (n.StartsWith("mailchat(", StringComparison.OrdinalIgnoreCase) ||
                 n.StartsWith("soschat(", StringComparison.OrdinalIgnoreCase) ||
                 n.StartsWith("mailchat_", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListAllFileNamesAsync(CancellationToken ct = default)
    {
        var folder = ActiveFolderPath;
        var token = await GetValidAccessTokenAsync(ct);
        return await ListFileNamesAsync(folder, token, ct);
    }

    public async Task<IReadOnlyList<string>> ListZeitwirtschaftFilesAsync(CancellationToken ct = default)
    {
        var folder = ActiveFolderPath;
        var token = await GetValidAccessTokenAsync(ct);
        var names = await ListFileNamesAsync(folder, token, ct);
        var filtered = names
            .Where(n =>
                n.StartsWith("zeitwirtschaft_", StringComparison.OrdinalIgnoreCase) &&
                n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (filtered.Count > 0)
        {
            return filtered;
        }

        var searched = await SearchZeitwirtschaftFileNamesAsync(folder, token, ct);
        return searched
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<long> UploadZblMessageAsync(string phoneRaw, string message, CancellationToken ct = default) =>
        await ZblMessageService.UploadAsync(this, phoneRaw, message, ct).ConfigureAwait(false);

    private async Task<string> DownloadRouteFileInternalAsync(string token, CancellationToken ct)
    {
        return await DownloadNamedFileInternalAsync(DropboxConstants.RouteFileName, token, ct);
    }

    private async Task<string> DownloadNamedFileInternalAsync(
        string fileName,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var bytes = await DownloadNamedBinaryFileInternalAsync(fileName, token, ct, progress)
            .ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]> DownloadNamedBinaryFileInternalAsync(
        string fileName,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var folder = ActiveFolderPath;
        var path = $"{folder}/{fileName}";
        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxConstants.DownloadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { path }));
        request.Content = new ByteArrayContent(Array.Empty<byte>());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
        {
            return await DownloadNamedBinaryFileInternalAsync(fileName, Settings.AccessToken!, ct, progress)
                .ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Download fehlgeschlagen ({fileName}): {err}");
        }

        var phase = $"{fileName} wird geladen…";
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var etaEstimator = new TransferEtaEstimator();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        long transferred = 0;

        ReportDownloadProgress(progress, phase, transferred, totalBytes, etaEstimator);

        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            transferred += read;
            if (totalBytes <= 0)
            {
                totalBytes = transferred;
            }

            ReportDownloadProgress(progress, phase, transferred, totalBytes, etaEstimator);
        }

        if (totalBytes > 0 && transferred < totalBytes)
        {
            ReportDownloadProgress(progress, phase, totalBytes, totalBytes, etaEstimator);
        }

        return buffer.ToArray();
    }

    private static void ReportDownloadProgress(
        IProgress<DropboxTransferProgress>? progress,
        string phase,
        long transferred,
        long totalBytes,
        TransferEtaEstimator etaEstimator)
    {
        progress?.Report(new DropboxTransferProgress
        {
            Phase = phase,
            BytesTransferred = transferred,
            TotalBytes = totalBytes,
            EstimatedSecondsRemaining = etaEstimator.EstimateSecondsRemaining(transferred, totalBytes)
        });
    }

    public async Task UploadRouteFileAsync(string jsonContent, CancellationToken ct = default)
    {
        await UploadNamedFileAsync(DropboxConstants.RouteFileName, jsonContent, ct);
    }

    public async Task UploadNamedFileAsync(
        string fileName,
        string content,
        CancellationToken ct = default,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        await UploadNamedFileInternalAsync(
            fileName,
            content,
            await GetValidAccessTokenAsync(ct),
            ct,
            progress).ConfigureAwait(false);
    }

    /// <summary>
    /// Lädt eine bereits serialisierte JSON-Datei hoch (speicherschonend – kein zusätzlicher JSON-String im RAM).
    /// </summary>
    public async Task UploadNamedFileFromPathAsync(
        string fileName,
        string filePath,
        CancellationToken ct = default,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Upload-Datei nicht gefunden.", filePath);
        }

        var token = await GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        var length = new FileInfo(filePath).Length;
        if (length == 0)
        {
            throw new InvalidOperationException($"Upload fehlgeschlagen ({fileName}): Datei ist leer.");
        }

        if (length <= DropboxConstants.SimpleUploadMaxBytes)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            await UploadBytesOnceAsync(fileName, bytes, token, ct, progress).ConfigureAwait(false);
            return;
        }

        await UploadFileSessionFromPathOnceAsync(fileName, filePath, token, ct, progress).ConfigureAwait(false);
    }

    public string CombineDropboxPath(string relativePath)
    {
        var relative = relativePath.Trim().TrimStart('/');
        return $"{ActiveFolderPath}/{relative}";
    }

    public async Task UploadRelativeFileFromPathAsync(
        string relativePath,
        string localFilePath,
        CancellationToken ct = default,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Upload-Datei nicht gefunden.", localFilePath);
        }

        var token = await GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        var dropboxPath = CombineDropboxPath(relativePath);
        var displayName = Path.GetFileName(relativePath);
        var length = new FileInfo(localFilePath).Length;
        if (length == 0)
        {
            throw new InvalidOperationException($"Upload fehlgeschlagen ({displayName}): Datei ist leer.");
        }

        if (length <= DropboxConstants.SimpleUploadMaxBytes)
        {
            var bytes = await File.ReadAllBytesAsync(localFilePath, ct).ConfigureAwait(false);
            await UploadBytesAtPathOnceAsync(dropboxPath, displayName, bytes, token, ct, progress)
                .ConfigureAwait(false);
            return;
        }

        await UploadFileSessionAtPathFromPathOnceAsync(
                dropboxPath,
                displayName,
                localFilePath,
                token,
                ct,
                progress)
            .ConfigureAwait(false);
    }

    public async Task DownloadRelativeFileToPathAsync(
        string relativePath,
        string localDestinationPath,
        CancellationToken ct = default,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var token = await GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        var dropboxPath = CombineDropboxPath(relativePath);
        var displayName = Path.GetFileName(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(localDestinationPath)!);
        var tempPath = localDestinationPath + ".tmp";

        try
        {
            await DownloadFileAtPathToPathOnceAsync(dropboxPath, displayName, tempPath, token, ct, progress)
                .ConfigureAwait(false);
            File.Move(tempPath, localDestinationPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // ignore
                }
            }

            throw;
        }
    }

    public async Task<IReadOnlyDictionary<string, long>> ListRelativeFolderFileSizesAsync(
        string relativeFolderPath,
        CancellationToken ct = default)
    {
        var token = await GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        var folderPath = CombineDropboxPath(relativeFolderPath);
        try
        {
            return await ListFolderFileSizesInternalAsync(folderPath, token, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("not_found", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("path/not_found", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<DropboxNamedFileMetadata?> TryGetRelativeFileMetadataAsync(
        string relativePath,
        CancellationToken ct = default)
    {
        var token = await GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        var meta = await GetMetadataAsync(CombineDropboxPath(relativePath), token, ct).ConfigureAwait(false);
        if (meta is null)
        {
            return null;
        }

        long? modifiedUtcMs = null;
        if (meta.Value.ServerModified is { } modified)
        {
            modifiedUtcMs = new DateTimeOffset(
                DateTime.SpecifyKind(modified, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        return new DropboxNamedFileMetadata(modifiedUtcMs, meta.Value.Size, meta.Value.ContentHash);
    }

    public async Task UploadNamedBinaryFileAsync(string fileName, byte[] content, CancellationToken ct = default)
    {
        await UploadNamedBinaryInternalAsync(fileName, content, await GetValidAccessTokenAsync(ct), ct);
    }

    public async Task<long> TriggerRemoteManualUpdateAsync(string vehiclePhone, CancellationToken ct = default) =>
        await RemoteManualUpdateService.UploadAsync(this, vehiclePhone, ct).ConfigureAwait(false);

    private async Task UploadNamedFileInternalAsync(
        string fileName,
        string jsonContent,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= DropboxConstants.UploadMaxAttempts; attempt++)
        {
            try
            {
                await UploadNamedFileInternalOnceAsync(fileName, jsonContent, token, ct, progress)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsRetriableUploadError(ex) && attempt < DropboxConstants.UploadMaxAttempts)
            {
                lastError = ex;
                await Task.Delay(GetUploadRetryDelay(ex, attempt), ct).ConfigureAwait(false);

                if (await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
                {
                    token = Settings.AccessToken!;
                }
            }
        }

        if (lastError is not null)
        {
            throw lastError;
        }
    }

    private async Task UploadNamedFileInternalOnceAsync(
        string fileName,
        string jsonContent,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var body = Encoding.UTF8.GetBytes(jsonContent);
        await UploadBytesOnceAsync(fileName, body, token, ct, progress).ConfigureAwait(false);
    }

    private async Task UploadBytesOnceAsync(
        string fileName,
        byte[] content,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        if (content.Length > DropboxConstants.SimpleUploadMaxBytes)
        {
            await UploadBytesSessionOnceAsync(fileName, content, token, ct, progress).ConfigureAwait(false);
            return;
        }

        await UploadBytesSimpleOnceAsync(fileName, content, token, ct, progress).ConfigureAwait(false);
    }

    private async Task UploadBytesSimpleOnceAsync(
        string fileName,
        byte[] content,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var folder = ActiveFolderPath;
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
        request.Content = new ProgressReportingHttpContent(
            content,
            $"{fileName} wird hochgeladen…",
            progress);

        using var response = await _uploadHttp.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
        {
            await UploadBytesSimpleOnceAsync(fileName, content, Settings.AccessToken!, ct, progress)
                .ConfigureAwait(false);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Upload fehlgeschlagen ({fileName}): {err}");
        }
    }

    private async Task UploadBytesSessionOnceAsync(
        string fileName,
        byte[] content,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var folder = ActiveFolderPath;
        var path = $"{folder}/{fileName}";
        var phase = $"{fileName} wird hochgeladen (große Datei)…";
        var total = (long)content.Length;
        var chunkSize = DropboxConstants.UploadSessionChunkBytes;
        var etaEstimator = new TransferEtaEstimator();
        string? sessionId = null;
        long offset = 0;

        ReportUploadProgress(progress, phase, offset, total, etaEstimator);

        while (offset < total)
        {
            var size = (int)Math.Min(chunkSize, total - offset);
            var chunk = content.AsMemory((int)offset, size);
            var isFirst = offset == 0;
            var isLast = offset + size >= total;

            if (isFirst)
            {
                sessionId = await UploadSessionStartAsync(chunk, token, ct).ConfigureAwait(false);
            }
            else if (!isLast)
            {
                await UploadSessionAppendAsync(sessionId!, offset, chunk, token, ct).ConfigureAwait(false);
            }
            else
            {
                await UploadSessionFinishAsync(sessionId!, offset, chunk, path, token, ct).ConfigureAwait(false);
            }

            offset += size;
            ReportUploadProgress(progress, phase, offset, total, etaEstimator);
        }

        if (total == 0)
        {
            throw new InvalidOperationException($"Upload fehlgeschlagen ({fileName}): Datei ist leer.");
        }
    }

    private async Task UploadFileSessionFromPathOnceAsync(
        string fileName,
        string filePath,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var folder = ActiveFolderPath;
        var path = $"{folder}/{fileName}";
        var phase = $"{fileName} wird hochgeladen (große Datei)…";
        var total = new FileInfo(filePath).Length;
        var chunkSize = DropboxConstants.UploadSessionChunkBytes;
        var etaEstimator = new TransferEtaEstimator();
        string? sessionId = null;
        long offset = 0;

        ReportUploadProgress(progress, phase, offset, total, etaEstimator);

        await using var stream = File.OpenRead(filePath);
        var buffer = new byte[chunkSize];

        while (offset < total)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, chunkSize), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            var chunk = buffer.AsMemory(0, read);
            var isFirst = offset == 0;
            var isLast = offset + read >= total;

            if (isFirst)
            {
                sessionId = await UploadSessionStartAsync(chunk, token, ct).ConfigureAwait(false);
            }
            else if (!isLast)
            {
                await UploadSessionAppendAsync(sessionId!, offset, chunk, token, ct).ConfigureAwait(false);
            }
            else
            {
                await UploadSessionFinishAsync(sessionId!, offset, chunk, path, token, ct).ConfigureAwait(false);
            }

            offset += read;
            ReportUploadProgress(progress, phase, offset, total, etaEstimator);
        }

        if (offset != total)
        {
            throw new InvalidOperationException($"Upload fehlgeschlagen ({fileName}): unvollständig gelesen ({offset}/{total} Bytes).");
        }
    }

    private async Task UploadBytesAtPathOnceAsync(
        string dropboxPath,
        string displayName,
        byte[] content,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        if (content.Length > DropboxConstants.SimpleUploadMaxBytes)
        {
            await UploadBytesSessionAtPathOnceAsync(dropboxPath, displayName, content, token, ct, progress)
                .ConfigureAwait(false);
            return;
        }

        var apiArg = JsonSerializer.Serialize(new
        {
            path = dropboxPath,
            mode = "overwrite",
            autorename = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxConstants.UploadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Dropbox-API-Arg", apiArg);
        request.Content = new ProgressReportingHttpContent(
            content,
            $"{displayName} wird hochgeladen…",
            progress);

        using var response = await _uploadHttp.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
        {
            await UploadBytesAtPathOnceAsync(dropboxPath, displayName, content, Settings.AccessToken!, ct, progress)
                .ConfigureAwait(false);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Upload fehlgeschlagen ({displayName}): {err}");
        }
    }

    private async Task UploadBytesSessionAtPathOnceAsync(
        string dropboxPath,
        string displayName,
        byte[] content,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var phase = $"{displayName} wird hochgeladen (große Datei)…";
        var total = (long)content.Length;
        var chunkSize = DropboxConstants.UploadSessionChunkBytes;
        var etaEstimator = new TransferEtaEstimator();
        string? sessionId = null;
        long offset = 0;

        ReportUploadProgress(progress, phase, offset, total, etaEstimator);

        while (offset < total)
        {
            var size = (int)Math.Min(chunkSize, total - offset);
            var chunk = content.AsMemory((int)offset, size);
            var isFirst = offset == 0;
            var isLast = offset + size >= total;

            if (isFirst)
            {
                sessionId = await UploadSessionStartAsync(chunk, token, ct).ConfigureAwait(false);
            }
            else if (!isLast)
            {
                await UploadSessionAppendAsync(sessionId!, offset, chunk, token, ct).ConfigureAwait(false);
            }
            else
            {
                await UploadSessionFinishAsync(sessionId!, offset, chunk, dropboxPath, token, ct).ConfigureAwait(false);
            }

            offset += size;
            ReportUploadProgress(progress, phase, offset, total, etaEstimator);
        }
    }

    private async Task UploadFileSessionAtPathFromPathOnceAsync(
        string dropboxPath,
        string displayName,
        string filePath,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        var phase = $"{displayName} wird hochgeladen (große Datei)…";
        var total = new FileInfo(filePath).Length;
        var chunkSize = DropboxConstants.UploadSessionChunkBytes;
        var etaEstimator = new TransferEtaEstimator();
        string? sessionId = null;
        long offset = 0;

        ReportUploadProgress(progress, phase, offset, total, etaEstimator);

        await using var stream = File.OpenRead(filePath);
        var buffer = new byte[chunkSize];

        while (offset < total)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, chunkSize), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            var chunk = buffer.AsMemory(0, read);
            var isFirst = offset == 0;
            var isLast = offset + read >= total;

            if (isFirst)
            {
                sessionId = await UploadSessionStartAsync(chunk, token, ct).ConfigureAwait(false);
            }
            else if (!isLast)
            {
                await UploadSessionAppendAsync(sessionId!, offset, chunk, token, ct).ConfigureAwait(false);
            }
            else
            {
                await UploadSessionFinishAsync(sessionId!, offset, chunk, dropboxPath, token, ct).ConfigureAwait(false);
            }

            offset += read;
            ReportUploadProgress(progress, phase, offset, total, etaEstimator);
        }

        if (offset != total)
        {
            throw new InvalidOperationException(
                $"Upload fehlgeschlagen ({displayName}): unvollständig gelesen ({offset}/{total} Bytes).");
        }
    }

    private async Task DownloadFileAtPathToPathOnceAsync(
        string dropboxPath,
        string displayName,
        string localDestinationPath,
        string token,
        CancellationToken ct,
        IProgress<DropboxTransferProgress>? progress = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxConstants.DownloadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { path = dropboxPath }));
        request.Content = new ByteArrayContent(Array.Empty<byte>());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
        {
            await DownloadFileAtPathToPathOnceAsync(
                    dropboxPath,
                    displayName,
                    localDestinationPath,
                    Settings.AccessToken!,
                    ct,
                    progress)
                .ConfigureAwait(false);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Download fehlgeschlagen ({displayName}): {err}");
        }

        var phase = $"{displayName} wird geladen…";
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var etaEstimator = new TransferEtaEstimator();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(localDestinationPath);
        var chunk = new byte[81_920];
        long transferred = 0;

        ReportDownloadProgress(progress, phase, transferred, totalBytes, etaEstimator);

        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            transferred += read;
            if (totalBytes <= 0)
            {
                totalBytes = transferred;
            }

            ReportDownloadProgress(progress, phase, transferred, totalBytes, etaEstimator);
        }

        if (totalBytes > 0 && transferred < totalBytes)
        {
            ReportDownloadProgress(progress, phase, totalBytes, totalBytes, etaEstimator);
        }
    }

    private async Task<IReadOnlyDictionary<string, long>> ListFolderFileSizesInternalAsync(
        string folderPath,
        string token,
        CancellationToken ct)
    {
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;

        do
        {
            using var request = cursor is null
                ? CreateJsonPost(
                    DropboxConstants.ListFolderUrl,
                    JsonSerializer.Serialize(new { path = folderPath }),
                    token)
                : CreateJsonPost(
                    DropboxConstants.ListFolderContinueUrl,
                    JsonSerializer.Serialize(new { cursor }),
                    token);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Dropbox-Zugriff abgelaufen – Token erneuern.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Ordnerliste fehlgeschlagen ({folderPath}): {err}");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("entries", out var entries))
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.TryGetProperty(".tag", out var tag) && tag.GetString() == "file" &&
                        entry.TryGetProperty("name", out var nameEl) &&
                        entry.TryGetProperty("size", out var sizeEl))
                    {
                        sizes[nameEl.GetString() ?? string.Empty] = sizeEl.GetInt64();
                    }
                }
            }

            cursor = doc.RootElement.TryGetProperty("has_more", out var hasMore) &&
                     hasMore.GetBoolean() &&
                     doc.RootElement.TryGetProperty("cursor", out var cursorEl)
                ? cursorEl.GetString()
                : null;
        } while (!string.IsNullOrEmpty(cursor));

        return sizes;
    }

    private async Task<string> UploadSessionStartAsync(
        ReadOnlyMemory<byte> chunk,
        string token,
        CancellationToken ct)
    {
        var apiArg = JsonSerializer.Serialize(new
        {
            close = false,
            session_type = new Dictionary<string, object> { [".tag"] = "sequential" }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxConstants.UploadSessionStartUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Dropbox-API-Arg", apiArg);
        request.Content = new ByteArrayContent(chunk.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _uploadHttp.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
        {
            return await UploadSessionStartAsync(chunk, Settings.AccessToken!, ct).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Upload-Session-Start fehlgeschlagen: {err}");
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var sessionId = doc.RootElement.GetProperty("session_id").GetString();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Upload-Session-Start: session_id fehlt.");
        }

        return sessionId;
    }

    private async Task UploadSessionAppendAsync(
        string sessionId,
        long offset,
        ReadOnlyMemory<byte> chunk,
        string token,
        CancellationToken ct)
    {
        var apiArg = JsonSerializer.Serialize(new
        {
            cursor = new { session_id = sessionId, offset },
            close = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxConstants.UploadSessionAppendUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Dropbox-API-Arg", apiArg);
        request.Content = new ByteArrayContent(chunk.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _uploadHttp.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
        {
            await UploadSessionAppendAsync(sessionId, offset, chunk, Settings.AccessToken!, ct)
                .ConfigureAwait(false);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Upload-Session-Append fehlgeschlagen: {err}");
        }
    }

    private async Task UploadSessionFinishAsync(
        string sessionId,
        long offset,
        ReadOnlyMemory<byte> chunk,
        string path,
        string token,
        CancellationToken ct)
    {
        var apiArg = JsonSerializer.Serialize(new
        {
            cursor = new { session_id = sessionId, offset },
            commit = new
            {
                path,
                mode = "overwrite",
                autorename = false,
                mute = false
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, DropboxConstants.UploadSessionFinishUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Dropbox-API-Arg", apiArg);
        request.Content = new ByteArrayContent(chunk.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _uploadHttp.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
        {
            await UploadSessionFinishAsync(sessionId, offset, chunk, path, Settings.AccessToken!, ct)
                .ConfigureAwait(false);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Upload-Session-Finish fehlgeschlagen: {err}");
        }
    }

    private static void ReportUploadProgress(
        IProgress<DropboxTransferProgress>? progress,
        string phase,
        long transferred,
        long totalBytes,
        TransferEtaEstimator etaEstimator)
    {
        progress?.Report(new DropboxTransferProgress
        {
            Phase = phase,
            BytesTransferred = transferred,
            TotalBytes = totalBytes,
            EstimatedSecondsRemaining = etaEstimator.EstimateSecondsRemaining(transferred, totalBytes)
        });
    }

    private static bool IsRetriableUploadError(Exception ex) =>
        !IsPermanentUploadError(ex) &&
        (ex is TaskCanceledException or HttpRequestException or IOException
         || ex.Message.Contains("too_many_write_operations", StringComparison.OrdinalIgnoreCase)
         || ex.Message.Contains("too_many_requests", StringComparison.OrdinalIgnoreCase)
         || ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
         || ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
         || ex.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
         || ex.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));

    private static bool IsPermanentUploadError(Exception ex) =>
        ex.Message.Contains("payload_too_large", StringComparison.OrdinalIgnoreCase);

    private static TimeSpan GetUploadRetryDelay(Exception ex, int attempt)
    {
        var retryAfterSeconds = TryParseDropboxRetryAfterSeconds(ex.Message);
        if (retryAfterSeconds is int seconds)
        {
            return TimeSpan.FromSeconds(Math.Max(seconds + 1, 2));
        }

        return TimeSpan.FromSeconds(DropboxConstants.UploadRetryDelaySeconds * attempt);
    }

    private static int? TryParseDropboxRetryAfterSeconds(string message)
    {
        var jsonStart = message.IndexOf('{');
        if (jsonStart < 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(message[jsonStart..]);
            if (!doc.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.TryGetProperty("retry_after", out var retryAfter) &&
                retryAfter.TryGetInt32(out var seconds))
            {
                return seconds;
            }
        }
        catch (JsonException)
        {
            // ignore malformed error payloads
        }

        return null;
    }

    private async Task UploadNamedBinaryInternalAsync(string fileName, byte[] content, string token, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= DropboxConstants.UploadMaxAttempts; attempt++)
        {
            try
            {
                await UploadNamedBinaryInternalOnceAsync(fileName, content, token, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsRetriableUploadError(ex) && attempt < DropboxConstants.UploadMaxAttempts)
            {
                lastError = ex;
                await Task.Delay(GetUploadRetryDelay(ex, attempt), ct).ConfigureAwait(false);

                if (await RefreshAccessTokenAsync(ct).ConfigureAwait(false))
                {
                    token = Settings.AccessToken!;
                }
            }
        }

        if (lastError is not null)
        {
            throw lastError;
        }
    }

    private async Task UploadNamedBinaryInternalOnceAsync(
        string fileName,
        byte[] content,
        string token,
        CancellationToken ct)
    {
        await UploadBytesOnceAsync(fileName, content, token, ct).ConfigureAwait(false);
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
            FolderPath = ActiveFolderPath
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
        string? contentHash = null;
        if (root.TryGetProperty("content_hash", out var ch) &&
            ch.ValueKind == JsonValueKind.String)
        {
            contentHash = ch.GetString();
        }

        return new DropboxFileMetadata(modified, size, contentHash);
    }

    private async Task<IReadOnlyList<string>> ListFileNamesAsync(string folderPath, string token, CancellationToken ct)
    {
        try
        {
            return await ListFileNamesInternalAsync(folderPath, token, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("abgelaufen", StringComparison.OrdinalIgnoreCase))
        {
            if (!await RefreshAccessTokenAsync(ct))
            {
                throw;
            }

            return await ListFileNamesInternalAsync(folderPath, Settings.AccessToken!, ct);
        }
    }

    private async Task<IReadOnlyList<string>> ListFileNamesInternalAsync(
        string folderPath,
        string token,
        CancellationToken ct)
    {
        var names = new List<string>();
        string? cursor = null;

        do
        {
            using var request = cursor is null
                ? CreateJsonPost(
                    DropboxConstants.ListFolderUrl,
                    JsonSerializer.Serialize(new { path = folderPath }),
                    token)
                : CreateJsonPost(
                    DropboxConstants.ListFolderContinueUrl,
                    JsonSerializer.Serialize(new { cursor }),
                    token);

            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Dropbox-Zugriff abgelaufen – Token erneuern.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Ordnerliste fehlgeschlagen ({folderPath}): {err}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("entries", out var entries))
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.TryGetProperty(".tag", out var tag) && tag.GetString() == "file" &&
                        entry.TryGetProperty("name", out var name))
                    {
                        names.Add(name.GetString() ?? string.Empty);
                    }
                }
            }

            cursor = doc.RootElement.TryGetProperty("has_more", out var hasMore) &&
                     hasMore.GetBoolean() &&
                     doc.RootElement.TryGetProperty("cursor", out var cursorEl)
                ? cursorEl.GetString()
                : null;
        } while (!string.IsNullOrEmpty(cursor));

        return names;
    }

    private async Task<IReadOnlyList<string>> SearchZeitwirtschaftFileNamesAsync(
        string folderPath,
        string token,
        CancellationToken ct)
    {
        using var request = CreateJsonPost(
            DropboxConstants.SearchUrl,
            JsonSerializer.Serialize(new
            {
                query = "zeitwirtschaft_",
                options = new
                {
                    path = folderPath,
                    filename_only = true,
                    max_results = 200
                }
            }),
            token);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<string>();
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("matches", out var matches))
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var match in matches.EnumerateArray())
        {
            if (!match.TryGetProperty("metadata", out var metadataWrapper) ||
                !metadataWrapper.TryGetProperty("metadata", out var metadata) ||
                !metadata.TryGetProperty("name", out var nameEl))
            {
                continue;
            }

            var name = nameEl.GetString() ?? string.Empty;
            if (name.StartsWith("zeitwirtschaft_", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(name);
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

    private readonly record struct DropboxFileMetadata(DateTime? ServerModified, long Size, string? ContentHash = null);
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
    public bool PlanerFolderValid { get; set; }
    public bool PlanerWorkspaceFileExists { get; set; }
    public bool PlanerSessionFileExists { get; set; }
    public string PlanerFolderValidationMessage { get; set; } = string.Empty;
}
