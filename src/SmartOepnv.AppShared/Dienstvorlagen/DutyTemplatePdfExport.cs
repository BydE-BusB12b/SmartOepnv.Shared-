using System.IO;
using SmartOepnv.Core;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.AppShared.Dienstvorlagen;

public static class DutyTemplatePdfExport
{
    public const string OutputFolderName = "dienstvorlagen-pdf";

    public static string GetWorkspaceOutputDirectory()
    {
        var dir = Path.Combine(
            AppPaths.GetRoamingDataDirectory(AppServices.SettingsSubfolder),
            "workspace",
            OutputFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool TryGetPart(
        DutyTemplate template,
        int part,
        out IReadOnlyList<DutyTemplateRow> rows,
        out string dutyNumber)
    {
        if (template.IsSplitShift && part == 1)
        {
            rows = template.Rows.Concat(template.Part2Rows).ToList();
            dutyNumber = template.DutyNumber;
            return rows.Count > 0 && !string.IsNullOrWhiteSpace(dutyNumber);
        }

        if (template.IsSplitShift)
        {
            rows = [];
            dutyNumber = string.Empty;
            return false;
        }

        rows = part switch
        {
            3 => template.Part3Rows,
            2 => template.Part2Rows,
            _ => template.Rows
        };

        dutyNumber = part switch
        {
            3 => template.DutyNumberPart3,
            2 => template.DutyNumberPart2,
            _ => template.DutyNumber
        };

        return rows.Count > 0 && !string.IsNullOrWhiteSpace(dutyNumber);
    }

    public static string SanitizeFileName(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();

    public static string BuildDefaultFileName(string dutyNumber, int part)
    {
        var safe = SanitizeFileName(dutyNumber.Trim());
        return string.IsNullOrWhiteSpace(safe) ? $"dienst-teil{part}.pdf" : $"{safe}.pdf";
    }

    public static void ExportPart(string outputPath, DutyTemplate template, int part)
    {
        if (!TryGetPart(template, part, out var rows, out var dutyNumber))
        {
            throw new InvalidOperationException($"Teil {part} hat keine Fahrten oder Dienstnummer.");
        }

        DienstvorlagenPdfGenerator.GeneratePart(outputPath, template, rows, dutyNumber.Trim(), part);
    }

    public static IReadOnlyList<DutyTemplatePdfExportResult> ExportAllPartsToWorkspace(DutyTemplate template)
    {
        var outputDir = GetWorkspaceOutputDirectory();
        var results = new List<DutyTemplatePdfExportResult>();

        for (var part = 1; part <= 3; part++)
        {
            if (!TryGetPart(template, part, out _, out var dutyNumber))
            {
                continue;
            }

            var fileName = BuildDefaultFileName(dutyNumber, part);
            var outputPath = Path.Combine(outputDir, fileName);
            ExportPart(outputPath, template, part);
            results.Add(new DutyTemplatePdfExportResult(part, dutyNumber.Trim(), outputPath));
        }

        return results;
    }
}

public sealed record DutyTemplatePdfExportResult(int Part, string DutyNumber, string FilePath);
