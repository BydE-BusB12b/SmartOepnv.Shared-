using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartOepnv.Core;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.AppShared.ViewModels;

public partial class DienstvorlagenLibraryViewModel : ObservableObject, IEditorAreaViewModel
{
    [ObservableProperty] private string statusMessage = "Lade Vorlagen…";
    [ObservableProperty] private DutyTemplate? selectedTemplate;
    [ObservableProperty] private string selectedSummary = string.Empty;
    [ObservableProperty] private string selectedStatsLine = string.Empty;
    [ObservableProperty] private bool hasPart2;
    [ObservableProperty] private bool hasPart3;

    public ObservableCollection<DutyTemplate> Templates { get; } = [];
    public ObservableCollection<DutyTemplateRow> DisplayRows { get; } = [];
    public ObservableCollection<DutyTemplateRow> DisplayPart2Rows { get; } = [];
    public ObservableCollection<DutyTemplateRow> DisplayPart3Rows { get; } = [];

    public void RefreshFromEditorIfNeeded() => RefreshFromEditor();

    public void RefreshFromEditor() => ReloadTemplates();

    public bool HasPendingChanges => false;

    public void CommitChangesIfDirty()
    {
    }

    private void ReloadTemplates()
    {
        Templates.Clear();
        DisplayRows.Clear();
        DisplayPart2Rows.Clear();
        DisplayPart3Rows.Clear();
        SelectedTemplate = null;
        SelectedSummary = string.Empty;
        SelectedStatsLine = string.Empty;
        HasPart2 = false;
        HasPart3 = false;

        if (AppServices.DutyTemplates is null)
        {
            StatusMessage = "Dienstvorlagen nur im Planer verfügbar.";
            return;
        }

        foreach (var template in AppServices.DutyTemplates.LoadAll()
                     .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            Templates.Add(template);
        }

        StatusMessage = Templates.Count == 0
            ? "Noch keine gespeicherten Vorlagen – unter „Dienstvorlagen“ anlegen."
            : $"{Templates.Count} Vorlage(n) – links wählen, rechts alle Fahrten anzeigen.";
    }

    partial void OnSelectedTemplateChanged(DutyTemplate? value)
    {
        DisplayRows.Clear();
        DisplayPart2Rows.Clear();
        DisplayPart3Rows.Clear();
        if (value is null)
        {
            SelectedSummary = string.Empty;
            SelectedStatsLine = string.Empty;
            HasPart2 = false;
            HasPart3 = false;
            return;
        }

        foreach (var row in value.Rows)
        {
            DisplayRows.Add(row);
        }

        foreach (var row in value.Part2Rows)
        {
            DisplayPart2Rows.Add(row);
        }

        foreach (var row in value.Part3Rows)
        {
            DisplayPart3Rows.Add(row);
        }

        HasPart2 = value.Part2Rows.Count > 0;
        HasPart3 = value.Part3Rows.Count > 0;
        SelectedSummary = value.Summary;
        var stats = DutyTemplateCalculator.ComputeSummary(value);
        var allRows = value.Rows.Concat(value.Part2Rows).Concat(value.Part3Rows).ToList();
        var start = DutyTemplateCalculator.GetServiceStartDisplay(allRows, value.WorkPreparationMinutes) ?? "–";
        var end = DutyTemplateCalculator.GetServiceEndDisplay(allRows, value.WorkFollowUpMinutes) ?? "–";
        SelectedStatsLine =
            $"Dienst {start} – {end} · {stats.ServiceDurationDisplay} · Lohn {stats.PayHoursDisplay}";
    }
}
