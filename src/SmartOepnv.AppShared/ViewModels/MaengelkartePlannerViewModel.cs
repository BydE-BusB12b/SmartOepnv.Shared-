using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.Maengelkarte;
using SmartOepnv.Core.Zeitwirtschaft;

namespace SmartOepnv.AppShared.ViewModels;

public partial class MaengelkartePlannerViewModel : ObservableObject
{
    public const string AllVehiclesFilter = "Alle Fahrzeuge";

    [ObservableProperty] private string statusMessage =
        "Mängel aus der Fahrer-App laden – Status hier bearbeiten und nach Dropbox speichern.";

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private bool showResolved = true;

    [ObservableProperty] private string selectedVehicleFilter = AllVehiclesFilter;

    [ObservableProperty] private MaengelkarteEntry? selectedEntry;

    public ObservableCollection<MaengelkarteEntry> Entries { get; } = [];

    public ObservableCollection<string> VehicleFilterOptions { get; } = [AllVehiclesFilter];

    public int NewEntryCount => MaengelkarteMergeService.CountNew(_document);

    private MaengelkarteDocument _document = MaengelkarteMergeService.EmptyDocument();

    private bool _isUpdatingVehicleFilter;

    partial void OnShowResolvedChanged(bool value) => RefreshEntriesList();

    partial void OnSelectedVehicleFilterChanged(string value)
    {
        if (_isUpdatingVehicleFilter)
        {
            return;
        }

        RefreshEntriesList();
    }

    partial void OnSelectedEntryChanged(MaengelkarteEntry? value)
    {
        SetStatusInProgressCommand.NotifyCanExecuteChanged();
        SetStatusResolvedCommand.NotifyCanExecuteChanged();
        SetStatusNewCommand.NotifyCanExecuteChanged();
    }

    public void RefreshHint()
    {
        if (AppServices.Dropbox.Settings.IsConnected)
        {
            _ = LoadFromDropboxAsync();
            return;
        }

        var localSync = DropboxSyncFolderLocator.FindMaengelkarteJsonFile(AppServices.Dropbox.Settings.FolderPath);
        if (localSync is not null)
        {
            LoadFromPath(localSync, "lokalem Dropbox-Ordner");
            return;
        }

        var workspace = MaengelkarteMergeService.TryLoadLocal(AppServices.SettingsSubfolder);
        if (workspace is not null)
        {
            ApplyDocument(workspace, "lokaler Arbeitskopie");
            return;
        }

        StatusMessage = "Dropbox nicht verbunden – bitte zuerst unter Übersicht/Einstellungen verbinden.";
    }

    [RelayCommand]
    private async Task LoadFromDropboxAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            StatusMessage = "Dropbox nicht verbunden – bitte zuerst verbinden.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Lade maengelkarte.json aus Dropbox…";
        try
        {
            string? remoteJson = null;
            try
            {
                remoteJson = await AppServices.Dropbox.DownloadNamedFileAsync(DropboxConstants.MaengelkarteFileName);
            }
            catch
            {
                // Datei existiert noch nicht
            }

            var remote = MaengelkarteMergeService.TryParse(remoteJson);
            var local = MaengelkarteMergeService.TryLoadLocal(AppServices.SettingsSubfolder);
            var merged = MaengelkarteMergeService.Merge(local, remote);
            MaengelkarteMergeService.SaveLocal(AppServices.SettingsSubfolder, merged);
            ApplyDocument(merged, "Dropbox");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Laden fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void LoadFromLocalFolder()
    {
        var path = DropboxSyncFolderLocator.FindMaengelkarteJsonFile(AppServices.Dropbox.Settings.FolderPath);
        if (path is not null)
        {
            LoadFromPath(path, "lokalem Dropbox-Ordner");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "maengelkarte.json wählen",
            Filter = "JSON|*.json|Alle|*.*",
            FileName = DropboxConstants.MaengelkarteFileName
        };
        if (dialog.ShowDialog() == true)
        {
            LoadFromPath(dialog.FileName, "Datei");
        }
        else
        {
            StatusMessage = "Keine maengelkarte.json im lokalen Dropbox-Ordner gefunden.";
        }
    }

    [RelayCommand]
    private async Task SaveToDropboxAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            StatusMessage = "Dropbox nicht verbunden – bitte zuerst verbinden.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Speichere maengelkarte.json nach Dropbox…";
        try
        {
            string? remoteJson = null;
            try
            {
                remoteJson = await AppServices.Dropbox.DownloadNamedFileAsync(DropboxConstants.MaengelkarteFileName);
            }
            catch
            {
                // neu anlegen
            }

            var remote = MaengelkarteMergeService.TryParse(remoteJson);
            var merged = MaengelkarteMergeService.Merge(_document, remote);
            var json = MaengelkarteMergeService.Serialize(merged);
            await AppServices.Dropbox.UploadNamedFileAsync(DropboxConstants.MaengelkarteFileName, json);
            MaengelkarteMergeService.SaveLocal(AppServices.SettingsSubfolder, merged);
            ApplyDocument(merged, "Dropbox (gespeichert)");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Speichern fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private void SetStatusInProgress() => SetSelectedStatus(MaengelkarteStatus.InProgress);

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private void SetStatusResolved() => SetSelectedStatus(MaengelkarteStatus.Resolved);

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private void SetStatusNew() => SetSelectedStatus(MaengelkarteStatus.New);

    private bool HasSelectedEntry() => SelectedEntry is not null;

    private void SetSelectedStatus(string status)
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var selectedId = SelectedEntry.Id;
        MaengelkarteMergeService.SetStatus(SelectedEntry, status);
        RefreshEntriesList();
        SelectedEntry = Entries.FirstOrDefault(e => e.Id == selectedId);
        OnPropertyChanged(nameof(NewEntryCount));
        StatusMessage = $"Status geändert: {MaengelkarteStatus.Label(status)} – bitte speichern.";
    }

    private void LoadFromPath(string path, string sourceLabel)
    {
        try
        {
            var parsed = MaengelkarteMergeService.TryParse(File.ReadAllText(path));
            if (parsed is null)
            {
                StatusMessage = "Ungültige maengelkarte.json.";
                return;
            }

            var local = MaengelkarteMergeService.TryLoadLocal(AppServices.SettingsSubfolder);
            var merged = MaengelkarteMergeService.Merge(local, parsed);
            MaengelkarteMergeService.SaveLocal(AppServices.SettingsSubfolder, merged);
            ApplyDocument(merged, sourceLabel);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Laden fehlgeschlagen: {ex.Message}";
        }
    }

    public void RefreshVehicleFilterOptions()
    {
        if (_isUpdatingVehicleFilter)
        {
            return;
        }

        _isUpdatingVehicleFilter = true;
        try
        {
            RefreshVehicleFilterOptionsCore();
        }
        finally
        {
            _isUpdatingVehicleFilter = false;
        }
    }

    private void ApplyDocument(MaengelkarteDocument document, string sourceLabel)
    {
        _document = document;
        RefreshVehicleFilterOptions();
        RefreshEntriesList();
        OnPropertyChanged(nameof(NewEntryCount));
        var openCount = document.Entries.Count(e => e.Status != MaengelkarteStatus.Resolved);
        StatusMessage = $"{openCount} offene Mängel, {NewEntryCount} neu – geladen aus {sourceLabel}.";
    }

    private void RefreshEntriesList()
    {
        var editor = AppServices.Routes.Editor;
        MaengelkarteVehicleLabelResolver.EnrichVehicleDisplay(
            _document.Entries,
            editor?.RegisteredVehicles,
            editor?.RegisteredVehiclePhoneRedirects);

        var filterByVehicle = !string.IsNullOrWhiteSpace(SelectedVehicleFilter) &&
                              !string.Equals(SelectedVehicleFilter, AllVehiclesFilter, StringComparison.Ordinal);

        Entries.Clear();
        foreach (var entry in _document.Entries)
        {
            if (!ShowResolved && entry.Status == MaengelkarteStatus.Resolved)
            {
                continue;
            }

            if (filterByVehicle &&
                !string.Equals(entry.VehicleDisplay, SelectedVehicleFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Entries.Add(entry);
        }
    }

    private void RefreshVehicleFilterOptionsCore()
    {
        var previous = SelectedVehicleFilter;
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _document.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.VehicleDisplay) &&
                !string.Equals(entry.VehicleDisplay, "—", StringComparison.Ordinal))
            {
                labels.Add(entry.VehicleDisplay.Trim());
            }
        }

        var editor = AppServices.Routes.Editor;
        if (editor?.RegisteredVehicles is not null)
        {
            var labelMap = ZeitwirtschaftVehicleLabelResolver.BuildLabelMap(
                editor.RegisteredVehicles,
                editor.RegisteredVehiclePhoneRedirects);
            foreach (var label in labelMap.Values)
            {
                if (!string.IsNullOrWhiteSpace(label))
                {
                    labels.Add(label.Trim());
                }
            }
        }

        VehicleFilterOptions.Clear();
        VehicleFilterOptions.Add(AllVehiclesFilter);
        foreach (var label in labels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            VehicleFilterOptions.Add(label);
        }

        var next = !string.IsNullOrWhiteSpace(previous) &&
                   VehicleFilterOptions.Contains(previous)
            ? previous
            : AllVehiclesFilter;
        if (!string.Equals(SelectedVehicleFilter, next, StringComparison.Ordinal))
        {
            SelectedVehicleFilter = next;
        }
    }
}
