using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class DisplaysOperationsViewModel : ObservableObject, IEditorAreaViewModel
{
    private readonly List<string> _preservedRawEntries = [];
    private bool _hasUnsavedChanges;
    private int _loadedRevision = -1;
    private OutsideDisplayProgram? _subscribedOutsideProgram;

    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private DateBasedHintItem? selectedHint;
    [ObservableProperty] private OutsideDisplayProgram? selectedOutsideProgram;
    [ObservableProperty] private string newHintText = string.Empty;
    [ObservableProperty] private string newHintStartDate = string.Empty;
    [ObservableProperty] private string newHintEndDate = string.Empty;

    public ObservableCollection<DateBasedHintItem> DateBasedHints { get; } = [];
    public ObservableCollection<OutsideDisplayProgram> OutsidePrograms { get; } = [];

    public bool HasPendingChanges => _hasUnsavedChanges;

    public void RefreshFromEditorIfNeeded()
    {
        if (!_hasUnsavedChanges &&
            _loadedRevision == AppServices.Routes.EditorDataRevision &&
            (OutsidePrograms.Count > 0 || DateBasedHints.Count > 0))
        {
            return;
        }

        RefreshFromEditor();
    }

    public void CommitChangesIfDirty() => CommitChanges();

    public void RefreshFromEditor()
    {
        DateBasedHints.Clear();
        OutsidePrograms.Clear();
        _preservedRawEntries.Clear();
        _hasUnsavedChanges = false;
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
            else if (!string.IsNullOrWhiteSpace(entry))
            {
                _preservedRawEntries.Add(entry.Trim());
            }
        }

        SelectedHint = DateBasedHints.FirstOrDefault();
        SelectedOutsideProgram = OutsidePrograms.FirstOrDefault();
        var enabledCount = OutsidePrograms.Count(p => p.IsListEnabled);
        StatusMessage =
            $"{OutsidePrograms.Count} Zielanzeigen ({enabledCount} in ITCS-Liste), {DateBasedHints.Count} Hinweise – mit Dropbox übertragbar. Linienführung über Navidaten.";
        _loadedRevision = AppServices.Routes.EditorDataRevision;
    }

    public void CommitChanges(bool force = false)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        if (!force && !_hasUnsavedChanges)
        {
            return;
        }

        editor.ReplaceDateBasedHints(DateBasedHints.ToList());
        foreach (var program in OutsidePrograms)
        {
            program.ApplyStartTargetName();
        }

        var outsideEntries = OutsidePrograms
            .Select(p => p.ToStorageEntry())
            .Concat(_preservedRawEntries)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();
        editor.ReplaceOutsideDisplays(outsideEntries);
        AppServices.Routes.ApplyEditorChanges("anzeigen-hinweise");
        _hasUnsavedChanges = false;
        StatusMessage =
            $"Gespeichert – {OutsidePrograms.Count} Zielanzeigen, {DateBasedHints.Count} Hinweise (Dropbox/Handy).";
    }

    private void MarkDirty()
    {
        _hasUnsavedChanges = true;
    }

    partial void OnSelectedOutsideProgramChanged(OutsideDisplayProgram? value)
    {
        if (_subscribedOutsideProgram is not null)
        {
            _subscribedOutsideProgram.PropertyChanged -= OnOutsideProgramPropertyChanged;
        }

        _subscribedOutsideProgram = value;

        if (value is not null)
        {
            value.PropertyChanged += OnOutsideProgramPropertyChanged;
        }
    }

    private void OnOutsideProgramPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutsideDisplayProgram.IsListEnabled))
        {
            MarkDirty();
            var enabledCount = OutsidePrograms.Count(p => p.IsListEnabled);
            StatusMessage =
                $"{OutsidePrograms.Count} Zielanzeigen ({enabledCount} in ITCS-Liste), {DateBasedHints.Count} Hinweise – mit Dropbox übertragbar.";
        }
    }

    [RelayCommand]
    private void Save() => CommitChanges(force: true);

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
        MarkDirty();
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
        MarkDirty();
        StatusMessage = "Hinweis entfernt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void AddOutsideProgramDs021t()
    {
        var program = OutsideDisplayProgram.CreateDs021t($"Ziel {OutsidePrograms.Count + 1}");
        OutsidePrograms.Add(program);
        SelectedOutsideProgram = program;
        MarkDirty();
        StatusMessage = "Neue Zielanzeige (DS021T) – Texte anpassen und speichern.";
    }

    [RelayCommand]
    private void AddOutsideProgramKrefeld()
    {
        var program = OutsideDisplayProgram.CreateKrefeld($"Ziel {OutsidePrograms.Count + 1}");
        OutsidePrograms.Add(program);
        SelectedOutsideProgram = program;
        MarkDirty();
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
        MarkDirty();
        StatusMessage = "Zielanzeige entfernt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void EnableAllInItcsList()
    {
        if (OutsidePrograms.Count == 0)
        {
            StatusMessage = "Keine Zielanzeigen vorhanden.";
            return;
        }

        foreach (var program in OutsidePrograms)
        {
            program.IsListEnabled = true;
        }

        MarkDirty();
        StatusMessage =
            $"Alle {OutsidePrograms.Count} Zielanzeigen in der ITCS-Liste aktiviert – „Speichern“ und Dropbox-Export nicht vergessen.";
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

