using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.AppShared.Pdf;
using SmartOepnv.Core.Zeitwirtschaft;

namespace SmartOepnv.AppShared.Zeitwirtschaft;

public static class ZeitwirtschaftPdfGenerator
{
    static ZeitwirtschaftPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public static void Generate(
        string outputPath,
        ZeitwirtschaftMergedEmployee employee,
        int year,
        int month,
        IReadOnlyList<ZeitwirtschaftTimeTableRow> rows)
    {
        var monthLabel = new DateTime(year, month, 1)
            .ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"));
        var totalDuration = ZeitwirtschaftMergeService.SumDurationHhMm(rows);
        var totalPause = ZeitwirtschaftMergeService.SumPauseMinutesHhMm(rows);
        var created = DateTime.Now;

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black));

                page.Header().Element(header =>
                    PlanerPdfBranding.ComposeHeaderWithSmartLogo(header, left =>
                    {
                        left.Column(column =>
                        {
                            column.Item().Text("Zeitwirtschaft").FontSize(18).SemiBold();
                            column.Item().Text($"{employee.DisplayLine} – {monthLabel}").FontSize(12);
                            column.Item().PaddingTop(6).Text($"Erstellt am {created:dd.MM.yyyy HH:mm}");
                        });
                    }));

                page.Footer().Element(c => PlanerPdfBranding.ComposeStandardFooter(c, created));

                page.Content().PaddingVertical(12).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.6f);
                        columns.RelativeColumn(1.6f);
                        columns.RelativeColumn(1.5f);
                        columns.ConstantColumn(70);
                        columns.ConstantColumn(72);
                        columns.RelativeColumn(2.4f);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellHeader).Text("Fahrzeug");
                        header.Cell().Element(CellHeader).Text("Kommen");
                        header.Cell().Element(CellHeader).Text("Gehen");
                        header.Cell().Element(CellHeader).Text("Pause");
                        header.Cell().Element(CellHeader).Text("Arbeitszeit");
                        header.Cell().Element(CellHeader).Text("Lohnstunden");
                        header.Cell().Element(CellHeader).Text("Hinweis");
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().Element(CellBody).Text(row.VehicleDisplayName);
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Kommen));
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Gehen));
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Pause, keepLineBreaks: true));
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Arbeitszeit));
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Lohnstunden));
                        table.Cell().Element(CellBody).Text(row.Hinweis);
                    }

                    if (rows.Count > 0)
                    {
                        table.Cell().Element(CellTotal).Text("Gesamtsumme");
                        table.Cell().Element(CellTotal).Text(string.Empty);
                        table.Cell().Element(CellTotal).Text(string.Empty);
                        table.Cell().Element(CellTotal).Text(totalPause);
                        table.Cell().Element(CellTotal).Text(totalDuration);
                        table.Cell().Element(CellTotal).Text(totalDuration);
                        table.Cell().Element(CellTotal).Text(string.Empty);
                    }
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static void WriteCell(IContainer container, ZeitwirtschaftTimeTableRow row, string text, bool keepLineBreaks = false)
    {
        var display = keepLineBreaks ? text.Trim() : text.Replace('\n', ' ').Trim();
        if (!row.IsVoided)
        {
            container.Text(display);
            return;
        }

        var reason = string.IsNullOrWhiteSpace(row.VoidReason) ? "Storno" : row.VoidReason.Trim();
        container.Text(textBlock =>
        {
            textBlock.Span(display).Strikethrough();
            textBlock.Span($" ({reason})").FontColor(Colors.Grey.Darken1);
        });
    }

    private static IContainer CellHeader(IContainer container) =>
        container
            .DefaultTextStyle(x => x.SemiBold())
            .PaddingVertical(4)
            .PaddingHorizontal(4)
            .Background(Colors.Grey.Lighten3)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Medium);

    private static IContainer CellBody(IContainer container) =>
        container
            .PaddingVertical(4)
            .PaddingHorizontal(4)
            .BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten2);

    private static IContainer CellTotal(IContainer container) =>
        container
            .DefaultTextStyle(x => x.SemiBold())
            .PaddingVertical(6)
            .PaddingHorizontal(4)
            .Background(Colors.Grey.Lighten3)
            .BorderTop(1)
            .BorderColor(Colors.Grey.Medium);
}
