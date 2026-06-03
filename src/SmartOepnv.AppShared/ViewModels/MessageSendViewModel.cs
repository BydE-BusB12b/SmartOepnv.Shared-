using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.ViewModels;

/// <summary>Leitstelle: Vorlagen aus dem Route-Paket wählen und per Dropbox an Fahrzeuge senden.</summary>
public partial class MessageSendViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage =
        "Route-Paket laden – Vorlagen stammen aus messageTemplates im JSON (Planer).";
    [ObservableProperty] private string messageText = string.Empty;
    [ObservableProperty] private string? selectedMessageTemplate;
    [ObservableProperty] private bool selectAllRecipients;
    [ObservableProperty] private bool isSending;

    public ObservableCollection<string> MessageTemplates { get; } = [];
    public ObservableCollection<MessageSendRecipientItem> Recipients { get; } = [];

    partial void OnSelectedMessageTemplateChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            MessageText = value;
        }
    }

    partial void OnSelectAllRecipientsChanged(bool value)
    {
        foreach (var r in Recipients)
        {
            r.IsSelected = value;
        }
    }

    public void RefreshFromEditor()
    {
        MessageTemplates.Clear();
        Recipients.Clear();
        SelectedMessageTemplate = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        foreach (var t in editor.MessageTemplates)
        {
            var text = t.Trim();
            if (!string.IsNullOrEmpty(text) && !MessageTemplates.Contains(text))
            {
                MessageTemplates.Add(text);
            }
        }

        foreach (var v in editor.RegisteredVehicles)
        {
            if (string.IsNullOrWhiteSpace(v.PhoneNumber))
            {
                continue;
            }

            Recipients.Add(new MessageSendRecipientItem
            {
                Name = v.Name.Trim(),
                PhoneNumber = v.PhoneNumber.Trim()
            });
        }

        SelectedMessageTemplate = MessageTemplates.FirstOrDefault();
        StatusMessage = MessageTemplates.Count == 0
            ? "Keine messageTemplates im Paket – im Planer unter „Nachrichten“ anlegen und verteilen."
            : $"{MessageTemplates.Count} Vorlage(n), {Recipients.Count} Fahrzeug(e) – Text wählen, Empfänger markieren, senden.";
    }

    private bool CanSend() => !IsSending;

    partial void OnIsSendingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {

        var text = MessageText.Trim();
        if (string.IsNullOrEmpty(text))
        {
            StatusMessage = "Bitte Nachrichtentext eingeben oder eine Vorlage wählen.";
            return;
        }

        var targets = Recipients.Where(r => r.IsSelected).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Bitte mindestens ein Fahrzeug auswählen.";
            return;
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            StatusMessage = "Dropbox nicht verbunden – bitte unter Einstellungen verbinden.";
            return;
        }

        IsSending = true;
        var ok = 0;
        var fail = 0;
        try
        {
            StatusMessage = $"Sende an {targets.Count} Fahrzeug(e)…";
            var errorDetails = new List<string>();
            foreach (var target in targets)
            {
                try
                {
                    await AppServices.Dropbox.UploadZblMessageAsync(target.PhoneNumber, text);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    var num = ZblMessageService.NormalizePhone(target.PhoneNumber);
                    var detail =
                        $"{target.Name} ({num}): {ex.Message}";
                    errorDetails.Add(detail);
                    System.Diagnostics.Debug.WriteLine($"ZBL send {target.PhoneNumber}: {ex}");
                }
            }

            StatusMessage = fail switch
            {
                0 => $"Nachricht an {ok} Fahrzeug(en) gesendet (zbl_message via Dropbox).",
                _ when ok == 0 && errorDetails.Count == 1 => ZblTruncateStatus($"Senden fehlgeschlagen: {errorDetails[0]}"),
                _ => ZblTruncateStatus(
                    $"An {ok} Fahrzeug(en) gesendet, {fail} Fehler – " +
                    string.Join(" • ", errorDetails))
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"Senden fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    private static string ZblTruncateStatus(string s, int maxLen = 520)
    {
        if (s.Length <= maxLen)
        {
            return s;
        }

        return s[..maxLen] + "…";
    }
}
