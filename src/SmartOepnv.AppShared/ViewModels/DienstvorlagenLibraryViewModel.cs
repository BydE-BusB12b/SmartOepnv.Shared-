using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Dienstvorlagen;
using SmartOepnv.Core;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.AppShared.ViewModels;

public partial class DienstvorlagenLibraryViewModel : EditorStatusViewModelBase, IEditorAreaViewModel
{
    [ObservableProperty] private DutyTemplate? selectedTemplate;
    [ObservableProperty] private string selectedSummary = string.Empty;
    [ObservableProperty] private string selectedStatsLine = string.Empty;
    [ObservableProperty] private bool hasPart2;
    [ObservableProperty] private bool hasPart3;
    [ObservableProperty] private bool canExportPart1;
    [ObservableProperty] private bool canExportPart2;
    [ObservableProperty] private bool canExportPart3;
    [ObservableProperty] private bool canExportAnyPdf;
    [ObservableProperty] private string exportPart1ButtonLabel = "Teil 1 als PDF";
    [ObservableProperty] private string exportPart2ButtonLabel = "Teil 2 als PDF";
    [ObservableProperty] private string exportPart3ButtonLabel = "Teil 3 als PDF";

    public ObservableCollection<DutyTemplate> Templates { get; } = [];
    public ObservableCollection<DutyTemplateRow> DisplayRows { get; } = [];
    public ObservableCollection<DutyTemplateRow> DisplayPart2Rows { get; } = [];
    public ObservableCollection<DutyTemplateRow> DisplayPart3Rows { get; } = [];

    public DienstvorlagenLibraryViewModel()
        : base("Gespeicherte Dienstvorlagen anzeigen und als PDF exportieren (301, 302, …).")
    {
    }

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
        UpdateExportState();

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
            UpdateExportState();
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
        UpdateExportState();
    }

    private void UpdateExportState()
    {
        var template = SelectedTemplate;
        CanExportPart1 = template is not null &&
                         DutyTemplatePdfExport.TryGetPart(template, 1, out _, out _);
        CanExportPart2 = template is not null &&
                         DutyTemplatePdfExport.TryGetPart(template, 2, out _, out _);
        CanExportPart3 = template is not null &&
                         DutyTemplatePdfExport.TryGetPart(template, 3, out _, out _);
        CanExportAnyPdf = CanExportPart1 || CanExportPart2 || CanExportPart3;

        ExportPart1ButtonLabel = BuildExportLabel(template, 1, "Teil 1");
        ExportPart2ButtonLabel = BuildExportLabel(template, 2, "Teil 2");
        ExportPart3ButtonLabel = BuildExportLabel(template, 3, "Teil 3");

        ExportPart1PdfCommand.NotifyCanExecuteChanged();
        ExportPart2PdfCommand.NotifyCanExecuteChanged();
        ExportPart3PdfCommand.NotifyCanExecuteChanged();
        ExportAllPdfsCommand.NotifyCanExecuteChanged();
        OpenPdfFolderCommand.NotifyCanExecuteChanged();
    }

    private static string BuildExportLabel(DutyTemplate? template, int part, string fallbackPartLabel)
    {
        if (template is null ||
            !DutyTemplatePdfExport.TryGetPart(template, part, out _, out var dutyNumber))
        {
            return $"{fallbackPartLabel} als PDF";
        }

        return $"{dutyNumber.Trim()}.pdf speichern";
    }

    [RelayCommand(CanExecute = nameof(CanExportPart1))]
    private void ExportPart1Pdf() => ExportSinglePartPdf(1);

    [RelayCommand(CanExecute = nameof(CanExportPart2))]
    private void ExportPart2Pdf() => ExportSinglePartPdf(2);

    [RelayCommand(CanExecute = nameof(CanExportPart3))]
    private void ExportPart3Pdf() => ExportSinglePartPdf(3);

    [RelayCommand(CanExecute = nameof(CanExportAnyPdf))]
    private void ExportAllPdfs()
    {
        if (SelectedTemplate is null)
        {
            ReportSaveError("Bitte zuerst eine Vorlage auswählen.");
            return;
        }

        try
        {
            var results = DutyTemplatePdfExport.ExportAllPartsToWorkspace(SelectedTemplate);
            if (results.Count == 0)
            {
                ReportSaveError("Keine exportierbaren Teile – Dienstnummern (301, 302, …) und Fahrten prüfen.");
                return;
            }

            var names = string.Join(", ", results.Select(r => $"{r.DutyNumber}.pdf"));
            var folder = DutyTemplatePdfExport.GetWorkspaceOutputDirectory();
            ReportSaveSuccess($"{results.Count} PDF(s) gespeichert ({names}) in {folder}");
        }
        catch (Exception ex)
        {
            ReportSaveError($"PDF-Erstellung fehlgeschlagen: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportAnyPdf))]
    private void OpenPdfFolder()
    {
        var folder = DutyTemplatePdfExport.GetWorkspaceOutputDirectory();
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    private void ExportSinglePartPdf(int part)
    {
        if (SelectedTemplate is null)
        {
            ReportSaveError("Bitte zuerst eine Vorlage auswählen.");
            return;
        }

        if (!DutyTemplatePdfExport.TryGetPart(SelectedTemplate, part, out _, out var dutyNumber))
        {
            ReportSaveError($"Teil {part} enthält keine Fahrten oder Dienstnummer.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = DutyTemplatePdfExport.BuildDefaultFileName(dutyNumber, part),
            DefaultExt = ".pdf",
            Title = $"Dienstvorlage {dutyNumber.Trim()} speichern"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            DutyTemplatePdfExport.ExportPart(dialog.FileName, SelectedTemplate, part);
            ReportSaveSuccess($"PDF gespeichert: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ReportSaveError($"PDF-Erstellung fehlgeschlagen: {ex.Message}");
        }
    }
}
