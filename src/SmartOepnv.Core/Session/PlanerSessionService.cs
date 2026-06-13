using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.Session;

/// <summary>
/// Exklusive Planer-Sitzung über planer_session.json in Dropbox.
/// </summary>
public sealed class PlanerSessionService
{
    private readonly PlanerSessionDropboxService _dropboxService;
    private readonly DropboxApiClient _dropbox;

    public PlanerSessionService(DropboxApiClient dropbox)
    {
        _dropbox = dropbox;
        _dropboxService = new PlanerSessionDropboxService(dropbox);
    }

    public string? CurrentUsername { get; private set; }

    public string? SessionId { get; private set; }

    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(SessionId);

    public bool NeedsExitHandling() => IsLoggedIn || PlanerSessionLocalStore.HasPendingRelease();

    public async Task TryReleasePendingLocalSessionAsync(CancellationToken ct = default)
    {
        if (!PlanerSessionLocalStore.HasPendingRelease())
        {
            return;
        }

        await ReleaseLockAsync(ct).ConfigureAwait(false);
    }

    public async Task<PlanerSessionAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        if (!_dropbox.Settings.IsConnected)
        {
            return PlanerSessionAvailability.DropboxUnavailable;
        }

        var document = await _dropboxService.LoadAsync(ct).ConfigureAwait(false);
        return PlanerSessionDropboxService.IsLockedByOther(document)
            ? PlanerSessionAvailability.InUseByOther
            : PlanerSessionAvailability.Available;
    }

    public async Task<(PlanerSessionAvailability Availability, string ActiveUsername)> InspectLockAsync(
        CancellationToken ct = default)
    {
        if (!_dropbox.Settings.IsConnected)
        {
            return (PlanerSessionAvailability.DropboxUnavailable, string.Empty);
        }

        var document = await _dropboxService.LoadAsync(ct).ConfigureAwait(false);
        if (PlanerSessionDropboxService.IsSameMachineOrphan(document))
        {
            await ClearLockDocumentAsync(document, ct).ConfigureAwait(false);
            return (PlanerSessionAvailability.Available, string.Empty);
        }

        if (!PlanerSessionDropboxService.IsLockedByOther(document))
        {
            return (PlanerSessionAvailability.Available, string.Empty);
        }

        var name = string.IsNullOrWhiteSpace(document.Username) ? "Unbekannt" : document.Username.Trim();
        return (PlanerSessionAvailability.InUseByOther, name);
    }

    public async Task<PlanerSessionLoginResult> TryLoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (!PlanerCredentialValidator.TryValidate(username, password, out var authenticatedName))
        {
            return PlanerSessionLoginResult.Fail(
                "Benutzername oder Passwort ist falsch.\n\n" +
                "Anmeldung nur für Hauptnutzer mit Planerpasswort (Personalverwaltung).");
        }

        if (!_dropbox.Settings.IsConnected)
        {
            return PlanerSessionLoginResult.Fail(
                "Dropbox ist nicht verbunden. Bitte zuerst „Dropbox einrichten…“ im Anmeldedialog verwenden.");
        }

        var document = await _dropboxService.LoadAsync(ct).ConfigureAwait(false);
        if (PlanerSessionDropboxService.IsSameMachineOrphan(document))
        {
            await ClearLockDocumentAsync(document, ct).ConfigureAwait(false);
            document = PlanerSessionDropboxService.CreateAvailableDocument();
        }

        if (PlanerSessionDropboxService.IsLockedByOther(document))
        {
            var who = string.IsNullOrWhiteSpace(document.Username) ? "ein anderer Nutzer" : document.Username.Trim();
            return PlanerSessionLoginResult.Fail($"Anmelden nicht möglich – Planer in Verwendung ({who}).");
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var locked = new PlanerSessionDocument
        {
            Version = "1.0",
            Status = PlanerSessionStatus.InUse,
            Username = authenticatedName,
            SessionId = sessionId,
            MachineName = Environment.MachineName,
            UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        try
        {
            await _dropboxService.SaveAsync(locked, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return PlanerSessionLoginResult.Fail($"Sperre in Dropbox fehlgeschlagen: {ex.Message}");
        }

        var verify = await _dropboxService.LoadAsync(ct).ConfigureAwait(false);
        if (!string.Equals(verify.SessionId, sessionId, StringComparison.Ordinal))
        {
            return PlanerSessionLoginResult.Fail("Anmelden nicht möglich – ein anderer Nutzer war schneller.");
        }

        SessionId = sessionId;
        CurrentUsername = authenticatedName;
        PlanerSessionLocalStore.Save(sessionId, authenticatedName);
        return PlanerSessionLoginResult.Ok();
    }

    public async Task ReleaseLockAsync(CancellationToken ct = default)
    {
        var ownSessionId = SessionId ?? PlanerSessionLocalStore.TryReadSessionId();
        if (string.IsNullOrWhiteSpace(ownSessionId))
        {
            return;
        }

        if (!_dropbox.Settings.IsConnected)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var document = await _dropboxService.LoadAsync(ct).ConfigureAwait(false);
                if (string.Equals(document.SessionId, ownSessionId, StringComparison.Ordinal))
                {
                    await ClearLockDocumentAsync(document, ct).ConfigureAwait(false);
                }

                SessionId = null;
                CurrentUsername = null;
                PlanerSessionLocalStore.Clear();
                return;
            }
            catch when (attempt < 2)
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
            catch
            {
                break;
            }
        }
    }

    /// <summary>Letzter Versuch beim Beenden – blockiert bis Upload oder Fehler.</summary>
    public void ReleaseLockBestEffortSync()
    {
        try
        {
            ReleaseLockAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // optional
        }

        if (!PlanerSessionLocalStore.HasPendingRelease())
        {
            return;
        }

        var ownSessionId = SessionId ?? PlanerSessionLocalStore.TryReadSessionId();
        if (string.IsNullOrWhiteSpace(ownSessionId) || !_dropbox.Settings.IsConnected)
        {
            return;
        }

        try
        {
            var document = _dropboxService.LoadAsync().GetAwaiter().GetResult();
            if (string.Equals(document.SessionId, ownSessionId, StringComparison.Ordinal))
            {
                ClearLockDocumentAsync(document, CancellationToken.None).GetAwaiter().GetResult();
            }

            SessionId = null;
            CurrentUsername = null;
            PlanerSessionLocalStore.Clear();
        }
        catch
        {
            // Lokale Session bleibt für nächsten Start (TryReleasePendingLocalSessionAsync).
        }
    }

    /// <summary>Admin: hängende Sperre in Dropbox freigeben (z. B. nach Absturz ohne Abmeldung).</summary>
    public async Task ForceClearLockAsync(CancellationToken ct = default)
    {
        SessionId = null;
        CurrentUsername = null;
        PlanerSessionLocalStore.Clear();

        if (!_dropbox.Settings.IsConnected)
        {
            throw new InvalidOperationException("Dropbox ist nicht verbunden.");
        }

        var document = await _dropboxService.LoadAsync(ct).ConfigureAwait(false);
        await ClearLockDocumentAsync(document, ct).ConfigureAwait(false);
    }

    private async Task ClearLockDocumentAsync(PlanerSessionDocument document, CancellationToken ct)
    {
        document.Status = PlanerSessionStatus.Available;
        document.Username = string.Empty;
        document.SessionId = string.Empty;
        document.MachineName = string.Empty;
        await _dropboxService.SaveAsync(document, ct).ConfigureAwait(false);
    }
}
