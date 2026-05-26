using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class DisplaysOperationsViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private DateBasedHintItem? selectedHint;
    [ObservableProperty] private OutsideDisplayProgram? selectedOutsideProgram;
    [ObservableProperty] private string newHintText = string.Empty;
    [ObservableProperty] private string newHintStartDate = string.Empty;
    [ObservableProperty] private string newHintEndDate = string.Empty;

    public ObservableCollection<DateBasedHintItem> DateBasedHints { get; } = [];
    public ObservableCollection<OutsideDisplayProgram> OutsidePrograms { get; } = [];

    public DisplaysOperationsViewModel()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChanges);
        }
    }

    public void RefreshFromEditor()
    {
        DateBasedHints.Clear();
        OutsidePrograms.Clear();
        SelectedHint = null;
        SelectedOutsideProgram = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        foreach (var hint in editor.DateBasedHints)
        {
            DateBasedHints.Add(hint);
        }

        foreach (var entry in editor.OutsideDisplays)
        {
            var program = OutsideDisplayProgram.TryParse(entry);
            if (program is not null)
            {
                OutsidePrograms.Add(program);
            }
        }

        SelectedHint = DateBasedHints.FirstOrDefault();
        SelectedOutsideProgram = OutsidePrograms.FirstOrDefault();
        StatusMessage =
            $"{OutsidePrograms.Count} Zielanzeigen, {DateBasedHints.Count} Hinweise – mit Dropbox übertragbar. Linienführung über Navidaten.";
    }

    public void CommitChanges()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        editor.ReplaceDateBasedHints(DateBasedHints.ToList());
        foreach (var program in OutsidePrograms)
        {
            program.ApplyStartTargetName();
        }

        editor.ReplaceOutsideDisplays(
            OutsidePrograms.Select(p => p.ToStorageEntry()).ToList());
        AppServices.Routes.ApplyEditorChanges("anzeigen-hinweise");
        StatusMessage =
            $"Gespeichert – {OutsidePrograms.Count} Zielanzeigen, {DateBasedHints.Count} Hinweise (Dropbox/Handy).";
    }

    [RelayCommand]
    private void Save() => CommitChanges();

    [RelayCommand]
    private void AddDateBasedHint()
    {
        var text = NewHintText.Trim();
        if (text.Length == 0)
        {
            StatusMessage = "Bitte Hinweistext eingeben.";
            return;
        }

        if (!TryParseGermanDate(NewHintStartDate, out var start) || !TryParseGermanDate(NewHintEndDate, out var end))
        {
            StatusMessage = "Datum im Format TT.MM.JJJJ (z. B. 01.06.2026).";
            return;
        }

        if (end < start)
        {
            StatusMessage = "Enddatum darf nicht vor Startdatum liegen.";
            return;
        }

        var hint = new DateBasedHintItem
        {
            HintText = text,
            StartDate = NewHintStartDate.Trim(),
            EndDate = NewHintEndDate.Trim()
        };
        DateBasedHints.Add(hint);
        SelectedHint = hint;
        NewHintText = string.Empty;
        StatusMessage = "Hinweis hinzugefügt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void RemoveDateBasedHint()
    {
        if (SelectedHint is null)
        {
            return;
        }

        DateBasedHints.Remove(SelectedHint);
        SelectedHint = DateBasedHints.FirstOrDefault();
        StatusMessage = "Hinweis entfernt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void AddOutsideProgramDs021t()
    {
        var program = OutsideDisplayProgram.CreateDs021t($"Ziel {OutsidePrograms.Count + 1}");
        OutsidePrograms.Add(program);
        SelectedOutsideProgram = program;
        StatusMessage = "Neue Zielanzeige (DS021T) – Texte anpassen und speichern.";
    }

    [RelayCommand]
    private void AddOutsideProgramKrefeld()
    {
        var program = OutsideDisplayProgram.CreateKrefeld($"Ziel {OutsidePrograms.Count + 1}");
        OutsidePrograms.Add(program);
        SelectedOutsideProgram = program;
        StatusMessage = "Neue Zielanzeige (DS003a Krefeld) – Texte anpassen und speichern.";
    }

    [RelayCommand]
    private void RemoveOutsideProgram()
    {
        if (SelectedOutsideProgram is null)
        {
            return;
        }

        OutsidePrograms.Remove(SelectedOutsideProgram);
        SelectedOutsideProgram = OutsidePrograms.FirstOrDefault();
        StatusMessage = "Zielanzeige entfernt – „Speichern“ nicht vergessen.";
    }

    private static bool TryParseGermanDate(string input, out DateTime date)
    {
        return DateTime.TryParseExact(
            input.Trim(),
            "dd.MM.yyyy",
            CultureInfo.GetCultureInfo("de-DE"),
            DateTimeStyles.None,
            out date);
    }
}
