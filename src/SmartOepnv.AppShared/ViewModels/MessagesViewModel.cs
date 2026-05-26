using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class MessagesViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private string? selectedMessageTemplate;
    [ObservableProperty] private string? selectedMailTemplate;
    [ObservableProperty] private string newMessageTemplate = string.Empty;
    [ObservableProperty] private string newMailTemplate = string.Empty;

    public ObservableCollection<string> MessageTemplates { get; } = [];
    public ObservableCollection<string> MailTemplates { get; } = [];

    public MessagesViewModel()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChanges);
        }
    }

    public void RefreshFromEditor()
    {
        MessageTemplates.Clear();
        MailTemplates.Clear();
        SelectedMessageTemplate = null;
        SelectedMailTemplate = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        foreach (var t in editor.MessageTemplates)
        {
            MessageTemplates.Add(t);
        }

        foreach (var t in editor.MailTemplates)
        {
            MailTemplates.Add(t);
        }

        SelectedMessageTemplate = MessageTemplates.FirstOrDefault();
        SelectedMailTemplate = MailTemplates.FirstOrDefault();
        StatusMessage =
            $"{MessageTemplates.Count} KOM-Nachrichten, {MailTemplates.Count} Mail-Vorlagen – werden mit dem Routen-JSON verteilt.";
    }

    public void CommitChanges()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        editor.ReplaceMessageTemplates(
            MessageTemplates.ToList(),
            MailTemplates.ToList());
        AppServices.Routes.ApplyEditorChanges("nachrichten");
        StatusMessage =
            $"{MessageTemplates.Count} Nachrichten, {MailTemplates.Count} Mail-Vorlagen gespeichert.";
    }

    [RelayCommand]
    private void AddMessageTemplate()
    {
        var text = NewMessageTemplate.Trim();
        if (text.Length == 0)
        {
            StatusMessage = "Bitte Text für die Nachrichtenvorlage eingeben.";
            return;
        }

        if (MessageTemplates.Contains(text))
        {
            StatusMessage = "Diese Nachrichtenvorlage existiert bereits.";
            return;
        }

        MessageTemplates.Add(text);
        SelectedMessageTemplate = text;
        NewMessageTemplate = string.Empty;
        StatusMessage = "Nachrichtenvorlage hinzugefügt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void RemoveMessageTemplate()
    {
        if (SelectedMessageTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Nachrichtenvorlage auswählen.";
            return;
        }

        var idx = MessageTemplates.IndexOf(SelectedMessageTemplate);
        MessageTemplates.Remove(SelectedMessageTemplate);
        SelectedMessageTemplate = MessageTemplates.Count == 0
            ? null
            : MessageTemplates[Math.Clamp(idx, 0, MessageTemplates.Count - 1)];
        StatusMessage = "Nachrichtenvorlage entfernt.";
    }

    [RelayCommand]
    private void AddMailTemplate()
    {
        var text = NewMailTemplate.Trim();
        if (text.Length == 0)
        {
            StatusMessage = "Bitte Text für die Mail-Vorlage eingeben.";
            return;
        }

        if (MailTemplates.Contains(text))
        {
            StatusMessage = "Diese Mail-Vorlage existiert bereits.";
            return;
        }

        MailTemplates.Add(text);
        SelectedMailTemplate = text;
        NewMailTemplate = string.Empty;
        StatusMessage = "Mail-Vorlage hinzugefügt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void RemoveMailTemplate()
    {
        if (SelectedMailTemplate is null)
        {
            StatusMessage = "Bitte zuerst eine Mail-Vorlage auswählen.";
            return;
        }

        var idx = MailTemplates.IndexOf(SelectedMailTemplate);
        MailTemplates.Remove(SelectedMailTemplate);
        SelectedMailTemplate = MailTemplates.Count == 0
            ? null
            : MailTemplates[Math.Clamp(idx, 0, MailTemplates.Count - 1)];
        StatusMessage = "Mail-Vorlage entfernt.";
    }

    [RelayCommand]
    private void SaveChanges() => CommitChanges();
}
