using System.Windows;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Kom;

/// <summary>Kurzes Status-Feedback (wie GPSAnsagen <c>KomCommandAckFeedback</c>).</summary>
public static class KomCommandAckFeedback
{
    public static void ShowSent(Window owner, string vehicleName, string detail) =>
        KomCommandStatusDialog.Show(
            KomFeedbackOwner.Resolve(owner),
            "Gesendet",
            $"{vehicleName}\n{detail}",
            success: true);

    public static void ShowAckResult(
        Window owner,
        string vehicleName,
        string message,
        bool success,
        string? commandType = null)
    {
        var title = success ? "Erfolgreich" : "Fehler";
        var body = success && commandType == KomLeitstelleDurchsageService.CommandType
            ? $"{vehicleName} Durchsage erfolgreich abgespielt"
            : $"{vehicleName}\n{message}";
        KomCommandStatusDialog.Show(KomFeedbackOwner.Resolve(owner), title, body, success);
    }
}
