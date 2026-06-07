using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Views;
using SmartOepnv.AppShared.Zeitwirtschaft;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Zeitwirtschaft;

namespace SmartOepnv.AppShared.ViewModels;

public partial class ZeitwirtschaftPlannerViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage =
        "Zeitwirtschaft aus Dropbox laden – Korrekturen werden zusätzlich gespeichert und nach Dropbox hochgeladen.";

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private ZeitwirtschaftMergedEmployee? selectedEmployee;

    [ObservableProperty] private ZeitwirtschaftTimeTableRow? selectedTimeRow;

    [ObservableProperty] private ZeitwirtschaftMonthOption? selectedMonth;

    [ObservableProperty] private string totalDuration = "—";

    public ObservableCollection<ZeitwirtschaftMergedEmployee> Employees { get; } = [];

    public ObservableCollection<ZeitwirtschaftTimeTableRow> TimeRows { get; } = [];

    public ObservableCollection<ZeitwirtschaftMonthOption> AvailableMonths { get; } = [];

    private readonly Dictionary<string, JsonObject> _documentsByPhone = new(StringComparer.Ordinal);

    private ZeitwirtschaftMergedData? _mergedData;

    private Dictionary<string, string> _vehicleLabels = new(StringComparer.Ordinal);

    public ZeitwirtschaftPlannerViewModel()
    {
        PopulateMonths();
        SelectedMonth = AvailableMonths.FirstOrDefault();
    }

    partial void OnSelectedEmployeeChanged(ZeitwirtschaftMergedEmployee? value) =>
        RefreshTimeRows();

    partial void OnSelectedMonthChanged(ZeitwirtschaftMonthOption? value) =>
        RefreshTimeRows();

    partial void OnSelectedTimeRowChanged(ZeitwirtschaftTimeTableRow? value)
    {
        CorrectTimeCommand.NotifyCanExecuteChanged();
        VoidEntryCommand.NotifyCanExecuteChanged();
    }

    public void RefreshFromEditor()
    {
        RefreshVehicleLabels();
        if (_mergedData is not null)
        {
            RefreshTimeRows();
        }
    }

    public void RefreshHint()
    {
        RefreshVehicleLabels();
        if (AppServices.Dropbox.Settings.IsConnected)
        {
            _ = LoadFromDropboxAsync();
            return;
        }

        var localFiles = DropboxSyncFolderLocator.FindZeitwirtschaftJsonFiles();
        if (localFiles.Count > 0)
        {
            LoadFromLocalFilePaths(localFiles, "lokalem Dropbox-Ordner");
            return;
        }

        StatusMessage = "Dropbox nicht verbunden – bitte zuerst unter Übersicht verbinden.";
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
            StatusMessage = "Dropbox nicht verbunden – bitte zuerst unter Übersicht verbinden.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Lade zeitwirtschaft_*.json aus Dropbox…";
        try
        {
            var folder = AppServices.Dropbox.Settings.FolderPath.TrimEnd('/');
            IReadOnlyList<string> allFiles;
            try
            {
                allFiles = await AppServices.Dropbox.ListAllFileNamesAsync();
            }
            catch
            {
                allFiles = Array.Empty<string>();
            }

            var files = await AppServices.Dropbox.ListZeitwirtschaftFilesAsync();
            _documentsByPhone.Clear();
            var docs = new List<(string FilePhone, string Json)>();
            var loadedPhones = new HashSet<string>(StringComparer.Ordinal);
            var apiCount = 0;
            var localCount = 0;

            foreach (var fileName in files)
            {
                var phone = ZeitwirtschaftMergeService.PhoneFromFileName(fileName) ?? fileName;
                try
                {
                    var json = await AppServices.Dropbox.DownloadNamedFileAsync(fileName);
                    if (TryAddDocument(docs, phone, json))
                    {
                        loadedPhones.Add(phone);
                        apiCount++;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Warnung bei {fileName}: {ex.Message}";
                }
            }

            var localFiles = DropboxSyncFolderLocator.FindZeitwirtschaftJsonFiles(folder);
            foreach (var path in localFiles)
            {
                var fileName = Path.GetFileName(path);
                var phone = ZeitwirtschaftMergeService.PhoneFromFileName(fileName) ?? fileName;
                if (loadedPhones.Contains(phone))
                {
                    continue;
                }

                try
                {
                    var json = await File.ReadAllTextAsync(path);
                    if (TryAddDocument(docs, phone, json))
                    {
                        loadedPhones.Add(phone);
                        localCount++;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Warnung bei {fileName}: {ex.Message}";
                }
            }

            if (docs.Count == 0)
            {
                Employees.Clear();
                TimeRows.Clear();
                _mergedData = null;
                SelectedEmployee = null;
                StatusMessage = BuildEmptyDropboxMessage(folder, allFiles);
                return;
            }

            RebuildMergedData(docs.Count);
            StatusMessage = localCount > 0 && apiCount == 0
                ? BuildLoadedStatusMessage(docs.Count, fromLocal: true, apiCount: 0, localCount: localCount) +
                  " Cloud-API hatte keine Treffer."
                : localCount > 0
                    ? BuildLoadedStatusMessage(docs.Count, fromLocal: false, apiCount: apiCount, localCount: localCount)
                    : BuildLoadedStatusMessage(docs.Count, fromLocal: false, apiCount: docs.Count, localCount: 0);
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

    [RelayCommand(CanExecute = nameof(CanCorrectTime))]
    private void CorrectTime(Window? owner)
    {
        if (SelectedEmployee is null || SelectedTimeRow is null)
        {
            return;
        }

        var entry = SelectedEmployee.Entries.FirstOrDefault(e => e.EntryId == SelectedTimeRow.EntryId);
        if (entry is null)
        {
            StatusMessage = "Eintrag nicht gefunden.";
            return;
        }

        var dialog = new ZeitwirtschaftCorrectionDialog(SelectedTimeRow, entry)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _ = ApplyCorrectionAndUploadAsync(entry, dialog.CorrectedStartMs, dialog.CorrectedEndMs);
    }

    private bool CanCorrectTime() =>
        !IsBusy &&
        SelectedEmployee is not null &&
        SelectedTimeRow is not null &&
        SelectedTimeRow is { IsVoided: false } &&
        AppServices.Dropbox.Settings.IsConnected;

    [RelayCommand(CanExecute = nameof(CanVoidEntry))]
    private void VoidEntry(Window? owner)
    {
        if (SelectedEmployee is null || SelectedTimeRow is null)
        {
            return;
        }

        if (SelectedTimeRow.IsVoided)
        {
            StatusMessage = "Eintrag ist bereits storniert.";
            return;
        }

        var entry = SelectedEmployee.Entries.FirstOrDefault(e => e.EntryId == SelectedTimeRow.EntryId);
        if (entry is null)
        {
            StatusMessage = "Eintrag nicht gefunden.";
            return;
        }

        var dialog = new ZeitwirtschaftVoidDialog(SelectedTimeRow)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _ = ApplyVoidAndUploadAsync(entry, dialog.VoidReason);
    }

    private bool CanVoidEntry() =>
        !IsBusy &&
        SelectedEmployee is not null &&
        SelectedTimeRow is not null &&
        SelectedTimeRow is { IsVoided: false } &&
        AppServices.Dropbox.Settings.IsConnected;

    [RelayCommand(CanExecute = nameof(CanExportPdf))]
    private void ExportPdf()
    {
        if (SelectedEmployee is null || SelectedMonth is null)
        {
            return;
        }

        var rows = BuildRowsForSelectedMonth(SelectedEmployee);
        if (rows.Count == 0)
        {
            StatusMessage = "Keine Einträge für den gewählten Monat.";
            return;
        }

        var monthLabel = new DateTime(SelectedMonth.Year, SelectedMonth.Month, 1)
            .ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var safeName = string.Join("_", SelectedEmployee.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = SelectedEmployee.PersonnelNumber;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"zeitwirtschaft_{safeName}_{monthLabel}.pdf",
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ZeitwirtschaftPdfGenerator.Generate(
                dialog.FileName,
                SelectedEmployee,
                SelectedMonth.Year,
                SelectedMonth.Month,
                rows);
            StatusMessage = $"PDF erstellt: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF-Erstellung fehlgeschlagen: {ex.Message}";
        }
    }

    private bool CanExportPdf() =>
        !IsBusy &&
        SelectedEmployee is not null &&
        SelectedMonth is not null &&
        _mergedData is not null;

    [RelayCommand]
    private void LoadFromLocalFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ordner mit zeitwirtschaft_*.json wählen"
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        try
        {
            var jsonFiles = Directory.GetFiles(dialog.FolderName, "zeitwirtschaft_*.json");
            if (jsonFiles.Length == 0)
            {
                StatusMessage = $"Keine zeitwirtschaft_*.json in „{dialog.FolderName}“ gefunden.";
                return;
            }

            LoadFromLocalFilePaths(jsonFiles, $"Ordner „{dialog.FolderName}“");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lokales Laden fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (_mergedData is null || _mergedData.TotalEntryCount == 0)
        {
            StatusMessage = "Keine Daten zum Export – zuerst aus Dropbox laden.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"zeitwirtschaft_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Personalnummer;Name;Fahrzeug;Kommen;Gehen;Arbeitszeit;Lohnstunden;Storno;StornoGrund;EintragId");
            foreach (var employee in _mergedData.Employees)
            {
                foreach (var row in ZeitwirtschaftMergeService.BuildTableRows(employee, vehicleLabels: _vehicleLabels))
                {
                    sb.Append(Csv(employee.PersonnelNumber)).Append(';')
                        .Append(Csv(employee.Name)).Append(';')
                        .Append(Csv(row.VehicleDisplayName)).Append(';')
                        .Append(Csv(row.Kommen)).Append(';')
                        .Append(Csv(row.Gehen)).Append(';')
                        .Append(Csv(row.Arbeitszeit)).Append(';')
                        .Append(Csv(row.Lohnstunden)).Append(';')
                        .Append(Csv(row.IsVoided ? "ja" : "nein")).Append(';')
                        .Append(Csv(row.VoidReason)).Append(';')
                        .Append(Csv(row.EntryId))
                        .AppendLine();
                }
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = $"CSV exportiert: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV-Export fehlgeschlagen: {ex.Message}";
        }
    }

    private async Task ApplyCorrectionAndUploadAsync(
        ZeitwirtschaftMergedEntry entry,
        long correctedStartMs,
        long? correctedEndMs)
    {
        if (IsBusy)
        {
            return;
        }

        var phone = entry.DevicePhone;
        if (!_documentsByPhone.TryGetValue(phone, out var root))
        {
            StatusMessage = $"Keine JSON für Fahrzeug {phone} geladen.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Speichere Korrektur nach Dropbox…";
        try
        {
            if (!ZeitwirtschaftDocumentEditor.TryApplyCorrection(
                    root,
                    entry.PersonnelNumber,
                    entry.EntryId,
                    correctedStartMs,
                    correctedEndMs,
                    "planer",
                    out var error))
            {
                StatusMessage = error ?? "Korrektur fehlgeschlagen.";
                return;
            }

            var json = ZeitwirtschaftDocumentEditor.Serialize(root);
            var fileName = ZeitwirtschaftMergeService.BuildFileName(phone);
            await AppServices.Dropbox.UploadNamedFileAsync(fileName, json);
            TryWriteLocalSyncCopy(fileName, json);

            RebuildMergedData(_documentsByPhone.Count);
            StatusMessage =
                $"Korrektur in zeitwirtschaft_{phone}.json gespeichert (Fahrzeug {phone}). " +
                "Apps holen alle Fahrzeug-JSONs beim Anmelden per Abgleich.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Speichern fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CorrectTimeCommand.NotifyCanExecuteChanged();
            VoidEntryCommand.NotifyCanExecuteChanged();
            ExportPdfCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ApplyVoidAndUploadAsync(ZeitwirtschaftMergedEntry entry, string voidReason)
    {
        if (IsBusy)
        {
            return;
        }

        var phone = entry.DevicePhone;
        if (!_documentsByPhone.TryGetValue(phone, out var root))
        {
            StatusMessage = $"Keine JSON für Fahrzeug {phone} geladen.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Speichere Storno nach Dropbox…";
        try
        {
            if (!ZeitwirtschaftDocumentEditor.TryApplyVoid(
                    root,
                    entry.PersonnelNumber,
                    entry.EntryId,
                    voidReason,
                    "planer",
                    out var error))
            {
                StatusMessage = error ?? "Storno fehlgeschlagen.";
                return;
            }

            var json = ZeitwirtschaftDocumentEditor.Serialize(root);
            var fileName = ZeitwirtschaftMergeService.BuildFileName(phone);
            await AppServices.Dropbox.UploadNamedFileAsync(fileName, json);
            TryWriteLocalSyncCopy(fileName, json);

            RebuildMergedData(_documentsByPhone.Count);
            StatusMessage =
                $"Storno in zeitwirtschaft_{phone}.json gespeichert (Grund: {voidReason.Trim()}). " +
                "Apps holen alle Fahrzeug-JSONs beim Anmelden per Abgleich.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Speichern fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CorrectTimeCommand.NotifyCanExecuteChanged();
            VoidEntryCommand.NotifyCanExecuteChanged();
            ExportPdfCommand.NotifyCanExecuteChanged();
        }
    }

    private void RebuildMergedData(int sourceFileCount)
    {
        var docs = _documentsByPhone
            .Select(kvp => (kvp.Key, Json: kvp.Value.ToJsonString()))
            .ToList();
        _mergedData = ZeitwirtschaftMergeService.MergeDocuments(docs);
        _mergedData = new ZeitwirtschaftMergedData
        {
            Employees = _mergedData.Employees,
            SourceFileCount = sourceFileCount,
            TotalEntryCount = _mergedData.TotalEntryCount
        };

        var selectedPersonnel = SelectedEmployee?.PersonnelNumber;
        Employees.Clear();
        foreach (var employee in _mergedData.Employees)
        {
            Employees.Add(employee);
        }

        SelectedEmployee = selectedPersonnel is not null
            ? Employees.FirstOrDefault(e => e.PersonnelNumber == selectedPersonnel) ?? Employees.FirstOrDefault()
            : Employees.FirstOrDefault();

        RefreshTimeRows();
    }

    private void RefreshTimeRows()
    {
        TimeRows.Clear();
        SelectedTimeRow = null;
        if (SelectedEmployee is null)
        {
            TotalDuration = "—";
            CorrectTimeCommand.NotifyCanExecuteChanged();
            VoidEntryCommand.NotifyCanExecuteChanged();
            ExportPdfCommand.NotifyCanExecuteChanged();
            return;
        }

        var rows = BuildRowsForSelectedMonth(SelectedEmployee).ToList();
        foreach (var row in rows)
        {
            TimeRows.Add(row);
        }

        TotalDuration = ZeitwirtschaftMergeService.SumDurationHhMm(rows);

        CorrectTimeCommand.NotifyCanExecuteChanged();
        VoidEntryCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<ZeitwirtschaftTimeTableRow> BuildRowsForSelectedMonth(
        ZeitwirtschaftMergedEmployee employee)
    {
        if (SelectedMonth is null)
        {
            return ZeitwirtschaftMergeService.BuildTableRows(employee, vehicleLabels: _vehicleLabels);
        }

        return ZeitwirtschaftMergeService.BuildTableRows(
            employee,
            SelectedMonth.Year,
            SelectedMonth.Month,
            _vehicleLabels);
    }

    private void RefreshVehicleLabels()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            _vehicleLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            return;
        }

        _vehicleLabels = ZeitwirtschaftVehicleLabelResolver.BuildLabelMap(
            editor.RegisteredVehicles,
            editor.RegisteredVehiclePhoneRedirects);
    }

    private void PopulateMonths()
    {
        AvailableMonths.Clear();
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var cursor = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (var i = 0; i < 24; i++)
        {
            AvailableMonths.Add(new ZeitwirtschaftMonthOption
            {
                Year = cursor.Year,
                Month = cursor.Month,
                Label = cursor.ToString("MMMM yyyy", culture)
            });
            cursor = cursor.AddMonths(-1);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        CorrectTimeCommand.NotifyCanExecuteChanged();
        VoidEntryCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    private void LoadFromLocalFilePaths(IReadOnlyList<string> jsonFiles, string sourceLabel)
    {
        _documentsByPhone.Clear();
        var docs = new List<(string FilePhone, string Json)>();
        foreach (var path in jsonFiles)
        {
            var fileName = Path.GetFileName(path);
            var phone = ZeitwirtschaftMergeService.PhoneFromFileName(fileName) ?? fileName;
            try
            {
                var json = File.ReadAllText(path);
                TryAddDocument(docs, phone, json);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Warnung bei {fileName}: {ex.Message}";
            }
        }

        if (docs.Count == 0)
        {
            StatusMessage = $"Keine gültigen zeitwirtschaft_*.json in {sourceLabel} gefunden.";
            return;
        }

        RebuildMergedData(docs.Count);
        StatusMessage = $"Aus {sourceLabel}: {BuildLoadedStatusMessage(docs.Count, fromLocal: true, apiCount: 0, localCount: docs.Count)}";
    }

    private string BuildLoadedStatusMessage(int vehicleFileCount, bool fromLocal, int apiCount, int localCount)
    {
        var vehicleLabel = vehicleFileCount == 1 ? "1 Fahrzeug" : $"{vehicleFileCount} Fahrzeuge";
        var source = fromLocal
            ? "lokalem Dropbox-Ordner"
            : localCount > 0
                ? $"{apiCount} Cloud + {localCount} lokal"
                : "Dropbox";
        return $"{vehicleLabel} zusammengeführt ({source}) – " +
               $"{_mergedData!.TotalEntryCount} Einträge, {Employees.Count} Fahrer, chronologisch pro Fahrer.";
    }

    private static void TryWriteLocalSyncCopy(string fileName, string json)
    {
        var syncFolder = DropboxSyncFolderLocator.TryResolveSmartOepnvFolder(
            AppServices.Dropbox.Settings.FolderPath);
        if (syncFolder is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(Path.Combine(syncFolder, fileName), json, new UTF8Encoding(false));
        }
        catch
        {
            // Lokale Kopie ist Zusatz – Cloud-Upload ist maßgeblich.
        }
    }

    private bool TryAddDocument(List<(string FilePhone, string Json)> docs, string phone, string json)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        if (root is null ||
            !string.Equals(root["type"]?.GetValue<string>(), ZeitwirtschaftMergeService.DocumentType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        docs.Add((phone, json));
        _documentsByPhone[phone] = root;
        return true;
    }

    private static string BuildEmptyDropboxMessage(string folder, IReadOnlyList<string> allFiles)
    {
        if (allFiles.Count == 0)
        {
            return $"Keine Dateien in Dropbox-Ordner „{folder}“ lesbar. " +
                   "Bitte unter Einstellungen Dropbox testen und Ordnerpfad prüfen (z. B. /App/Smart ÖPNV). " +
                   "Alternativ „Aus Ordner laden“ für einen lokalen Dropbox-Sync-Ordner.";
        }

        var syncFolder = DropboxSyncFolderLocator.TryResolveSmartOepnvFolder(folder);
        if (syncFolder is not null)
        {
            return $"Keine zeitwirtschaft_*.json lesbar (API-Ordner „{folder}“, {allFiles.Count} andere Datei(en)). " +
                   $"Lokaler Sync-Ordner: „{syncFolder}“ – bitte „Aus Dropbox laden“ erneut oder „Aus Ordner laden“.";
        }

        return $"Keine zeitwirtschaft_*.json in „{folder}“ ({allFiles.Count} andere Datei(en)). " +
               "Alternativ „Aus Ordner laden“ mit dem Dropbox-Sync-Ordner auf diesem PC.";
    }

    private static string Csv(string? value)
    {
        var s = value ?? string.Empty;
        if (s.Contains('"') || s.Contains(';') || s.Contains('\n'))
        {
            return '"' + s.Replace("\"", "\"\"") + '"';
        }

        return s;
    }
}
