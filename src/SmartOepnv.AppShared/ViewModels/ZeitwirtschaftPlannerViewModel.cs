using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.Core;
using SmartOepnv.Core.Zeitwirtschaft;

namespace SmartOepnv.AppShared.ViewModels;

public partial class ZeitwirtschaftPlannerViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage =
        "Zeitwirtschaft aus Dropbox laden – alle zeitwirtschaft_*.json werden zusammengeführt.";

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private ZeitwirtschaftMergedEmployee? selectedEmployee;

    public ObservableCollection<ZeitwirtschaftMergedEmployee> Employees { get; } = [];

    public ObservableCollection<ZeitwirtschaftTimeTableRow> TimeRows { get; } = [];

    private ZeitwirtschaftMergedData? _mergedData;

    partial void OnSelectedEmployeeChanged(ZeitwirtschaftMergedEmployee? value) =>
        RefreshTimeRows();

    public void RefreshHint()
    {
        if (AppServices.Dropbox.Settings.IsConnected)
        {
            _ = LoadFromDropboxAsync();
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
            var files = await AppServices.Dropbox.ListZeitwirtschaftFilesAsync();
            if (files.Count == 0)
            {
                Employees.Clear();
                TimeRows.Clear();
                _mergedData = null;
                SelectedEmployee = null;
                StatusMessage = "Keine zeitwirtschaft_*.json im Dropbox-Ordner gefunden.";
                return;
            }

            var docs = new List<(string FilePhone, string Json)>();
            foreach (var fileName in files)
            {
                var phone = ZeitwirtschaftMergeService.PhoneFromFileName(fileName) ?? fileName;
                try
                {
                    var json = await AppServices.Dropbox.DownloadNamedFileAsync(fileName);
                    docs.Add((phone, json));
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Warnung bei {fileName}: {ex.Message}";
                }
            }

            _mergedData = ZeitwirtschaftMergeService.MergeDocuments(docs);
            Employees.Clear();
            foreach (var employee in _mergedData.Employees)
            {
                Employees.Add(employee);
            }

            SelectedEmployee = Employees.FirstOrDefault();
            RefreshTimeRows();

            StatusMessage =
                $"{_mergedData.SourceFileCount} JSON-Datei(en), {_mergedData.TotalEntryCount} Einträge, {Employees.Count} Mitarbeiter.";
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
            sb.AppendLine("Personalnummer;Name;Fahrzeug;Kommen;Gehen;Arbeitszeit;Lohnstunden;EintragId");
            foreach (var employee in _mergedData.Employees)
            {
                foreach (var row in ZeitwirtschaftMergeService.BuildTableRows(employee))
                {
                    sb.Append(Csv(employee.PersonnelNumber)).Append(';')
                        .Append(Csv(employee.Name)).Append(';')
                        .Append(Csv(row.VehiclePhone)).Append(';')
                        .Append(Csv(row.Kommen)).Append(';')
                        .Append(Csv(row.Gehen)).Append(';')
                        .Append(Csv(row.Arbeitszeit)).Append(';')
                        .Append(Csv(row.Lohnstunden)).Append(';')
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

    private void RefreshTimeRows()
    {
        TimeRows.Clear();
        if (SelectedEmployee is null)
        {
            return;
        }

        foreach (var row in ZeitwirtschaftMergeService.BuildTableRows(SelectedEmployee))
        {
            TimeRows.Add(row);
        }
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
