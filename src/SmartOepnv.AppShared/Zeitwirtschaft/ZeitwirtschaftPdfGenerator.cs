using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

                page.Header().Column(column =>
                {
                    column.Item().Text("Zeitwirtschaft").FontSize(18).SemiBold();
                    column.Item().Text($"{employee.DisplayLine} – {monthLabel}").FontSize(12);
                    column.Item().PaddingTop(6).Text($"Erstellt am {DateTime.Now:dd.MM.yyyy HH:mm}");
                });

                page.Content().PaddingVertical(12).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(2.2f);
                        columns.RelativeColumn(2.2f);
                        columns.ConstantColumn(64);
                        columns.ConstantColumn(64);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellHeader).Text("Fahrzeug");
                        header.Cell().Element(CellHeader).Text("Kommen");
                        header.Cell().Element(CellHeader).Text("Gehen");
                        header.Cell().Element(CellHeader).Text("Arbeitszeit");
                        header.Cell().Element(CellHeader).Text("Lohnstunden");
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().Element(CellBody).Text(row.VehicleDisplayName);
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Kommen));
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Gehen));
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Arbeitszeit));
                        table.Cell().Element(CellBody).Element(c => WriteCell(c, row, row.Lohnstunden));
                    }

                    if (rows.Count > 0)
                    {
                        table.Cell().Element(CellTotal).Text("Gesamtsumme");
                        table.Cell().Element(CellTotal).Text(string.Empty);
                        table.Cell().Element(CellTotal).Text(string.Empty);
                        table.Cell().Element(CellTotal).Text(totalDuration);
                        table.Cell().Element(CellTotal).Text(totalDuration);
                    }
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static void WriteCell(IContainer container, ZeitwirtschaftTimeTableRow row, string text)
    {
        var display = text.Replace('\n', ' ').Trim();
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
