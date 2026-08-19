using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartOepnv.AppShared.Pdf;

/// <summary>
/// PDF der Routenschnur – Layout und Farben wie im Dialog „Routenschnur &amp; Fahrplan“.
/// </summary>
public static class RouteChainPdfGenerator
{
    // Entspricht RouteChainDialog (dunkles Planer-Panel)
    private const string PageBackground = "#0A1628";
    private const string CardBackground = "#12243A";
    private const string CardBorder = "#335577";
    private const string Accent = "#4FC3F7";
    private const string Muted = "#BBDEFB";
    private const string White = "#FFFFFF";

    static RouteChainPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public sealed record StopRow(string Name, string TimeDisplay, bool IsRouteChangeStop);

    public sealed record Segment(
        string Title,
        string MetaLine,
        IReadOnlyList<StopRow> Stops);

    public sealed record Model(
        string LineCourse,
        string? FilterSummary,
        string? ValiditySummary,
        IReadOnlyList<Segment> Segments);

    public static string BuildDefaultFileName(string? lineCourse)
    {
        var lc = string.IsNullOrWhiteSpace(lineCourse)
            ? "Schnur"
            : SanitizeFilePart(lineCourse);
        return $"Routenschnur_{lc}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
    }

    public static void Generate(string outputPath, Model model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var stamp = DateTime.Now;

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.PageColor(PageBackground);
                page.DefaultTextStyle(x =>
                    PlanerPdfTextStyles.ApplyLight(x)
                        .FontSize(11)
                        .FontColor(White)
                        .LineHeight(1.25f));

                page.Header().Element(h => ComposeHeader(h, model, stamp));
                page.Content().PaddingTop(10).Element(c => ComposeContent(c, model));
                page.Footer().PaddingTop(8).Row(footer =>
                {
                    footer.RelativeItem().AlignMiddle().Text(
                            $"Smart-ÖPNV · Routenschnur · {stamp:dd.MM.yyyy HH:mm}")
                        .FontSize(8)
                        .FontColor(Muted);
                    footer.ConstantItem(48).Height(36).AlignRight().AlignMiddle()
                        .Element(PlanerPdfBranding.DrawSmartOepnvLogo);
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static void ComposeHeader(IContainer container, Model model, DateTime stamp)
    {
        PlanerPdfBranding.ComposeHeaderWithSmartLogo(container, left =>
        {
            left.Column(col =>
            {
                col.Item().Text("Routenschnur & Fahrplan")
                    .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                    .FontSize(16)
                    .SemiBold()
                    .FontColor(White);

                var line = string.IsNullOrWhiteSpace(model.LineCourse)
                    ? "Linie/Kurs: —"
                    : $"Linie/Kurs: {model.LineCourse.Trim()}";
                col.Item().PaddingTop(4).Text(line)
                    .FontSize(12)
                    .FontColor(Accent);

                if (!string.IsNullOrWhiteSpace(model.FilterSummary))
                {
                    col.Item().PaddingTop(2).Text($"Prüfzeitraum: {model.FilterSummary}")
                        .FontSize(10)
                        .FontColor(Muted);
                }

                if (!string.IsNullOrWhiteSpace(model.ValiditySummary))
                {
                    col.Item().PaddingTop(2).Text(model.ValiditySummary)
                        .FontSize(10)
                        .FontColor(Accent);
                }

                col.Item().PaddingTop(2).Text($"Erstellt: {stamp:dd.MM.yyyy HH:mm}")
                    .FontSize(9)
                    .FontColor(Muted);
            });
        });
    }

    private static void ComposeContent(IContainer container, Model model)
    {
        if (model.Segments.Count == 0)
        {
            container.Text("Keine Fahrplandaten für diese Routenschnur.")
                .FontColor(Muted);
            return;
        }

        container.Column(col =>
        {
            for (var i = 0; i < model.Segments.Count; i++)
            {
                var segment = model.Segments[i];
                col.Item().Element(c => ComposeSegmentCard(c, segment));

                if (i < model.Segments.Count - 1)
                {
                    col.Item().PaddingVertical(6).AlignCenter()
                        .Text("↓ Routenwechsel")
                        .FontSize(11)
                        .SemiBold()
                        .FontColor(Accent);
                }
            }
        });
    }

    private static void ComposeSegmentCard(IContainer container, Segment segment)
    {
        container
            .Border(1)
            .BorderColor(CardBorder)
            .Background(CardBackground)
            .Padding(12)
            .Column(col =>
            {
                col.Item().Text(segment.Title)
                    .FontSize(13)
                    .SemiBold()
                    .FontColor(White);

                if (!string.IsNullOrWhiteSpace(segment.MetaLine))
                {
                    col.Item().PaddingTop(4).Text(segment.MetaLine)
                        .FontSize(10)
                        .FontColor(Muted);
                }

                col.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.ConstantColumn(52);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Haltestelle")
                            .FontSize(10)
                            .SemiBold()
                            .FontColor(Muted);
                        header.Cell().AlignRight().Text("Zeit")
                            .FontSize(10)
                            .SemiBold()
                            .FontColor(Muted);
                    });

                    foreach (var stop in segment.Stops)
                    {
                        var name = stop.IsRouteChangeStop
                            ? $"{stop.Name}  (Routenwechsel)"
                            : stop.Name;
                        var nameColor = stop.IsRouteChangeStop ? Accent : White;

                        table.Cell().PaddingTop(3).Text(name)
                            .FontSize(11)
                            .FontColor(nameColor);
                        table.Cell().PaddingTop(3).AlignRight().Text(stop.TimeDisplay)
                            .FontSize(11)
                            .FontColor(White);
                    }
                });
            });
    }

    private static string SanitizeFilePart(string value)
    {
        var trimmed = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '_');
        }

        return trimmed.Replace(' ', '_').Replace('/', '-');
    }
}
