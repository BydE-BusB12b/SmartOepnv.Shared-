using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.AppShared.Pdf;

namespace SmartOepnv.AppShared.Employees;

/// <summary>Gemeinsamer PDF-Export für Planer- und Leitstelle-Unterweisungsanleitungen.</summary>
public static class OperatorManualPdfGenerator
{
    private const string PrimaryBlue = "#0D47A1";
    private const string BorderColor = "#4A5F82";
    private const float PageMargin = 32f;
    private const float SectionGap = 10f;
    private const float ImageMaxWidth = 228f;

    static OperatorManualPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public static void Generate(
        string outputPath,
        OperatorManualDocument document,
        string assetSubfolder)
    {
        var created = DateTime.Now;
        var figureNumber = 1;

        Document.Create(container =>
        {
            container.Page(page => RenderCoverPage(page, document, created));
            foreach (var section in document.Sections)
            {
                var currentFigure = figureNumber;
                container.Page(page =>
                {
                    RenderSectionPage(page, section, assetSubfolder, currentFigure, created);
                });
                figureNumber += section.Images?.Count ?? 0;
            }

            if (!string.IsNullOrWhiteSpace(document.ClosingNote))
            {
                container.Page(page => RenderClosingPage(page, document.ClosingNote!, created));
            }
        }).GeneratePdf(outputPath);
    }

    public static string BuildDefaultFileName(string prefix) =>
        $"{prefix}_{DateTime.Now:yyyy-MM-dd}.pdf";

    private static void RenderCoverPage(PageDescriptor page, OperatorManualDocument document, DateTime created)
    {
        ApplyPageFrame(page, created);
        page.Header().Element(header =>
            PlanerPdfBranding.ComposeHeaderWithSmartLogo(header, left =>
            {
                left.AlignMiddle().Column(column =>
                {
                    column.Item().Text(document.CoverTitle)
                        .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                        .FontSize(20).SemiBold().FontColor(PrimaryBlue);
                    column.Item().PaddingTop(2)
                        .Text($"Erstellt am {created:dd.MM.yyyy}")
                        .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }));

        page.Content().PaddingTop(8).Column(column =>
        {
            column.Item().Border(1).BorderColor(BorderColor).Padding(14).Column(box =>
            {
                box.Item().Text(document.CoverSubtitle)
                    .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                    .FontSize(13).SemiBold().FontColor(PrimaryBlue);
                box.Item().PaddingTop(8).Text(NormalizeBody(document.IntroText)).LineHeight(1.3f);
            });

            if (!string.IsNullOrWhiteSpace(document.CoverHint))
            {
                column.Item().PaddingTop(14).Text(NormalizeBody(document.CoverHint))
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken1)
                    .LineHeight(1.25f);
            }
        });
    }

    private static void RenderSectionPage(
        PageDescriptor page,
        BriefingSection section,
        string assetSubfolder,
        int startFigureNumber,
        DateTime created)
    {
        ApplyPageFrame(page, created);
        page.DefaultTextStyle(x => PlanerPdfTextStyles.ApplyLight(x).FontSize(10).FontColor(Colors.Black).LineHeight(1.25f));
        page.Header().Element(header => ComposePageHeader(header, "Smart-ÖPNV Unterweisungsanleitung"));

        page.Content().PaddingTop(2).Column(column =>
        {
            column.Item().Text(section.Title)
                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                .FontSize(12).SemiBold().FontColor(PrimaryBlue);
            column.Item().PaddingTop(3).Text(NormalizeBody(section.Body));

            if (section.Images is not { Count: > 0 } images)
            {
                return;
            }

            column.Item().PaddingTop(5).Element(content =>
            {
                if (images.Count == 2)
                {
                    content.Row(row =>
                    {
                        row.Spacing(8);
                        for (var i = 0; i < 2; i++)
                        {
                            row.RelativeItem().Element(cell =>
                                ComposeImageFigure(cell, images[i], assetSubfolder, startFigureNumber + i));
                        }
                    });
                    return;
                }

                content.AlignCenter().Element(cell =>
                    ComposeImageFigure(cell, images[0], assetSubfolder, startFigureNumber));
            });
        });
    }

    private static void RenderClosingPage(PageDescriptor page, string closingNote, DateTime created)
    {
        ApplyPageFrame(page, created);
        page.Header().Element(header => ComposePageHeader(header, "Smart-ÖPNV Unterweisungsanleitung"));
        page.Content().PaddingTop(12).Border(1.5f).BorderColor(BorderColor).Padding(16).Column(column =>
        {
            column.Item().Text(NormalizeBody(closingNote))
                .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                .FontSize(9)
                .LineHeight(1.3f)
                .FontColor(Colors.Grey.Darken2);
        });
    }

    private static void ComposeImageFigure(
        IContainer container,
        BriefingImage image,
        string assetSubfolder,
        int figureNumber)
    {
        container.Column(block =>
        {
            var path = ResolveAssetPath(assetSubfolder, image.AssetFileName);
            if (path is null)
            {
                block.Item().Text($"Abb. {figureNumber}: {image.Caption} (Screenshot noch nicht hinterlegt)")
                    .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                    .FontSize(7).FontColor(Colors.Grey.Darken1);
                return;
            }

            block.Item()
                .AlignCenter()
                .MaxWidth(ImageMaxWidth)
                .Image(File.ReadAllBytes(path))
                .FitWidth();

            block.Item().PaddingTop(0).AlignCenter()
                .Text($"Abb. {figureNumber}: {image.Caption}")
                .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                .FontSize(7)
                .FontColor(Colors.Grey.Darken2);
        });
    }

    internal static string? ResolveAssetPath(string assetSubfolder, string fileName)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", assetSubfolder, fileName),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", assetSubfolder, fileName))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void ApplyPageFrame(PageDescriptor page, DateTime created)
    {
        page.Size(PageSizes.A4);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => PlanerPdfTextStyles.ApplyLight(x).FontSize(10).FontColor(Colors.Black));
        page.Footer().Element(c => PlanerPdfBranding.ComposeStandardFooter(c, created));
    }

    private static void ComposePageHeader(IContainer header, string titleLine)
    {
        PlanerPdfBranding.ComposeHeaderWithSmartLogo(header, left =>
        {
            left.Column(column =>
            {
                column.Item().Text(titleLine)
                    .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                    .FontSize(9).SemiBold().FontColor(PrimaryBlue);
                column.Item().PaddingTop(1).Text(text =>
                {
                    text.DefaultTextStyle(PlanerPdfTextStyles.ApplyLight(TextStyle.Default)
                        .FontSize(8)
                        .FontColor(Colors.Grey.Darken1));
                    text.Span("Seite ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });
    }

    private static string NormalizeBody(string body) =>
        string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
}

public sealed record OperatorManualDocument(
    string CoverTitle,
    string CoverSubtitle,
    string IntroText,
    IReadOnlyList<BriefingSection> Sections,
    string? CoverHint = null,
    string? ClosingNote = null);
