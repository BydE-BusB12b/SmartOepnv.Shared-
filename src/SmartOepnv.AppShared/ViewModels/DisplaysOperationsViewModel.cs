using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Pdf;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public enum DisplaysOperationsButtonState
{
    Idle,
    Success
}

public partial class DisplaysOperationsViewModel : ObservableObject, IEditorAreaViewModel
{
    private const int SuccessFeedbackMs = 5000;

    private readonly List<string> _preservedRawEntries = [];
    private bool _hasUnsavedChanges;
    private bool _committingChanges;
    private int _loadedRevision = -1;
    private OutsideDisplayProgram? _subscribedOutsideProgram;
    private string? _selectedProgramIdSnapshot;
    private int? _selectedDestinationNumberSnapshot;
    private bool _isSortingPrograms;
    private bool _suppressIdCollisionCheck;
    private CancellationTokenSource? _saveFeedbackCts;
    private readonly SearchQueryDebouncer _outsideSearchDebouncer;

    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private DateBasedHintItem? selectedHint;
    [ObservableProperty] private OutsideDisplayProgram? selectedOutsideProgram;
    [ObservableProperty] private int selectedWechseltextIndex;
    [ObservableProperty] private string newHintText = string.Empty;
    [ObservableProperty] private string newHintStartDate = string.Empty;
    [ObservableProperty] private string newHintEndDate = string.Empty;
    [ObservableProperty] private DisplaysOperationsButtonState saveButtonState = DisplaysOperationsButtonState.Idle;
    [ObservableProperty] private int selectedTabIndex;
    [ObservableProperty] private string outsideProgramSearchText = string.Empty;
    [ObservableProperty] private string selectedProtocolFilter = "Alle";
    [ObservableProperty] private string selectedOutsideProgramSort = "ID ↑";

    public ObservableCollection<DateBasedHintItem> DateBasedHints { get; } = [];
    public ObservableCollection<OutsideDisplayProgram> OutsidePrograms { get; } = [];

    /// <summary>Gefilterte Zielliste (Suche + Protokoll).</summary>
    public ICollectionView OutsideProgramsView { get; }

    public IReadOnlyList<string> ProtocolFilterOptions { get; } =
    [
        "Alle",
        "Mobitec",
        "DS021T",
        "DS021neu",
        "FMA-S1",
        "DS003",
        "DS003a Krefeld"
    ];

    public IReadOnlyList<string> OutsideProgramSortOptions { get; } =
    [
        "ID ↑",
        "ID ↓",
        "Zielnummer ↑",
        "Zielnummer ↓",
        "Linie ↑",
        "Linie ↓",
        "Name ↑",
        "Name ↓"
    ];

    public bool HasPendingChanges => _hasUnsavedChanges;

    public DisplaysOperationsViewModel()
    {
        OutsideProgramsView = CollectionViewSource.GetDefaultView(OutsidePrograms);
        OutsideProgramsView.Filter = FilterOutsideProgram;
        ApplyOutsideProgramSort();
        _outsideSearchDebouncer = new SearchQueryDebouncer(RefreshOutsideProgramsView, delayMilliseconds: 150);

        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChangesIfDirty);
        }
    }

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
            StatusMessage = "Kein Route-Paket – unter Routen/Haltestellen zuerst anlegen oder Planer neu starten.";
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

        SortOutsidePrograms();

        if (OutsideDisplayId.AssignUniqueFourDigitIds(OutsidePrograms))
        {
            MarkDirty();
        }

        SelectedHint = DateBasedHints.FirstOrDefault();
        SelectedOutsideProgram = OutsidePrograms.FirstOrDefault();
        var enabledCount = OutsidePrograms.Count(p => p.IsListEnabled);
        StatusMessage =
            $"{OutsidePrograms.Count} Zielanzeigen ({enabledCount} in ITCS-Liste), {DateBasedHints.Count} Hinweise – mit Dropbox übertragbar. Linienführung über Navidaten.";
        _loadedRevision = AppServices.Routes.EditorDataRevision;
    }

    public void CommitChanges(bool force = false, bool showSuccessFeedback = false)
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

        _committingChanges = true;
        try
        {
            editor.ReplaceDateBasedHints(DateBasedHints.ToList());
            SortOutsidePrograms();

            var programs = OutsidePrograms.ToList();
            foreach (var program in programs)
            {
                program.ApplyStartTargetName();
            }

            var outsideEntries = programs
                .Select(p => p.ToStorageEntry())
                .Concat(_preservedRawEntries)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .ToList();
            editor.ReplaceOutsideDisplays(outsideEntries);
            OutsideDisplayDestinationResolver.SyncStopLinks(editor);
            AppServices.Routes.ApplyEditorChanges("anzeigen-hinweise", rebuildEmbeddedMedia: false);
            _hasUnsavedChanges = false;
            RefreshOutsideProgramListLabels();
            var enabledCount = OutsidePrograms.Count(p => p.IsListEnabled);
            StatusMessage =
                $"Gespeichert – {OutsidePrograms.Count} Zielanzeigen ({enabledCount} in ITCS-Liste), {DateBasedHints.Count} Hinweise (Dropbox/Handy).";

            if (showSuccessFeedback)
            {
                _ = ShowSaveSuccessFeedbackAsync();
            }
        }
        finally
        {
            _committingChanges = false;
        }
    }

    private async Task ShowSaveSuccessFeedbackAsync()
    {
        _saveFeedbackCts?.Cancel();
        _saveFeedbackCts?.Dispose();
        _saveFeedbackCts = new CancellationTokenSource();
        var token = _saveFeedbackCts.Token;

        SaveButtonState = DisplaysOperationsButtonState.Success;
        try
        {
            await Task.Delay(SuccessFeedbackMs, token).ConfigureAwait(true);
            SaveButtonState = DisplaysOperationsButtonState.Idle;
        }
        catch (TaskCanceledException)
        {
            // neuer Klick hat Feedback zurückgesetzt
        }
    }

    private void RefreshOutsideProgramListLabels()
    {
        foreach (var program in OutsidePrograms)
        {
            program.RefreshListDisplayProperties();
        }
    }

    private void SortOutsidePrograms()
    {
        if (_isSortingPrograms || OutsidePrograms.Count <= 1)
        {
            RefreshOutsideProgramsView();
            return;
        }

        _isSortingPrograms = true;
        try
        {
            var selected = SelectedOutsideProgram;
            var sorted = OutsidePrograms
                .OrderBy(p => p, Comparer<OutsideDisplayProgram>.Create(OutsideDisplayProgram.CompareForZielliste))
                .ToList();
            OutsidePrograms.Clear();
            foreach (var program in sorted)
            {
                OutsidePrograms.Add(program);
            }

            SelectedOutsideProgram = selected is not null && OutsidePrograms.Contains(selected)
                ? selected
                : OutsidePrograms.FirstOrDefault();
        }
        finally
        {
            _isSortingPrograms = false;
            RefreshOutsideProgramsView();
        }
    }

    private void RefreshOutsideProgramsView()
    {
        OutsideProgramsView.Refresh();
        EnsureSelectedProgramVisibleInFilter();
    }

    partial void OnSelectedOutsideProgramSortChanged(string value) => ApplyOutsideProgramSort();

    private void ApplyOutsideProgramSort()
    {
        if (OutsideProgramsView is not ListCollectionView listView)
        {
            OutsideProgramsView.Refresh();
            return;
        }

        listView.CustomSort = CreateOutsideProgramComparer(SelectedOutsideProgramSort);
        EnsureSelectedProgramVisibleInFilter();
    }

    private static IComparer CreateOutsideProgramComparer(string? sortOption)
    {
        var descending = sortOption?.Contains('↓', StringComparison.Ordinal) == true;
        var mode = sortOption switch
        {
            not null when sortOption.StartsWith("Zielnummer", StringComparison.OrdinalIgnoreCase) =>
                OutsideProgramSortMode.Zielnummer,
            not null when sortOption.StartsWith("Linie", StringComparison.OrdinalIgnoreCase) =>
                OutsideProgramSortMode.Linie,
            not null when sortOption.StartsWith("Name", StringComparison.OrdinalIgnoreCase) =>
                OutsideProgramSortMode.Name,
            _ => OutsideProgramSortMode.Id
        };

        return Comparer<OutsideDisplayProgram>.Create((left, right) =>
        {
            var cmp = mode switch
            {
                OutsideProgramSortMode.Zielnummer =>
                    GetZielnummerSortKey(left).CompareTo(GetZielnummerSortKey(right)),
                OutsideProgramSortMode.Linie =>
                    CompareLinieSortKeys(left, right),
                OutsideProgramSortMode.Name =>
                    OutsideDisplayProgram.CompareZiellisteNames(left.Name, right.Name),
                _ => GetIdSortKey(left).CompareTo(GetIdSortKey(right))
            };

            if (cmp == 0)
            {
                cmp = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            }

            return descending ? -cmp : cmp;
        });
    }

    private static int GetIdSortKey(OutsideDisplayProgram program)
    {
        if (int.TryParse(OutsideDisplayId.Normalize(program.Id), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return id;
        }

        if (int.TryParse(program.DisplayNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return id;
        }

        return int.MaxValue;
    }

    private static int GetZielnummerSortKey(OutsideDisplayProgram program)
    {
        if (program.IsDs003)
        {
            var zn = OutsideDisplayTelegramFactory.NormalizeZielnummer(program.FrontLine1);
            if (int.TryParse(zn, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                return n;
            }
        }

        if (program.DestinationNumber is int destNo)
        {
            return destNo;
        }

        var fromFront = OutsideDisplayTelegramFactory.NormalizeZielnummer(program.FrontLine1);
        if (int.TryParse(fromFront, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frontNo) &&
            frontNo is >= 0 and <= 9999)
        {
            return frontNo;
        }

        return GetIdSortKey(program);
    }

    /// <summary>Linie (Ds001Value / Front z999): Zahlen numerisch, sonst Text; leer ans Ende.</summary>
    private static int CompareLinieSortKeys(OutsideDisplayProgram left, OutsideDisplayProgram right)
    {
        var leftRaw = left.Ds001Value?.Trim() ?? string.Empty;
        var rightRaw = right.Ds001Value?.Trim() ?? string.Empty;
        var leftEmpty = leftRaw.Length == 0;
        var rightEmpty = rightRaw.Length == 0;
        if (leftEmpty && rightEmpty)
        {
            return 0;
        }

        if (leftEmpty)
        {
            return 1;
        }

        if (rightEmpty)
        {
            return -1;
        }

        var leftDigits = ExtractLeadingDigits(leftRaw);
        var rightDigits = ExtractLeadingDigits(rightRaw);
        if (leftDigits is int ln && rightDigits is int rn)
        {
            var numCmp = ln.CompareTo(rn);
            if (numCmp != 0)
            {
                return numCmp;
            }
        }
        else if (leftDigits is int)
        {
            return -1;
        }
        else if (rightDigits is int)
        {
            return 1;
        }

        return string.Compare(leftRaw, rightRaw, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int? ExtractLeadingDigits(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }

    private enum OutsideProgramSortMode
    {
        Id,
        Zielnummer,
        Linie,
        Name
    }

    private void EnsureSelectedProgramVisibleInFilter()
    {
        if (SelectedOutsideProgram is null)
        {
            return;
        }

        if (FilterOutsideProgram(SelectedOutsideProgram))
        {
            return;
        }

        // Aktuelle Auswahl ist durch Filter ausgeblendet → erstes sichtbares Ziel wählen
        SelectedOutsideProgram = OutsideProgramsView.Cast<OutsideDisplayProgram>().FirstOrDefault();
    }

    private bool FilterOutsideProgram(object obj)
    {
        if (obj is not OutsideDisplayProgram program)
        {
            return false;
        }

        if (!string.Equals(SelectedProtocolFilter, "Alle", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(program.ProtocolLabel, SelectedProtocolFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = OutsideProgramSearchText?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return true;
        }

        return ContainsIgnoreCase(program.Name, query) ||
               ContainsIgnoreCase(program.DisplayNumber, query) ||
               ContainsIgnoreCase(program.ListNumberLabel, query) ||
               ContainsIgnoreCase(program.ProtocolLabel, query) ||
               ContainsIgnoreCase(program.Ds001Value, query) ||
               ContainsIgnoreCase(program.FrontPreview, query) ||
               ContainsIgnoreCase(program.SidePreview, query) ||
               ContainsIgnoreCase(program.WechseltextPreview, query) ||
               (program.DestinationNumber is int destNo &&
                (destNo.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 destNo.ToString("D4", CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ContainsIgnoreCase(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    partial void OnOutsideProgramSearchTextChanged(string value) => _outsideSearchDebouncer.Schedule();

    partial void OnSelectedProtocolFilterChanged(string value) => RefreshOutsideProgramsView();

    private void MarkDirty()
    {
        _hasUnsavedChanges = true;
    }

    /// <summary>false = ID zurückgesetzt (Kollision).</summary>
    private bool EnforceUniqueDestinationId(OutsideDisplayProgram program)
    {
        if (!OutsideDisplayId.IsFourDigit(program.Id))
        {
            return true;
        }

        var clash = OutsidePrograms.Any(p =>
            !ReferenceEquals(p, program) &&
            string.Equals(p.Id, program.Id, StringComparison.Ordinal));
        if (!clash)
        {
            _selectedProgramIdSnapshot = program.Id;
            return true;
        }

        StatusMessage = $"ID {program.Id} ist bereits vergeben – bitte eine freie Nummer wählen.";
        var restore = OutsideDisplayId.IsFourDigit(_selectedProgramIdSnapshot)
            ? _selectedProgramIdSnapshot!
            : OutsideDisplayId.NewUniqueId(
                OutsidePrograms.Where(p => !ReferenceEquals(p, program)).Select(p => p.Id));

        if (string.Equals(program.Id, restore, StringComparison.Ordinal))
        {
            return true;
        }

        _suppressIdCollisionCheck = true;
        try
        {
            program.Id = restore;
        }
        finally
        {
            _suppressIdCollisionCheck = false;
        }

        return false;
    }

    /// <summary>false = Zielnummer zurückgesetzt (Kollision unter Mobitec).</summary>
    private bool EnforceUniqueMobitecDestinationNumber(OutsideDisplayProgram program)
    {
        if (!program.IsMobitec || program.DestinationNumber is not int destNo)
        {
            _selectedDestinationNumberSnapshot = program.DestinationNumber;
            return true;
        }

        var clash = OutsidePrograms.Any(p =>
            !ReferenceEquals(p, program) &&
            p.IsMobitec &&
            p.DestinationNumber == destNo);
        if (!clash)
        {
            _selectedDestinationNumberSnapshot = destNo;
            return true;
        }

        StatusMessage =
            $"Zielnummer {destNo:D4} ist bereits vergeben – bitte eine freie ICU-Nummer wählen.";
        _suppressIdCollisionCheck = true;
        try
        {
            program.DestinationNumber = _selectedDestinationNumberSnapshot;
        }
        finally
        {
            _suppressIdCollisionCheck = false;
        }

        return false;
    }

    partial void OnSelectedOutsideProgramChanged(OutsideDisplayProgram? value)
    {
        if (_subscribedOutsideProgram is not null)
        {
            _subscribedOutsideProgram.PropertyChanged -= OnOutsideProgramPropertyChanged;
        }

        _subscribedOutsideProgram = value;
        _selectedProgramIdSnapshot = value?.Id;
        _selectedDestinationNumberSnapshot = value?.DestinationNumber;
        SelectedWechseltextIndex = 0;

        if (value is not null)
        {
            value.PropertyChanged += OnOutsideProgramPropertyChanged;
        }

        NotifyActiveWechseltextBindings();
    }

    partial void OnSelectedWechseltextIndexChanged(int value) => NotifyActiveWechseltextBindings();

    public string? ActiveFrontLine1
    {
        get => GetActiveFrontCycle()?.Line1;
        set
        {
            var cycle = GetActiveFrontCycle();
            if (cycle is null || value is null)
            {
                return;
            }

            cycle.Line1 = value;
            MarkDirty();
            NotifyActiveWechseltextBindings();
        }
    }

    public string? ActiveFrontLine2
    {
        get => GetActiveFrontCycle()?.Line2;
        set
        {
            var cycle = GetActiveFrontCycle();
            if (cycle is null || value is null)
            {
                return;
            }

            cycle.Line2 = value;
            MarkDirty();
            NotifyActiveWechseltextBindings();
        }
    }

    public string? ActiveSideLine1
    {
        get => GetActiveSideCycle()?.Line1;
        set
        {
            var cycle = GetActiveSideCycle();
            if (cycle is null || value is null)
            {
                return;
            }

            cycle.Line1 = value;
            MarkDirty();
            NotifyActiveWechseltextBindings();
        }
    }

    public string? ActiveSideLine2
    {
        get => GetActiveSideCycle()?.Line2;
        set
        {
            var cycle = GetActiveSideCycle();
            if (cycle is null || value is null)
            {
                return;
            }

            cycle.Line2 = value;
            MarkDirty();
            NotifyActiveWechseltextBindings();
        }
    }

    private OutsideDisplayTextCycle? GetActiveFrontCycle() =>
        SelectedOutsideProgram?.FrontCycles.ElementAtOrDefault(SelectedWechseltextIndex);

    private OutsideDisplayTextCycle? GetActiveSideCycle() =>
        SelectedOutsideProgram?.SideCycles.ElementAtOrDefault(SelectedWechseltextIndex);

    private void NotifyActiveWechseltextBindings()
    {
        OnPropertyChanged(nameof(ActiveFrontLine1));
        OnPropertyChanged(nameof(ActiveFrontLine2));
        OnPropertyChanged(nameof(ActiveSideLine1));
        OnPropertyChanged(nameof(ActiveSideLine2));
    }

    [RelayCommand]
    private void SelectTab(string? tabKey)
    {
        SelectedTabIndex = tabKey == "hints" ? 1 : 0;
    }

    [RelayCommand]
    private void SelectWechseltext(string? indexText)
    {
        if (!int.TryParse(indexText, out var index))
        {
            return;
        }

        SelectedWechseltextIndex = Math.Clamp(index, 0, OutsideDisplayCycleParser.MaxCycles - 1);
    }

    private void OnOutsideProgramPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSortingPrograms)
        {
            return;
        }

        if (!_committingChanges &&
            !_suppressIdCollisionCheck &&
            sender is OutsideDisplayProgram program &&
            e.PropertyName is nameof(OutsideDisplayProgram.Id) or nameof(OutsideDisplayProgram.IdEditText))
        {
            if (!EnforceUniqueDestinationId(program))
            {
                return;
            }
        }

        if (!_committingChanges &&
            !_suppressIdCollisionCheck &&
            sender is OutsideDisplayProgram destProgram &&
            e.PropertyName is nameof(OutsideDisplayProgram.DestinationNumber)
                or nameof(OutsideDisplayProgram.DestinationNumberEditText))
        {
            if (!EnforceUniqueMobitecDestinationNumber(destProgram))
            {
                return;
            }
        }

        if (!_committingChanges)
        {
            MarkDirty();
        }

        if (!_committingChanges &&
            e.PropertyName is nameof(OutsideDisplayProgram.IsStartTarget) or nameof(OutsideDisplayProgram.Name))
        {
            SortOutsidePrograms();
        }

        if (_committingChanges)
        {
            return;
        }

        if (e.PropertyName is nameof(OutsideDisplayProgram.FrontPreview) or
            nameof(OutsideDisplayProgram.SidePreview) or
            nameof(OutsideDisplayProgram.WechseltextPreview) or
            nameof(OutsideDisplayProgram.WechseltextCount))
        {
            NotifyActiveWechseltextBindings();
            return;
        }

        if (e.PropertyName is nameof(OutsideDisplayProgram.IsListEnabled))
        {
            var enabledCount = OutsidePrograms.Count(p => p.IsListEnabled);
            StatusMessage =
                $"{OutsidePrograms.Count} Zielanzeigen ({enabledCount} in ITCS-Liste), {DateBasedHints.Count} Hinweise – Änderungen bitte speichern.";
            return;
        }

        if (e.PropertyName is nameof(OutsideDisplayProgram.FontLine1Weight) or
            nameof(OutsideDisplayProgram.FontLine1Height) or
            nameof(OutsideDisplayProgram.FontLine2Weight) or
            nameof(OutsideDisplayProgram.FontLine2Height) or
            nameof(OutsideDisplayProgram.Protocol) or
            nameof(OutsideDisplayProgram.IsDs021Neu) or
            nameof(OutsideDisplayProgram.IsFmaS1) or
            nameof(OutsideDisplayProgram.IsZielnummer) or
            nameof(OutsideDisplayProgram.UsesCycleEditor) or
            nameof(OutsideDisplayProgram.IsDs021T) or
            nameof(OutsideDisplayProgram.IsKrefeld))
        {
            return;
        }

        StatusMessage =
            $"{OutsidePrograms.Count} Zielanzeigen, {DateBasedHints.Count} Hinweise – Änderungen bitte speichern.";
    }

    [RelayCommand]
    private void Save() => CommitChanges(force: true, showSuccessFeedback: true);

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
        if (!EnsurePackageForOutsidePrograms())
        {
            return;
        }

        var program = OutsideDisplayProgram.CreateDs021t($"Ziel {OutsidePrograms.Count + 1}");
        program.Id = OutsideDisplayId.NewUniqueId(OutsidePrograms.Select(p => p.Id));
        OutsidePrograms.Add(program);
        SortOutsidePrograms();
        SelectedOutsideProgram = program;
        MarkDirty();
        StatusMessage = "Neue Zielanzeige (DS021T) – Texte anpassen und speichern.";
    }

    [RelayCommand]
    private void AddOutsideProgramDs021Neu()
    {
        if (!EnsurePackageForOutsidePrograms())
        {
            return;
        }

        var program = OutsideDisplayProgram.CreateDs021Neu($"Ziel {OutsidePrograms.Count + 1}");
        program.Id = OutsideDisplayId.NewUniqueId(OutsidePrograms.Select(p => p.Id));
        OutsidePrograms.Add(program);
        SortOutsidePrograms();
        SelectedOutsideProgram = program;
        MarkDirty();
        StatusMessage = "Neue Zielanzeige (DS021neu) – Texte anpassen und speichern.";
    }

    [RelayCommand]
    private void AddOutsideProgramFmaS1()
    {
        if (!EnsurePackageForOutsidePrograms())
        {
            return;
        }

        var program = OutsideDisplayProgram.CreateFmaS1($"Ziel {OutsidePrograms.Count + 1}");
        program.Id = OutsideDisplayId.NewUniqueId(OutsidePrograms.Select(p => p.Id));
        OutsidePrograms.Add(program);
        SortOutsidePrograms();
        SelectedOutsideProgram = program;
        MarkDirty();
        StatusMessage = "Neue Zielanzeige (FMA-S1) – Texte anpassen und speichern.";
    }

    [RelayCommand]
    private void AddOutsideProgramKrefeld()
    {
        if (!EnsurePackageForOutsidePrograms())
        {
            return;
        }

        var program = OutsideDisplayProgram.CreateKrefeld($"Ziel {OutsidePrograms.Count + 1}");
        program.Id = OutsideDisplayId.NewUniqueId(OutsidePrograms.Select(p => p.Id));
        OutsidePrograms.Add(program);
        SortOutsidePrograms();
        SelectedOutsideProgram = program;
        MarkDirty();
        StatusMessage = "Neue Zielanzeige (DS003a Krefeld) – Texte anpassen und speichern.";
    }

    [RelayCommand]
    private void AddOutsideProgramZielnummer()
    {
        if (!EnsurePackageForOutsidePrograms())
        {
            return;
        }

        var program = OutsideDisplayProgram.CreateZielnummer($"Ziel {OutsidePrograms.Count + 1}");
        program.Id = OutsideDisplayId.NewUniqueId(OutsidePrograms.Select(p => p.Id));
        OutsidePrograms.Add(program);
        SortOutsidePrograms();
        SelectedOutsideProgram = program;
        MarkDirty();
        StatusMessage = "Neue Zielanzeige (DS003) – Zielnummer setzen (z. B. 001); Tablet sendet nur z001…";
    }

    [RelayCommand]
    private void AddOutsideProgramMobitec()
    {
        if (!EnsurePackageForOutsidePrograms())
        {
            return;
        }

        var program = OutsideDisplayProgram.CreateMobitec($"Ziel {OutsidePrograms.Count + 1}");
        program.Id = OutsideDisplayId.NewUniqueId(OutsidePrograms.Select(p => p.Id));
        OutsidePrograms.Add(program);
        SortOutsidePrograms();
        SelectedOutsideProgram = program;
        MarkDirty();
        StatusMessage = "Neue Zielanzeige (Mobitec) – Linie und Zieltext setzen, speichern, exportieren.";
    }

    [RelayCommand]
    private void AddOutsideProgramMobitecSmile()
    {
        if (!EnsurePackageForOutsidePrograms())
        {
            return;
        }

        var program = OutsideDisplayProgram.CreateMobitecSmile();
        program.Id = OutsideDisplayId.NewUniqueId(OutsidePrograms.Select(p => p.Id));
        OutsidePrograms.Add(program);
        SortOutsidePrograms();
        SelectedOutsideProgram = program;
        MarkDirty();
        StatusMessage = "Mobitec Danke/Smile angelegt – Tablet sendet Smile-Bitmap (wie Verspätungs-Danke).";
    }

    [RelayCommand]
    private void ImportMobitecOut()
    {
        if (!EnsurePackageForOutsidePrograms())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "ZEdit / Mobitec OUT importieren",
            Filter = "ZEdit OUT (*.out)|*.out|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            var imported = MobitecTransOutImporter.Parse(bytes);
            if (imported.Count == 0)
            {
                StatusMessage = "Keine Mobitec-Ziele in der OUT-Datei gefunden.";
                return;
            }

            var selection = Views.MobitecOutImportDialog.Show(
                DialogOwnerHelper.ResolveOwner(),
                Path.GetFileName(dialog.FileName),
                imported,
                OutsidePrograms
                    .Where(p => p.IsMobitec && p.DestinationNumber is int)
                    .Select(p => p.DestinationNumber!.Value.ToString("D4")));
            if (selection is null || selection.Count == 0)
            {
                StatusMessage = "OUT-Import abgebrochen.";
                return;
            }

            var usedIds = new HashSet<string>(
                OutsidePrograms.Select(p => p.Id),
                StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var updated = 0;
            OutsideDisplayProgram? lastTouched = null;
            foreach (var (destination, saveName) in selection)
            {
                // Upsert über ICU-Zielnummer (nicht über App-ID).
                var existing = destination.DestinationNumber is >= 0 and <= 9999
                    ? OutsidePrograms.FirstOrDefault(p =>
                        p.IsMobitec &&
                        p.DestinationNumber == destination.DestinationNumber)
                    : null;
                if (existing is not null)
                {
                    MobitecTransOutImporter.ApplyToExistingProgram(existing, destination, saveName);
                    updated++;
                    lastTouched = existing;
                    continue;
                }

                var program = MobitecTransOutImporter.ToProgram(destination, saveName, usedIds);
                OutsidePrograms.Add(program);
                added++;
                lastTouched = program;
            }

            SortOutsidePrograms();
            if (lastTouched is not null)
            {
                SelectedOutsideProgram = lastTouched;
            }

            MarkDirty();
            StatusMessage =
                $"Mobitec OUT „{Path.GetFileName(dialog.FileName)}“: {added} neu, {updated} aktualisiert " +
                $"(Zielnummer = Upsert-Schlüssel) – bitte prüfen und speichern.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"OUT-Import fehlgeschlagen: {ex.Message}";
        }
    }

    private bool EnsurePackageForOutsidePrograms()
    {
        if (!AppServices.Routes.EnsureEmptyPackageIfNeeded())
        {
            StatusMessage = "Leeres Route-Paket konnte nicht angelegt werden.";
            return false;
        }

        if (OutsidePrograms.Count == 0 && DateBasedHints.Count == 0)
        {
            RefreshFromEditor();
        }

        return AppServices.Routes.Editor is not null;
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

    [RelayCommand]
    private void ExportDestinationsPdf()
    {
        if (OutsidePrograms.Count == 0)
        {
            StatusMessage = "Keine Ziele in der Zielliste – PDF kann nicht erstellt werden.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Zielliste als PDF speichern",
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            FileName = OutsideDisplayDestinationsPdfGenerator.BuildDefaultFileName(),
            AddExtension = true,
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            OutsideDisplayDestinationsPdfGenerator.Generate(dialog.FileName, OutsidePrograms.ToList());
            StatusMessage = $"PDF gespeichert: {Path.GetFileName(dialog.FileName)} ({OutsidePrograms.Count} Ziele)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF fehlgeschlagen: {ex.Message}";
            MessageBox.Show(
                Application.Current?.MainWindow,
                ex.Message,
                "Anzeigen & Hinweise",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

