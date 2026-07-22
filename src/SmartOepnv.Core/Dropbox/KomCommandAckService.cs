using System.Text.Json;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Liest Fahrzeug-Bestätigungen aus <c>kom_command_ack_&lt;Telefon&gt;.json</c>
/// (wie GPSAnsagen <c>KomCommandAck.pollAcksFromDropboxIfMain</c>).
/// </summary>
public static class KomCommandAckService
{
    public const string AckType = "kom_command_ack";
    public const string StatusSuccess = "success";
    public const string StatusError = "error";

    private static readonly int[] DefaultPollDelaysMs = [0, 1_000, 2_000, 5_000, 8_000, 15_000, 25_000, 40_000, 60_000];

    private static readonly int[] ManualUpdatePollDelaysMs =
        [0, 1_000, 2_000, 5_000, 8_000, 15_000, 25_000, 40_000, 60_000, 90_000, 120_000, 180_000];

    /// <summary>Nach Zwischenstand „Update gestartet“ häufiger auf Abschluss prüfen (Import kann mehrere Minuten dauern).</summary>
    private static readonly int[] ManualUpdateFollowUpPollDelaysMs =
        Enumerable.Repeat(3_000, 200).ToArray();

    /** Intro + Pause + bis zu 3 Min. Aufnahme – länger als Standard-Polling. */
    private static readonly int[] DurchsagePollDelaysMs =
        [0, 1_000, 2_000, 5_000, 8_000, 15_000, 25_000, 40_000, 60_000, 90_000];

    private static readonly int[] DurchsageFollowUpPollDelaysMs =
        Enumerable.Repeat(3_000, 80).ToArray();

    public sealed record AckResult(
        long CommandId,
        string OriginalType,
        bool IsSuccess,
        string Message,
        bool IsProgress = false);

    public static string BuildAckFileName(string phoneRaw)
    {
        var normalized = KomPhone.Normalize(phoneRaw);
        return string.IsNullOrEmpty(normalized)
            ? "kom_command_ack_unknown.json"
            : $"kom_command_ack_{normalized}.json";
    }

    public static string SentHintFor(string commandType, string vehicleName) => commandType switch
    {
        KomLeitstelleDurchsageService.CommandType => $"Fahrgastraum-Durchsage an {vehicleName} gesendet",
        KomRemoteDestinationService.CommandType => $"Fernziel an {vehicleName} gesendet",
        KomRemoteRouteService.CommandType => $"Fernroute an {vehicleName} gesendet",
        KomRemoteLockService.CommandType => $"Sperre/Entsperren an {vehicleName} gesendet",
        KomRemoteDriverLoginService.CommandType => $"Fahreranmeldung an {vehicleName} gesendet",
        RemoteManualUpdateService.CommandType => $"Fernupdate an {vehicleName} gesendet",
        ZblMessageService.CommandType => $"Meldung an {vehicleName} gesendet",
        _ => $"Befehl an {vehicleName} gesendet"
    };

    public static string DefaultSuccessMessage(string commandType) => commandType switch
    {
        KomLeitstelleDurchsageService.CommandType => "Durchsage erfolgreich abgespielt",
        KomRemoteDestinationService.CommandType => "Ziel erfolgreich geändert",
        KomRemoteRouteService.CommandType => "Route erfolgreich übernommen",
        KomRemoteLockService.CommandType => "Sperrstatus übernommen",
        KomRemoteDriverLoginService.CommandType => "Fahreranmeldung übernommen",
        RemoteManualUpdateService.CommandType => "Update erfolgreich installiert",
        ZblMessageService.CommandType => "Meldung empfangen",
        _ => "Erfolgreich"
    };

    public const string AckPhaseStarted = "started";
    public const string AckPhaseComplete = "complete";

    public static string DefaultProgressMessage(string commandType) => commandType switch
    {
        RemoteManualUpdateService.CommandType => "Routen-Update auf dem Gerät gestartet",
        KomLeitstelleDurchsageService.CommandType => "Durchsage wird im Fahrgastraum abgespielt",
        _ => "Befehl auf dem Gerät gestartet"
    };

    public static async Task<AckResult?> WaitForAckAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        long commandId,
        CancellationToken ct = default,
        Action<AckResult>? onProgress = null,
        string? commandType = null)
    {
        if (commandId <= 0)
        {
            return null;
        }

        var delays = ResolvePollDelays(commandType);

        var sawProgress = false;
        foreach (var delayMs in delays)
        {
            ct.ThrowIfCancellationRequested();
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }

            var ack = await TryPollAckAsync(dropbox, phoneRaw, commandId, ct).ConfigureAwait(false);
            if (ack is null)
            {
                continue;
            }

            if (ack.IsProgress)
            {
                sawProgress = true;
                onProgress?.Invoke(ack);
                continue;
            }

            return ack;
        }

        if (sawProgress)
        {
            foreach (var delayMs in ResolveFollowUpPollDelays(commandType))
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(delayMs, ct).ConfigureAwait(false);

                var ack = await TryPollAckAsync(dropbox, phoneRaw, commandId, ct).ConfigureAwait(false);
                if (ack is null || ack.IsProgress)
                {
                    continue;
                }

                return ack;
            }
        }

        return null;
    }

    private static int[] ResolvePollDelays(string? commandType)
    {
        if (string.Equals(commandType, RemoteManualUpdateService.CommandType, StringComparison.Ordinal))
        {
            return ManualUpdatePollDelaysMs;
        }

        if (string.Equals(commandType, KomLeitstelleDurchsageService.CommandType, StringComparison.Ordinal))
        {
            return DurchsagePollDelaysMs;
        }

        return DefaultPollDelaysMs;
    }

    private static int[] ResolveFollowUpPollDelays(string? commandType)
    {
        if (string.Equals(commandType, RemoteManualUpdateService.CommandType, StringComparison.Ordinal))
        {
            return ManualUpdateFollowUpPollDelaysMs;
        }

        if (string.Equals(commandType, KomLeitstelleDurchsageService.CommandType, StringComparison.Ordinal))
        {
            return DurchsageFollowUpPollDelaysMs;
        }

        return [];
    }

    public static async Task<AckResult?> TryPollAckAsync(
        DropboxApiClient dropbox,
        string phoneRaw,
        long commandId,
        CancellationToken ct = default)
    {
        var phone = KomPhone.Normalize(phoneRaw);
        if (commandId <= 0 || string.IsNullOrEmpty(phone))
        {
            return null;
        }

        if (KomCommandAckProcessedStore.WasProcessed(phone, commandId))
        {
            return null;
        }

        var fileName = BuildAckFileName(phone);
        string content;
        try
        {
            content = await dropbox.DownloadNamedFileAsync(fileName, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsMissingFileError(ex))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (!string.Equals(root.GetProperty("type").GetString(), AckType, StringComparison.Ordinal))
            {
                return null;
            }

            var ackCommandId = root.TryGetProperty("commandId", out var idProp)
                ? idProp.GetInt64()
                : 0L;
            if (ackCommandId != commandId)
            {
                return null;
            }

            var originalType = root.TryGetProperty("originalType", out var typeProp)
                ? typeProp.GetString() ?? string.Empty
                : string.Empty;
            var status = root.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? StatusSuccess
                : StatusSuccess;
            var message = root.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(message))
            {
                message = DefaultSuccessMessage(originalType);
            }

            var phase = root.TryGetProperty("phase", out var phaseProp)
                ? phaseProp.GetString() ?? AckPhaseComplete
                : AckPhaseComplete;
            var isProgress = string.Equals(phase, AckPhaseStarted, StringComparison.OrdinalIgnoreCase);

            if (!isProgress)
            {
                KomCommandAckProcessedStore.MarkProcessed(phone, commandId);
            }

            return new AckResult(
                ackCommandId,
                originalType,
                !string.Equals(status, StatusError, StringComparison.OrdinalIgnoreCase),
                message,
                isProgress);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsMissingFileError(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("not_found", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("path/not_found", StringComparison.OrdinalIgnoreCase);
    }
}
