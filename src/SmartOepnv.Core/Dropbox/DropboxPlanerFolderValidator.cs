using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Session;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Prüft, ob der konfigurierte Dropbox-Ordner der Smart-ÖPNV-Planer-Ordner ist
/// (Marker: planer_workspace.json und/oder planer_session.json).
/// </summary>
public static class DropboxPlanerFolderValidator
{
    public sealed class ValidationResult
    {
        public bool FolderExists { get; init; }
        public bool WorkspaceFileExists { get; init; }
        public bool SessionFileExists { get; init; }
        public bool WorkspaceDocumentValid { get; init; }
        public bool IsValid => FolderExists && (WorkspaceFileExists || SessionFileExists);

        public string Message { get; init; } = string.Empty;
    }

    public static async Task<ValidationResult> ValidateAsync(
        DropboxApiClient dropbox,
        CancellationToken ct = default,
        bool verifyWorkspaceContent = true)
    {
        var folderPath = dropbox.Settings.FolderPath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new ValidationResult
            {
                Message = "Kein Dropbox-Ordnerpfad eingetragen."
            };
        }

        if (!dropbox.Settings.IsConnected)
        {
            return new ValidationResult
            {
                Message = "Dropbox ist nicht verbunden."
            };
        }

        if (!await dropbox.FolderExistsAsync(ct).ConfigureAwait(false))
        {
            return new ValidationResult
            {
                Message = $"Dropbox-Ordner nicht gefunden: {folderPath}"
            };
        }

        var workspaceExists = await dropbox
            .NamedFileExistsAsync(DropboxConstants.PlanerWorkspaceFileName, ct)
            .ConfigureAwait(false);
        var sessionExists = await dropbox
            .NamedFileExistsAsync(DropboxConstants.PlanerSessionFileName, ct)
            .ConfigureAwait(false);

        var workspaceValid = workspaceExists;
        if (workspaceExists && verifyWorkspaceContent)
        {
            try
            {
                var json = await dropbox
                    .DownloadNamedFileAsync(DropboxConstants.PlanerWorkspaceFileName, ct)
                    .ConfigureAwait(false);
                var document = PlanerWorkspaceService.Parse(json);
                workspaceValid = document is not null &&
                                 string.Equals(
                                     document.DocumentType,
                                     PlanerWorkspaceDocument.Kind,
                                     StringComparison.Ordinal);
            }
            catch
            {
                workspaceValid = false;
            }
        }

        if (workspaceExists && !workspaceValid)
        {
            return new ValidationResult
            {
                FolderExists = true,
                WorkspaceFileExists = true,
                SessionFileExists = sessionExists,
                Message =
                    $"{DropboxConstants.PlanerWorkspaceFileName} ist vorhanden, aber kein gültiger Planer-Arbeitsstand."
            };
        }

        if (!workspaceExists && !sessionExists)
        {
            return new ValidationResult
            {
                FolderExists = true,
                Message =
                    $"Kein Planer-Ordner: In „{folderPath}“ fehlen {DropboxConstants.PlanerWorkspaceFileName} " +
                    $"und {DropboxConstants.PlanerSessionFileName}. " +
                    $"Bitte Ordnerpfad prüfen (Standard: {DropboxConstants.DefaultFolderPath}) " +
                    "oder unter Einstellungen „Planer-Ordner initialisieren“."
            };
        }

        var marker = workspaceExists
            ? DropboxConstants.PlanerWorkspaceFileName
            : DropboxConstants.PlanerSessionFileName;
        return new ValidationResult
        {
            FolderExists = true,
            WorkspaceFileExists = workspaceExists,
            SessionFileExists = sessionExists,
            WorkspaceDocumentValid = workspaceValid || workspaceExists,
            Message = $"Planer-Ordner erkannt ({marker} in {folderPath})."
        };
    }
}
