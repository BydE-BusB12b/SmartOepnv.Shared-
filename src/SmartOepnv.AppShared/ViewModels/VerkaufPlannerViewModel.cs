using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Verkauf;

namespace SmartOepnv.AppShared.ViewModels;

public partial class VerkaufPlannerViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage =
        "Verkaufshistorie aus Dropbox (verkauf_<Personalnummer>.json). 5×24 Stunden ab dem ersten Verkauf, danach Sperre – Abrechnung jederzeit.";

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private VerkaufDriverDocument? selectedDriver;

    [ObservableProperty] private string depositTotalDisplay = "Einzuzahlen: —";

    public ObservableCollection<VerkaufDriverDocument> Drivers { get; } = [];

    public ObservableCollection<VerkaufSaleRow> SaleRows { get; } = [];

    private readonly Dictionary<string, string> _rawJsonByFile = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<VerkaufDriverDocument> _fileDocs = [];

    public void RefreshHint()
    {
        if (AppServices.Dropbox.Settings.IsConnected)
        {
            _ = LoadFromDropboxAsync();
            return;
        }

        var local = DropboxSyncFolderLocator.FindVerkaufJsonFiles();
        if (local.Count > 0)
        {
            LoadFromLocalPaths(local);
            return;
        }

        ShowDrivers();
        StatusMessage = "Dropbox nicht verbunden – Mitarbeiter aus dem Register, ohne Verkaufsdateien.";
    }

    public void RefreshFromEditor() => ShowDrivers();

    [RelayCommand]
    private async Task LoadFromDropboxAsync()
    {
        if (IsBusy) return;
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            StatusMessage = "Dropbox nicht verbunden.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Lade verkauf_*.json …";
        try
        {
            var names = await AppServices.Dropbox.ListVerkaufFilesAsync().ConfigureAwait(true);
            _rawJsonByFile.Clear();
            _fileDocs.Clear();
            foreach (var name in names)
            {
                try
                {
                    var json = await AppServices.Dropbox.DownloadNamedFileAsync(name).ConfigureAwait(true);
                    _rawJsonByFile[name] = json;
                    var doc = VerkaufDocumentParser.TryParse(name, json);
                    if (doc is not null) _fileDocs.Add(doc);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Fehler bei {name}: {ex.Message}";
                }
            }

            ShowDrivers();
            StatusMessage = DescribeLoaded("Dropbox");
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
        var files = DropboxSyncFolderLocator.FindVerkaufJsonFiles();
        if (files.Count == 0)
        {
            ShowDrivers();
            StatusMessage = "Keine lokalen verkauf_*.json – Mitarbeiter aus dem Register.";
            return;
        }

        LoadFromLocalPaths(files);
    }

    private void LoadFromLocalPaths(IReadOnlyList<string> paths)
    {
        _rawJsonByFile.Clear();
        _fileDocs.Clear();
        foreach (var path in paths)
        {
            try
            {
                var name = Path.GetFileName(path);
                var json = File.ReadAllText(path);
                _rawJsonByFile[name] = json;
                var doc = VerkaufDocumentParser.TryParse(name, json);
                if (doc is not null) _fileDocs.Add(doc);
            }
            catch
            {
                // ignore single file
            }
        }

        ShowDrivers();
        StatusMessage = DescribeLoaded("lokaler Dropbox-Ordner");
    }

    private void ShowDrivers()
    {
        var selectedFile = SelectedDriver?.SourceFileName;
        var selectedPn = SelectedDriver?.PersonnelNumber;
        IEnumerable<EmployeeRosterItem> roster = AppServices.IsInitialized
            ? AppServices.Routes.Editor?.Employees ?? []
            : [];
        var rows = VerkaufDriverListComposer.Compose(_fileDocs, roster);
        Drivers.Clear();
        foreach (var row in rows)
        {
            Drivers.Add(row);
        }

        SelectedDriver = Drivers.FirstOrDefault(d =>
                !string.IsNullOrEmpty(selectedFile) &&
                string.Equals(d.SourceFileName, selectedFile, StringComparison.OrdinalIgnoreCase))
            ?? Drivers.FirstOrDefault(d =>
                !string.IsNullOrEmpty(selectedPn) &&
                EmployeeRosterItem.NormalizePersonnelDigits(d.PersonnelNumber) ==
                EmployeeRosterItem.NormalizePersonnelDigits(selectedPn))
            ?? Drivers.FirstOrDefault();
    }

    private string DescribeLoaded(string source)
    {
        var withFile = _fileDocs.Count;
        if (Drivers.Count == 0)
        {
            return "Keine Mitarbeiter und keine Verkaufsdateien.";
        }

        return $"{Drivers.Count} Mitarbeiter aus dem Register, {withFile} mit Verkaufsdatei ({source}).";
    }

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockDriverAsync()
    {
        if (SelectedDriver is null) return;
        if (!SmartConfirmDialog.ShowConfirm(
                Application.Current?.MainWindow,
                "Verkauf freischalten",
                $"Fahrer {SelectedDriver.Name} ({SelectedDriver.PersonnelNumber}) freischalten?\n" +
                "Verkaufstage der aktuellen Periode werden zurückgesetzt. Historie bleibt in der Datei."))
        {
            return;
        }

        var file = SelectedDriver.SourceFileName;
        if (!_rawJsonByFile.TryGetValue(file, out var json))
        {
            json = VerkaufDocumentParser.BuildEmptyJson(
                SelectedDriver.PersonnelNumber,
                SelectedDriver.Name);
        }

        IsBusy = true;
        try
        {
            var newJson = VerkaufDocumentParser.ApplyUnlock(json, "planer");
            if (AppServices.Dropbox.Settings.IsConnected)
            {
                await AppServices.Dropbox.UploadNamedFileAsync(file, newJson).ConfigureAwait(true);
            }

            _rawJsonByFile[file] = newJson;
            var doc = VerkaufDocumentParser.TryParse(file, newJson);
            if (doc is not null)
            {
                var idx = _fileDocs.FindIndex(d =>
                    string.Equals(d.SourceFileName, file, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _fileDocs[idx] = doc;
                else _fileDocs.Add(doc);
            }

            ShowDrivers();

            StatusMessage = $"Freigeschaltet: {SelectedDriver?.Name} – Periode zurückgesetzt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Freischalten fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUnlock() => SelectedDriver is not null && !IsBusy;

    partial void OnSelectedDriverChanged(VerkaufDriverDocument? value)
    {
        RebuildSaleRows();
        UnlockDriverCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => UnlockDriverCommand.NotifyCanExecuteChanged();

    private void RebuildSaleRows()
    {
        SaleRows.Clear();
        if (SelectedDriver is null)
        {
            DepositTotalDisplay = "Einzuzahlen: —";
            return;
        }

        var period = SelectedDriver.PeriodResetAtMs;
        var depositCents = 0;
        foreach (var s in SelectedDriver.Sales)
        {
            var inPeriod = s.SoldAtMs >= period;
            if (inPeriod && !s.Voided)
            {
                depositCents += s.TotalCents;
            }

            SaleRows.Add(new VerkaufSaleRow
            {
                SoldAtDisplay = FormatMs(s.SoldAtMs),
                SaleNumberDisplay = string.IsNullOrWhiteSpace(s.SaleNumber) ? "—" : s.SaleNumber,
                TotalDisplay = FormatEuro(s.TotalCents),
                LinesDisplay = string.Join(", ", s.Lines.Select(l => $"{l.Qty}× {l.Name}")),
                IsVoided = s.Voided,
                InCurrentPeriod = inPeriod,
                PeriodHint = inPeriod ? "aktuell" : "abgerechnet"
            });
        }

        DepositTotalDisplay = $"Einzuzahlen: {FormatEuro(depositCents)}";
    }

    private static string FormatMs(long ms)
    {
        if (ms <= 0) return "—";
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime()
            .ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));
    }

    private static string FormatEuro(int cents) =>
        (cents / 100m).ToString("0.00 €", CultureInfo.GetCultureInfo("de-DE"));
}

public sealed class VerkaufSaleRow
{
    public string SoldAtDisplay { get; init; } = string.Empty;
    public string SaleNumberDisplay { get; init; } = string.Empty;
    public string TotalDisplay { get; init; } = string.Empty;
    public string LinesDisplay { get; init; } = string.Empty;
    public bool IsVoided { get; init; }
    public bool InCurrentPeriod { get; init; }
    public string PeriodHint { get; init; } = string.Empty;
}
