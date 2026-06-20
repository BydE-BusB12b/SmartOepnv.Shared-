using System.Text.Json;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Session;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Legt fehlende Planer-Markerdateien im Dropbox-Ordner an (Ersteinrichtung / neue Installation).
/// </summary>
public static class PlanerDropboxFolderBootstrap
{
    public sealed class BootstrapResult
    {
        public bool Success { get; init; }
        public bool CreatedWorkspace { get; init; }
        public bool CreatedSession { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public static async Task<BootstrapResult> EnsureMarkerFilesAsync(
        DropboxApiClient dropbox,
        CancellationToken ct = default)
    {
        if (!dropbox.Settings.IsConnected)
        {
            return Fail("Dropbox ist nicht verbunden.");
        }

        if (!await dropbox.FolderExistsAsync(ct).ConfigureAwait(false))
        {
            var folder = dropbox.Settings.FolderPath.TrimEnd('/');
            return Fail($"Dropbox-Ordner nicht gefunden: {folder}");
        }

        var workspaceExists = await dropbox
            .NamedFileExistsAsync(DropboxConstants.PlanerWorkspaceFileName, ct)
            .ConfigureAwait(false);
        var sessionExists = await dropbox
            .NamedFileExistsAsync(DropboxConstants.PlanerSessionFileName, ct)
            .ConfigureAwait(false);

        if (workspaceExists && sessionExists)
        {
            return new BootstrapResult
            {
                Success = true,
                Message = "Planer-Ordner ist bereits eingerichtet."
            };
        }

        var createdWorkspace = false;
        var createdSession = false;

        if (!workspaceExists)
        {
            var document = new PlanerWorkspaceDocument
            {
                SavedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await dropbox
                .UploadNamedFileAsync(DropboxConstants.PlanerWorkspaceFileName, PlanerWorkspaceService.Serialize(document), ct)
                .ConfigureAwait(false);
            createdWorkspace = true;
        }

        if (!sessionExists)
        {
            var session = PlanerSessionDropboxService.CreateAvailableDocument();
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await dropbox
                .UploadNamedFileAsync(DropboxConstants.PlanerSessionFileName, json, ct)
                .ConfigureAwait(false);
            createdSession = true;
        }

        var parts = new List<string>();
        if (createdWorkspace)
        {
            parts.Add(DropboxConstants.PlanerWorkspaceFileName);
        }

        if (createdSession)
        {
            parts.Add(DropboxConstants.PlanerSessionFileName);
        }

        return new BootstrapResult
        {
            Success = true,
            CreatedWorkspace = createdWorkspace,
            CreatedSession = createdSession,
            Message = parts.Count == 0
                ? "Planer-Ordner ist bereits eingerichtet."
                : $"Angelegt: {string.Join(", ", parts)}"
        };
    }

    private static BootstrapResult Fail(string message) =>
        new() { Success = false, Message = message };
}
