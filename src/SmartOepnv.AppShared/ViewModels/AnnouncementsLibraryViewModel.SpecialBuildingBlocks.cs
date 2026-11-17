using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using SmartOepnv.AppShared.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Models;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel
{
    public ObservableCollection<SpecialBuildingBlockItem> SpecialBuildingBlocks { get; } = [];

    [ObservableProperty] private int selectedLibraryTabIndex;
    [ObservableProperty] private SpecialBuildingBlockItem? selectedSpecialBuildingBlock;
    [ObservableProperty] private string specialBuildingBlocksSummary = string.Empty;
    [ObservableProperty] private bool canRemoveSelectedSpecialBuildingBlock;

    partial void OnSelectedLibraryTabIndexChanged(int value)
    {
        if (value == 1)
        {
            RefreshSpecialBuildingBlocks();
        }
    }

    partial void OnSelectedSpecialBuildingBlockChanged(SpecialBuildingBlockItem? value) =>
        CanRemoveSelectedSpecialBuildingBlock = value?.IsLineBlock == true;

    public void RefreshSpecialBuildingBlocks()
    {
        SpecialBuildingBlocks.Clear();
        SelectedSpecialBuildingBlock = null;

        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            SpecialBuildingBlocksSummary = "Kein Route-Paket geladen.";
            return;
        }

        var settings = LoadSpecialBuildingSettings();
        var dropbox = ActiveDropboxFolderPath;
        var present = 0;
        foreach (var def in PlanerSpecialBuildingBlocksCatalog.BuildAll(
                     AppServices.Routes.Editor,
                     settings.SpecialBuildingBlockLines,
                     settings.SpecialBuildingBlockLinesHidden))
        {
            var path = string.Equals(def.Id, "next_stop", StringComparison.OrdinalIgnoreCase)
                ? PlanerNextStopMp3SoundResolver.TryResolve(AppServices.Workspace, dropbox)
                : string.Equals(def.Id, "folgende", StringComparison.OrdinalIgnoreCase)
                    ? PlanerFollowingStopsSoundResolver.TryResolve(AppServices.Workspace, dropbox)
                    : PlanerSpecialBuildingBlocksCatalog.TryResolvePath(
                        AppServices.Workspace,
                        def.FileName,
                        dropbox);
            var displayFileName = path is not null
                ? Path.GetFileName(path)
                : def.FileName;
            var supportsVoice2 = !string.Equals(def.Id, "gong", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(def.Id, "next_stop", StringComparison.OrdinalIgnoreCase);
            var voice2Name = AnnouncementVoiceFiles.Voice2FileName(def.FileName);
            string? voice2Path = null;
            if (supportsVoice2 && !string.IsNullOrWhiteSpace(voice2Name))
            {
                voice2Path = PlanerSpecialBuildingBlocksCatalog.TryResolvePath(
                    AppServices.Workspace,
                    voice2Name,
                    dropbox);
            }

            var item = new SpecialBuildingBlockItem
            {
                Id = def.Id,
                Title = def.Title,
                FileName = displayFileName,
                Description = def.Description,
                IsLineBlock = def.IsLineBlock,
                IsUserAdded = def.IsUserAdded,
                IsPresent = path is not null,
                StatusLabel = path is not null ? "✓ vorhanden" : "⚠ fehlt",
                ResolvedPath = path,
                SupportsVoice2 = supportsVoice2,
                Voice2FileName = voice2Name,
                Voice2ResolvedPath = voice2Path,
                Voice2StatusLabel = !supportsVoice2
                    ? string.Empty
                    : voice2Path is not null
                        ? $"Stimme 2: ✓ {voice2Name}"
                        : $"Stimme 2: fehlt ({voice2Name})"
            };
            if (item.IsPresent)
            {
                present++;
            }

            SpecialBuildingBlocks.Add(item);
        }

        SpecialBuildingBlocksSummary =
            $"{present} von {SpecialBuildingBlocks.Count} Bausteinen vorhanden";
        SelectedSpecialBuildingBlock = SpecialBuildingBlocks.FirstOrDefault(b => !b.IsPresent)
            ?? SpecialBuildingBlocks.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshSpecialBuildingBlocksList() => RefreshSpecialBuildingBlocks();

    [RelayCommand]
    private void AddLineSpecialBuildingBlock()
    {
        if (!AppServices.IsInitialized)
        {
            StatusMessage = "Workspace nicht initialisiert.";
            return;
        }

        var owner = Application.Current?.MainWindow;
        var entered = PromptTextDialog.Ask(
            owner,
            "Ton hinzufügen",
            "Bezeichnung (wird als Dateiname gespeichert), z. B. S28, RE10, RE7:",
            "");
        if (entered is null)
        {
            return;
        }

        var line = entered.Trim();
        if (string.IsNullOrWhiteSpace(line))
        {
            StatusMessage = "Keine Bezeichnung eingegeben.";
            return;
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (line.Contains(c))
            {
                StatusMessage = $"Ungültiges Zeichen in „{line}“.";
                return;
            }
        }

        var fileName = PlanerLineSoundResolver.BuildFileName(line);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            StatusMessage = "Ungültige Bezeichnung.";
            return;
        }

        if (SpecialBuildingBlocks.Any(b =>
                string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"„{line}“ ist bereits in der Liste – Datei zuweisen, falls noch fehlend.";
            SelectedSpecialBuildingBlock =
                SpecialBuildingBlocks.FirstOrDefault(b =>
                    string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var settings = AppServices.PlanerAppSettings?.Load() ?? new PlanerAppSettings();
        if (!settings.SpecialBuildingBlockLines.Any(l =>
                string.Equals(l.Trim(), line, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SpecialBuildingBlockLines.Add(line);
        }

        // Falls zuvor ausgeblendet: wieder einblenden
        settings.SpecialBuildingBlockLinesHidden.RemoveAll(h =>
            string.Equals(h.Trim(), line, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                PlanerLineSoundResolver.BuildFileName(h),
                fileName,
                StringComparison.OrdinalIgnoreCase));
        AppServices.PlanerAppSettings?.Save(settings);

        RefreshSpecialBuildingBlocks();
        SelectedSpecialBuildingBlock =
            SpecialBuildingBlocks.FirstOrDefault(b =>
                string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        StatusMessage =
            $"Ton „{line}“ eingetragen ({fileName}). Jetzt „Tondatei zuweisen…“.";
    }

    [RelayCommand]
    private void RemoveSelectedSpecialBuildingBlock()
    {
        var block = SelectedSpecialBuildingBlock;
        if (block is null || !block.IsLineBlock)
        {
            StatusMessage = "Nur Linien-Bausteine können entfernt werden (nicht und/Nach/Gong/…).";
            return;
        }

        var settings = AppServices.PlanerAppSettings?.Load() ?? new PlanerAppSettings();

        // Manuell hinzugefügt → aus Extra-Liste streichen
        settings.SpecialBuildingBlockLines.RemoveAll(l =>
            string.Equals(l.Trim(), block.Title, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                PlanerLineSoundResolver.BuildFileName(l),
                block.FileName,
                StringComparison.OrdinalIgnoreCase));

        // Aus Routen stammend (z. B. 001/01) → ausblenden, sonst kommt es beim Aktualisieren zurück
        if (!settings.SpecialBuildingBlockLinesHidden.Any(h =>
                string.Equals(h.Trim(), block.Title, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    PlanerLineSoundResolver.BuildFileName(h),
                    block.FileName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            settings.SpecialBuildingBlockLinesHidden.Add(block.Title);
        }

        AppServices.PlanerAppSettings?.Save(settings);
        RefreshSpecialBuildingBlocks();
        StatusMessage =
            $"„{block.Title}“ aus der Liste entfernt (Tondatei bleibt ggf. in ansagen_roh).";
    }

    [RelayCommand]
    private void AssignSpecialBuildingBlockFile()
    {
        var block = SelectedSpecialBuildingBlock;
        if (block is null)
        {
            StatusMessage = "Bitte zuerst einen Spezialbaustein wählen.";
            return;
        }

        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            StatusMessage = "Workspace nicht initialisiert.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = string.Equals(block.Id, "next_stop", StringComparison.OrdinalIgnoreCase)
                ? $"Tondatei für „{block.Title}“ wählen (WAV empfohlen → Next Stop.wav)"
                : string.Equals(block.Id, "folgende", StringComparison.OrdinalIgnoreCase)
                    ? $"Tondatei für „{block.Title}“ wählen (WAV empfohlen → Folgende Halte.wav)"
                    : $"Tondatei für „{block.Title}“ wählen → wird als {block.FileName} gespeichert",
            Filter = "Audiodateien|*.wav;*.mp3;*.ogg;*.m4a|Alle Dateien|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        TryInstallSpecialBuildingBlockAudio(block, dialog.FileName, voice2: false);
    }

    [RelayCommand]
    private void AssignSpecialBuildingBlockVoice2File()
    {
        var block = SelectedSpecialBuildingBlock;
        if (block is null || !block.SupportsVoice2)
        {
            StatusMessage = "Für diesen Baustein gibt es keine Stimme 2.";
            return;
        }

        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            StatusMessage = "Workspace nicht initialisiert.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Stimme 2 für „{block.Title}“ → wird als {block.Voice2FileName} gespeichert",
            Filter = "Audiodateien|*.wav;*.mp3;*.ogg;*.m4a|Alle Dateien|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        TryInstallSpecialBuildingBlockAudio(block, dialog.FileName, voice2: true);
    }

    private void TryInstallSpecialBuildingBlockAudio(SpecialBuildingBlockItem block, string sourcePath, bool voice2)
    {
        try
        {
            AnnouncementPreviewPlayer.Stop();
            var targetFileName = voice2
                ? block.Voice2FileName
                : string.Equals(block.Id, "next_stop", StringComparison.OrdinalIgnoreCase)
                    ? PlanerNextStopMp3SoundResolver.TargetFileNameForSource(sourcePath)
                    : string.Equals(block.Id, "folgende", StringComparison.OrdinalIgnoreCase)
                        ? PlanerFollowingStopsSoundResolver.TargetFileNameForSource(sourcePath)
                        : block.FileName;

            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                StatusMessage = "Dateiname für Stimme 2 fehlt.";
                return;
            }

            var rawDir = PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(AppServices.Workspace);
            Directory.CreateDirectory(rawDir);
            var rawTarget = Path.Combine(rawDir, targetFileName);
            var converted = targetFileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                ? AnnouncementWavImport.Store(sourcePath, rawTarget)
                : CopyUnchanged(sourcePath, rawTarget);

            var embeddedDir = PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace);
            Directory.CreateDirectory(embeddedDir);
            var embeddedTarget = Path.Combine(embeddedDir, targetFileName);
            File.Copy(rawTarget, embeddedTarget, overwrite: true);
            EmbeddedSoundsEditor.UpsertFromFile(
                AppServices.Routes.Editor.PackageRoot,
                targetFileName,
                embeddedTarget);

            // Alte MP3-Fehlzuweisung entfernen, wenn jetzt WAV liegt
            if (string.Equals(block.Id, "next_stop", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(targetFileName, PlanerNextStopMp3SoundResolver.FileName, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteLegacyNextStopMp3(rawDir, embeddedDir, AppServices.Routes.Editor.PackageRoot);
            }

            if (string.Equals(block.Id, "folgende", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(targetFileName, PlanerFollowingStopsSoundResolver.FileName, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteLegacyFollowingStopsMp3(rawDir, embeddedDir, AppServices.Routes.Editor.PackageRoot);
            }

            MarkDirty();
            RefreshSpecialBuildingBlocks();
            SelectedSpecialBuildingBlock =
                SpecialBuildingBlocks.FirstOrDefault(b => b.Id == block.Id) ?? SelectedSpecialBuildingBlock;
            var saved = converted ? $"„{targetFileName}“ aus der Audiodatei als WAV gespeichert" : $"„{targetFileName}“ übernommen";
            StatusMessage = voice2
                ? $"{saved}. Petra bleibt. „Speichern & JSON“ fürs Handy."
                : $"{saved} (Rohordner + embeddedSounds). „Speichern & JSON“ fürs Handy-Update.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Zuweisen fehlgeschlagen: {ex.Message}";
        }
    }

    private static bool CopyUnchanged(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
        return false;
    }

    private static void TryDeleteLegacyNextStopMp3(
        string rawDir,
        string embeddedDir,
        System.Text.Json.Nodes.JsonObject packageRoot)
    {
        TryDeleteLegacyAlternateAudio(
            rawDir,
            embeddedDir,
            packageRoot,
            PlanerNextStopMp3SoundResolver.LegacyMp3FileName);
    }

    private static void TryDeleteLegacyFollowingStopsMp3(
        string rawDir,
        string embeddedDir,
        System.Text.Json.Nodes.JsonObject packageRoot)
    {
        TryDeleteLegacyAlternateAudio(
            rawDir,
            embeddedDir,
            packageRoot,
            "Folgende Halte.mp3");
    }

    private static void TryDeleteLegacyAlternateAudio(
        string rawDir,
        string embeddedDir,
        System.Text.Json.Nodes.JsonObject packageRoot,
        string legacyFileName)
    {
        try
        {
            foreach (var dir in new[] { rawDir, embeddedDir })
            {
                var path = Path.Combine(dir, legacyFileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            if (packageRoot["embeddedSounds"] is System.Text.Json.Nodes.JsonObject sounds &&
                sounds.ContainsKey(legacyFileName))
            {
                sounds.Remove(legacyFileName);
            }
        }
        catch
        {
            // optional – Vorschau nutzt ohnehin die WAV zuerst
        }
    }

    [RelayCommand]
    private void PlaySpecialBuildingBlock()
    {
        var block = SelectedSpecialBuildingBlock;
        if (block is null)
        {
            StatusMessage = "Bitte zuerst einen Spezialbaustein wählen.";
            return;
        }

        var path = block.ResolvedPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = $"„{block.FileName}“ fehlt – zuerst Datei zuweisen.";
            return;
        }

        try
        {
            AnnouncementPreviewPlayer.Play(path);
            StatusMessage = $"Vorschau: {block.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Vorschau fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RevealSpecialBuildingBlock()
    {
        var block = SelectedSpecialBuildingBlock;
        if (block?.ResolvedPath is { Length: > 0 } path && File.Exists(path))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
                return;
            }
            catch
            {
                // Fallback Ordner
            }
        }

        OpenRawSoundsFolder();
    }

    [RelayCommand]
    private void StageAllPresentSpecialBuildingBlocks()
    {
        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var editor = AppServices.Routes.Editor;
        var staged = 0;
        var missing = 0;
        foreach (var block in SpecialBuildingBlocks)
        {
            var path = PlanerSpecialBuildingBlocksCatalog.TryResolvePath(
                AppServices.Workspace,
                block.FileName,
                ActiveDropboxFolderPath);
            if (path is null)
            {
                missing++;
                continue;
            }

            if (TryStageStandardSound(editor, path, block.FileName, out _))
            {
                staged++;
            }

            if (block.SupportsVoice2 && !string.IsNullOrWhiteSpace(block.Voice2FileName))
            {
                var voice2Path = PlanerSpecialBuildingBlocksCatalog.TryResolvePath(
                    AppServices.Workspace,
                    block.Voice2FileName,
                    ActiveDropboxFolderPath);
                if (voice2Path is not null &&
                    TryStageStandardSound(editor, voice2Path, block.Voice2FileName, out _))
                {
                    staged++;
                }
            }
        }

        MarkDirty();
        RefreshSpecialBuildingBlocks();
        StatusMessage = missing == 0
            ? $"{staged} Spezialbausteine in embeddedSounds übernommen."
            : $"{staged} übernommen, {missing} fehlen noch (Datei zuweisen).";
    }

    private static PlanerAppSettings LoadSpecialBuildingSettings()
    {
        if (!AppServices.IsInitialized || AppServices.PlanerAppSettings is null)
        {
            return new PlanerAppSettings();
        }

        return AppServices.PlanerAppSettings.Load();
    }

    private static IReadOnlyList<string> LoadExtraSpecialBuildingBlockLines() =>
        LoadSpecialBuildingSettings().SpecialBuildingBlockLines;
}
