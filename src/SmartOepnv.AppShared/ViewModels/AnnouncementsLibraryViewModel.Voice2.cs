using System.IO;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Models;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel
{
    [RelayCommand]
    private void AssignVoice2Sound()
    {
        var announcement = SelectedAnnouncement;
        var petraName = announcement?.EmbeddedSoundFileName?.Trim() ?? string.Empty;
        if (announcement is null || petraName.Length == 0)
        {
            StatusMessage = "Zuerst die Petra-Datei dieser Ansage zuweisen.";
            return;
        }

        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            StatusMessage = "Workspace nicht initialisiert.";
            return;
        }

        var voice2Name = AnnouncementVoiceFiles.Voice2FileName(petraName);
        var dialog = new OpenFileDialog
        {
            Title = $"Stimme 2 für „{announcement.DisplayName}“ → {voice2Name}",
            Filter = "Audiodateien|*.wav;*.mp3;*.ogg;*.m4a|Alle Dateien|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            AnnouncementPreviewPlayer.Stop();
            var rawDir = PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(AppServices.Workspace);
            Directory.CreateDirectory(rawDir);
            var rawTarget = Path.Combine(rawDir, voice2Name);
            var converted = AnnouncementWavImport.Store(dialog.FileName, rawTarget);

            var embeddedDir = PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace);
            Directory.CreateDirectory(embeddedDir);
            var embeddedTarget = Path.Combine(embeddedDir, voice2Name);
            File.Copy(rawTarget, embeddedTarget, overwrite: true);
            EmbeddedSoundsEditor.UpsertFromFile(
                AppServices.Routes.Editor.PackageRoot,
                voice2Name,
                embeddedTarget);
            MarkDirty();
            StatusMessage = converted
                ? $"„{voice2Name}“ aus der Audiodatei als WAV gespeichert. „{petraName}“ bleibt Petra. Speichern & JSON fürs Handy."
                : $"„{voice2Name}“ als Stimme 2 gespeichert. „{petraName}“ bleibt Petra. Speichern & JSON fürs Handy.";
            RefreshGreetingSequenceNotes();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stimme 2 zuweisen fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AssignSequenceItemVoice2()
    {
        var item = SelectedSequenceItem;
        if (item is null || item.Kind != AnnouncementSequenceEntryKind.Audio)
        {
            StatusMessage = "In der Folge zuerst den Intro-Ton wählen (nicht Linie, Nach oder Ziel).";
            return;
        }

        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            StatusMessage = "Workspace nicht initialisiert.";
            return;
        }

        var petraName = item.DisplayName?.Trim() ?? string.Empty;
        if (petraName.Length == 0)
        {
            StatusMessage = "Dieser Eintrag hat keinen Dateinamen.";
            return;
        }

        var voice2Name = AnnouncementVoiceFiles.Voice2FileName(petraName);
        var dialog = new OpenFileDialog
        {
            Title = $"Stimme 2 für „{petraName}“ → {voice2Name}. Die Folge und die 150-ms-Lücke bleiben.",
            Filter = "Audiodateien|*.wav;*.mp3;*.ogg;*.m4a|Alle Dateien|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            AnnouncementPreviewPlayer.Stop();
            var rawDir = PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(AppServices.Workspace);
            Directory.CreateDirectory(rawDir);
            var rawTarget = Path.Combine(rawDir, voice2Name);
            var converted = AnnouncementWavImport.Store(dialog.FileName, rawTarget);

            var embeddedDir = PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace);
            Directory.CreateDirectory(embeddedDir);
            var embeddedTarget = Path.Combine(embeddedDir, voice2Name);
            File.Copy(rawTarget, embeddedTarget, overwrite: true);
            EmbeddedSoundsEditor.UpsertFromFile(
                AppServices.Routes.Editor.PackageRoot,
                voice2Name,
                embeddedTarget);
            MarkDirty();
            RefreshGreetingSequenceNotes();
            StatusMessage = converted
                ? $"„{voice2Name}“ aus der Audiodatei als WAV gespeichert. Die Folge Linie → Nach → Ziel und die automatische Lücke bleiben. Speichern & JSON fürs Handy."
                : $"„{voice2Name}“ liegt neben „{petraName}“. Die Folge Linie → Nach → Ziel und die automatische Lücke bleiben. Speichern & JSON fürs Handy.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stimme 2 zuweisen fehlgeschlagen: {ex.Message}";
        }
    }
}
