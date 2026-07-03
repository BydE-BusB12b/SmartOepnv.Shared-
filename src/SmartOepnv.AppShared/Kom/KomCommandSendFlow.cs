using System.Windows;
using System.Windows.Controls;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Kom;

public enum KomCommandSendOutcome
{
    Success,
    UploadFailed,
    AckError,
    Timeout
}

/// <summary>Senden + „Gesendet“-Hinweis + Warten auf Fahrzeug-Ack (wie Haupthandy).</summary>
public static class KomCommandSendFlow
{
    private static void SetStatus(TextBlock? statusLine, string text)
    {
        if (statusLine != null)
        {
            statusLine.Text = text;
        }
    }

    public static async Task<KomCommandSendOutcome> ExecuteAsync(
        Window owner,
        TextBlock? statusLine,
        string vehicleDisplayName,
        string vehiclePhone,
        string commandType,
        Func<CancellationToken, Task<long>> uploadAsync,
        CancellationToken ct = default)
    {
        SetStatus(statusLine, "Sende Befehl …");
        long commandId;
        try
        {
            commandId = await uploadAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(statusLine, $"Fehler: {ex.Message}");
            return KomCommandSendOutcome.UploadFailed;
        }

        if (commandId <= 0)
        {
            SetStatus(statusLine, "Senden fehlgeschlagen.");
            return KomCommandSendOutcome.UploadFailed;
        }

        KomCommandAckFeedback.ShowSent(
            owner,
            vehicleDisplayName,
            KomCommandAckService.SentHintFor(commandType, vehicleDisplayName));

        SetStatus(statusLine, "Warte auf Bestätigung vom Fahrzeug …");
        var ack = await KomCommandAckService.WaitForAckAsync(
            AppServices.Dropbox,
            vehiclePhone,
            commandId,
            ct).ConfigureAwait(true);

        if (ack is not null)
        {
            KomCommandAckFeedback.ShowAckResult(
                owner,
                vehicleDisplayName,
                ack.Message,
                ack.IsSuccess,
                commandType);
            SetStatus(statusLine, ack.IsSuccess
                ? ack.Message
                : $"Fehler: {ack.Message}");
            return ack.IsSuccess ? KomCommandSendOutcome.Success : KomCommandSendOutcome.AckError;
        }

        const string timeoutMessage = "Keine Rückmeldung vom Fahrzeug (Zeitüberschreitung).";
        KomCommandAckFeedback.ShowAckResult(
            owner,
            vehicleDisplayName,
            timeoutMessage,
            success: false,
            commandType);
        SetStatus(statusLine, timeoutMessage);
        return KomCommandSendOutcome.Timeout;
    }
}
