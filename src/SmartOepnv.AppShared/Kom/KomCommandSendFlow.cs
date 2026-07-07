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
    Timeout,
    ProgressOnly
}

/// <summary>Senden + „Gesendet“-Hinweis + Warten auf Fahrzeug-Ack (wie Haupthandy).</summary>
public static class KomCommandSendFlow
{
    private static void SetStatus(TextBlock? statusLine, string text)
    {
        if (statusLine is null)
        {
            return;
        }

        if (statusLine.CheckAccess())
        {
            statusLine.Text = text;
            return;
        }

        statusLine.Dispatcher.Invoke(() => statusLine.Text = text);
    }

    public static async Task<KomCommandSendOutcome> ExecuteAsync(
        Window owner,
        TextBlock? statusLine,
        string vehicleDisplayName,
        string vehiclePhone,
        string commandType,
        Func<CancellationToken, Task<long>> uploadAsync,
        CancellationToken ct = default,
        Action? onProgressAck = null)
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
        var progressShown = false;
        var ack = await KomCommandAckService.WaitForAckAsync(
            AppServices.Dropbox,
            vehiclePhone,
            commandId,
            ct,
            onProgress: progress =>
            {
                owner.Dispatcher.Invoke(() =>
                {
                    if (progressShown)
                    {
                        return;
                    }

                    progressShown = true;
                    KomCommandAckFeedback.ShowAckResult(
                        owner,
                        vehicleDisplayName,
                        progress.Message,
                        progress.IsSuccess,
                        commandType);
                    SetStatus(statusLine, progress.Message);
                    onProgressAck?.Invoke();
                });
            },
            commandType: commandType).ConfigureAwait(true);

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

        if (progressShown)
        {
            var partialMessage = string.Equals(
                commandType,
                RemoteManualUpdateService.CommandType,
                StringComparison.Ordinal)
                ? "Update läuft auf dem Fahrzeug – keine Abschlussmeldung (Zeitüberschreitung)."
                : string.Equals(commandType, KomLeitstelleDurchsageService.CommandType, StringComparison.Ordinal)
                    ? "Durchsage läuft – keine Abschlussmeldung (Zeitüberschreitung)."
                    : "Befehl läuft – keine Abschlussmeldung (Zeitüberschreitung).";
            KomCommandAckFeedback.ShowAckResult(
                owner,
                vehicleDisplayName,
                partialMessage,
                success: false,
                commandType);
            SetStatus(statusLine, partialMessage);
            return KomCommandSendOutcome.ProgressOnly;
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

    /// <summary>
    /// Sendet Befehl, schließt den Dialog sofort danach.
    /// Rückmeldung vom Fahrzeug erscheint später als separates Statusfenster (blockiert die Leitstelle nicht).
    /// </summary>
    /// <remarks><paramref name="dialog"/> muss vor <see cref="Close"/> freigegeben sein (z. B. via <see cref="KomSendDialogGuard.EndSend"/>).</remarks>
    public static async Task<bool> SendAndReleaseDialogAsync(
        Window dialog,
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
            return false;
        }

        if (commandId <= 0)
        {
            SetStatus(statusLine, "Senden fehlgeschlagen.");
            return false;
        }

        var feedbackOwner = KomFeedbackOwner.Resolve(dialog);
        KomCommandAckFeedback.ShowSent(
            feedbackOwner,
            vehicleDisplayName,
            KomCommandAckService.SentHintFor(commandType, vehicleDisplayName));

        dialog.DialogResult = true;
        dialog.Close();

        _ = WaitForAckAndNotifyAsync(
            feedbackOwner,
            vehicleDisplayName,
            vehiclePhone,
            commandType,
            commandId);

        return true;
    }

    private static async Task WaitForAckAndNotifyAsync(
        Window feedbackOwner,
        string vehicleDisplayName,
        string vehiclePhone,
        string commandType,
        long commandId)
    {
        try
        {
            var progressShown = false;
            var ack = await KomCommandAckService.WaitForAckAsync(
                AppServices.Dropbox,
                vehiclePhone,
                commandId,
                CancellationToken.None,
                onProgress: progress =>
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (progressShown)
                        {
                            return;
                        }

                        progressShown = true;
                        KomCommandAckFeedback.ShowAckResult(
                            feedbackOwner,
                            vehicleDisplayName,
                            progress.Message,
                            progress.IsSuccess,
                            commandType);
                    });
                },
                commandType: commandType).ConfigureAwait(false);

            Application.Current?.Dispatcher.Invoke(() =>
                NotifyFinalAck(feedbackOwner, vehicleDisplayName, commandType, ack, progressShown));
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                KomCommandAckFeedback.ShowAckResult(
                    feedbackOwner,
                    vehicleDisplayName,
                    $"Rückmeldung fehlgeschlagen: {ex.Message}",
                    success: false,
                    commandType));
        }
    }

    private static void NotifyFinalAck(
        Window feedbackOwner,
        string vehicleDisplayName,
        string commandType,
        KomCommandAckService.AckResult? ack,
        bool progressShown)
    {
        if (ack is not null)
        {
            KomCommandAckFeedback.ShowAckResult(
                feedbackOwner,
                vehicleDisplayName,
                ack.Message,
                ack.IsSuccess,
                commandType);
            return;
        }

        if (progressShown)
        {
            var partialMessage = string.Equals(
                commandType,
                RemoteManualUpdateService.CommandType,
                StringComparison.Ordinal)
                ? "Update läuft auf dem Fahrzeug – keine Abschlussmeldung (Zeitüberschreitung)."
                : string.Equals(commandType, KomLeitstelleDurchsageService.CommandType, StringComparison.Ordinal)
                    ? "Durchsage läuft – keine Abschlussmeldung (Zeitüberschreitung)."
                    : "Befehl läuft – keine Abschlussmeldung (Zeitüberschreitung).";
            KomCommandAckFeedback.ShowAckResult(
                feedbackOwner,
                vehicleDisplayName,
                partialMessage,
                success: false,
                commandType);
            return;
        }

        KomCommandAckFeedback.ShowAckResult(
            feedbackOwner,
            vehicleDisplayName,
            "Keine Rückmeldung vom Fahrzeug (Zeitüberschreitung).",
            success: false,
            commandType);
    }
}
