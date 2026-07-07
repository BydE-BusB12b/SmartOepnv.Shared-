using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.Core;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Dienstvorlagen;

public static class DienstvorlagenPdfGenerator
{
    private const string DarkBlue = "#002171";
    private const string PrimaryBlue = "#0D47A1";
    private const string LightBlue = "#5472D3";
    private const string AccentBlue = "#42A5F5";
    private const string SurfaceBlue = "#E8EEF8";
    private const string RowAltBlue = "#C8DBF5";
    private const string TextDark = "#0A1020";
    private const string TextMuted = "#4A5F82";
    private const float FooterLogoWidth = 144f;
    private const float FooterLogoHeight = 54f;

    static DienstvorlagenPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public static void Generate(string outputPath, DutyTemplate template) =>
        GeneratePart(outputPath, template, template.Rows, template.DutyNumber, 1);

    public static void GeneratePart(
        string outputPath,
        DutyTemplate template,
        IReadOnlyList<DutyTemplateRow> rows,
        string dutyNumber,
        int part = 1)
    {
        var prep = DutyTemplateCalculator.ResolvePreparationMinutes(template.WorkPreparationMinutes);
        var followUp = DutyTemplateCalculator.ResolveFollowUpMinutes(template.WorkFollowUpMinutes);
        var deduction = DutyTemplateCalculator.ResolveUnpaidBreakDeductionMinutes(template, part);

        DutyTemplateStats stats;
        IReadOnlyList<DutyTemplateRemarkEntry> remarkLegend;
        IReadOnlyList<DutyTripPdfTableRow> tripRows;
        string serviceStart;
        string serviceEnd;

        if (template.IsSplitShift && template.Part2Rows.Count > 0)
        {
            stats = DutyTemplateCalculator.ComputeSplitShiftSummary(
                template.Rows,
                template.Part2Rows,
                prep,
                followUp,
                deduction);
            remarkLegend = DutyTemplateRemarkHelper.BuildLegend(
                template.Rows.Concat(template.Part2Rows));
            tripRows = BuildSplitShiftTripRows(template, prep, followUp);
            serviceStart = DutyTemplateCalculator.GetServiceStartDisplay(template.Rows, prep) ?? "–";
            serviceEnd = DutyTemplateCalculator.GetServiceEndDisplay(template.Part2Rows, followUp) ?? "–";
        }
        else
        {
            var orderedRows = DutyTemplateCalculator.OrderRows(rows);
            stats = DutyTemplateCalculator.ComputePart(orderedRows, prep, followUp, deduction);
            remarkLegend = DutyTemplateRemarkHelper.BuildLegend(rows);
            tripRows = BuildTripRows(rows).Select(DutyTripPdfTableRow.FromTrip).ToList();
            serviceStart = DutyTemplateCalculator.GetServiceStartDisplay(rows, prep) ?? "–";
            serviceEnd = DutyTemplateCalculator.GetServiceEndDisplay(rows, followUp) ?? "–";
        }

        Document.Create(document =>
        {
            RenderDutyPage(
                document,
                template,
                dutyNumber,
                stats,
                prep,
                followUp,
                remarkLegend,
                tripRows,
                serviceStart,
                serviceEnd);
        }).GeneratePdf(outputPath);
    }

    private static void RenderDutyPage(
        IDocumentContainer document,
        DutyTemplate template,
        string dutyNumber,
        DutyTemplateStats stats,
        int preparationMinutes,
        int followUpMinutes,
        IReadOnlyList<DutyTemplateRemarkEntry> remarkLegend,
        IReadOnlyList<DutyTripPdfTableRow> tripRows,
        string serviceStart,
        string serviceEnd)
    {
        var dutyDisplay = string.IsNullOrWhiteSpace(dutyNumber) ? "–" : dutyNumber.Trim();
        var operatingDay = string.IsNullOrWhiteSpace(template.OperatingDay) ? "–" : template.OperatingDay.Trim();
        var companyLogoPath = ResolveCompanyLogoPath(template.CompanyLogoId);

        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor(TextDark));

            page.Content().Column(content =>
            {
                content.Item().Background(DarkBlue).PaddingVertical(8).PaddingHorizontal(12).Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Element(c => HeaderField(c, "Dienst", dutyDisplay));
                        row.ConstantItem(12);
                        row.RelativeItem().Element(c => HeaderField(c, "Betriebstag", operatingDay));
                    });
                });

                content.Item().PaddingTop(10).Row(statsRow =>
                {
                    statsRow.RelativeItem().Element(c => StatCard(c, "Dienstanfang", serviceStart));
                    statsRow.ConstantItem(6);
                    statsRow.RelativeItem().Element(c => StatCard(c, "Dienstende", serviceEnd));
                    statsRow.ConstantItem(6);
                    statsRow.RelativeItem().Element(c => StatCard(c, "Dienstlänge", stats.ServiceDurationDisplay));
                    statsRow.ConstantItem(6);
                    statsRow.RelativeItem().Element(c => StatCard(c, "Lohn-Std.", stats.PayHoursDisplay));
                    statsRow.ConstantItem(6);
                    statsRow.RelativeItem().Element(c => StatCard(c, "Lenkzeit", stats.PureDrivingDisplay));
                    statsRow.ConstantItem(6);
                    statsRow.RelativeItem().Element(c => StatCard(c, "Pausenzeit", stats.PureBreakDisplay));
                });

                if (preparationMinutes > 0 || followUpMinutes > 0)
                {
                    content.Item().PaddingTop(6).Text(text =>
                    {
                        if (preparationMinutes > 0)
                        {
                            text.Span("Arbeitsvorbereitung: ").SemiBold().FontColor(PrimaryBlue);
                            text.Span($"{preparationMinutes} Min.");
                        }

                        if (preparationMinutes > 0 && followUpMinutes > 0)
                        {
                            text.Span("   ·   ");
                        }

                        if (followUpMinutes > 0)
                        {
                            text.Span("Arbeitsnachbereitung: ").SemiBold().FontColor(PrimaryBlue);
                            text.Span($"{followUpMinutes} Min.");
                        }
                    });
                }

                content.Item().PaddingTop(12).LineHorizontal(2).LineColor(PrimaryBlue);

                content.Item().PaddingTop(10).Text("Fahrten")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(PrimaryBlue);

                var tripRowList = tripRows.ToList();

                content.Item().PaddingTop(6).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(52);
                        columns.ConstantColumn(58);
                        columns.ConstantColumn(42);
                        columns.RelativeColumn(1);
                        columns.ConstantColumn(42);
                        columns.ConstantColumn(46);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(TableHeaderCell).Text("Fahrtnr.");
                        header.Cell().Element(TableHeaderCell).Text("Linie/Kurs");
                        header.Cell().Element(TableHeaderCell).Text("Ab:");
                        header.Cell().Element(TableHeaderCell).Text("Richtung");
                        header.Cell().Element(TableHeaderCellRight).Text("An:");
                        header.Cell().Element(TableHeaderCellRight).Text("Bemerkung");
                    });

                    for (var i = 0; i < tripRowList.Count; i++)
                    {
                        RenderTripTableRow(table, tripRowList[i], i % 2 == 0);
                    }

                    if (tripRowList.Count == 0)
                    {
                        table.Cell().ColumnSpan(6).Element(c => TableBodyCell(c, true))
                            .Text("Keine Fahrten erfasst.");
                    }
                });

                if (remarkLegend.Count > 0)
                {
                    content.Item().PaddingTop(10).Column(legend =>
                    {
                        legend.Item().Text("Bemerkungen")
                            .FontSize(11)
                            .SemiBold()
                            .FontColor(PrimaryBlue);
                        foreach (var remarkEntry in remarkLegend)
                        {
                            legend.Item().PaddingTop(3).Text(descriptor =>
                            {
                                descriptor.Span($"{remarkEntry.Code} ").SemiBold().FontColor(PrimaryBlue);
                                descriptor.Span(remarkEntry.Text);
                            });
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(template.VehicleNumber) || !string.IsNullOrWhiteSpace(template.Notes))
                {
                    content.Item().PaddingTop(12).Background(SurfaceBlue).Padding(10).Column(meta =>
                    {
                        if (!string.IsNullOrWhiteSpace(template.VehicleNumber))
                        {
                            meta.Item().Text(text =>
                            {
                                text.Span("Fahrzeug: ").SemiBold().FontColor(PrimaryBlue);
                                text.Span($"Bus {template.VehicleNumber.Trim()}");
                            });
                        }

                        if (!string.IsNullOrWhiteSpace(template.Notes))
                        {
                            meta.Item().PaddingTop(4).Text(template.Notes.Trim()).FontColor(TextMuted);
                        }
                    });
                }
            });

            page.Footer().PaddingTop(4).Row(footerRow =>
            {
                footerRow.RelativeItem().AlignMiddle().Text(text =>
                {
                    text.Span("Smart-ÖPNV · ").FontColor(TextMuted).FontSize(8);
                    text.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm")).FontColor(LightBlue).FontSize(8);
                });

                footerRow.ConstantItem(FooterLogoWidth).Height(FooterLogoHeight).AlignRight().AlignMiddle()
                    .Element(c => DrawFooterLogo(c, companyLogoPath));
            });
        });
    }

    private static string? ResolveCompanyLogoPath(string? logoId)
    {
        if (!AppServices.IsInitialized || !AppServices.IsPlannerApp || string.IsNullOrWhiteSpace(logoId))
        {
            return null;
        }

        return PlanerBrandingWorkspace.TryGetLogoPath(AppServices.SettingsSubfolder, logoId);
    }

    private static void DrawFooterLogo(IContainer container, string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(logoPath);
            container.Image(bytes).FitArea();
        }
        catch
        {
            // Logo optional – PDF ohne Logo fortsetzen
        }
    }

    private static void HeaderField(IContainer container, string label, string value)
    {
        container.Column(column =>
        {
            column.Item().Text(label.ToUpperInvariant())
                .FontSize(7)
                .FontColor(AccentBlue);
            column.Item().PaddingTop(1).Text(value)
                .FontSize(11)
                .SemiBold()
                .FontColor(Colors.White);
        });
    }

    private static void StatCard(IContainer container, string label, string value)
    {
        container
            .Background(SurfaceBlue)
            .Border(1)
            .BorderColor(LightBlue)
            .PaddingVertical(6)
            .PaddingHorizontal(5)
            .Column(column =>
            {
                column.Item().Text(label).FontSize(7).FontColor(TextMuted);
                column.Item().PaddingTop(1).Text(value).FontSize(11).SemiBold().FontColor(PrimaryBlue);
            });
    }

    private static IContainer TableHeaderCell(IContainer container) =>
        container
            .Background(PrimaryBlue)
            .PaddingVertical(6)
            .PaddingHorizontal(6)
            .DefaultTextStyle(x => x.SemiBold().FontColor(Colors.White).FontSize(8));

    private static IContainer TableHeaderCellRight(IContainer container) =>
        TableHeaderCell(container).AlignRight();

    private static IContainer TableBodyCell(IContainer container, bool even) =>
        container
            .Background(even ? Colors.White : RowAltBlue)
            .BorderBottom(0.5f)
            .BorderColor(SurfaceBlue)
            .PaddingVertical(5)
            .PaddingHorizontal(6);

    private static IContainer TableBodyCellRight(IContainer container, bool even) =>
        TableBodyCell(container, even).AlignRight();

    private static void RenderTripTableRow(TableDescriptor table, DutyTripPdfTableRow row, bool even)
    {
        switch (row.Kind)
        {
            case DutyTripPdfRowKind.Empty:
                table.Cell().Element(c => TableBodyCell(c, even)).Text(string.Empty);
                table.Cell().Element(c => TableBodyCell(c, even)).Text(string.Empty);
                table.Cell().Element(c => TableBodyCell(c, even)).Text(string.Empty);
                table.Cell().Element(c => TableBodyCell(c, even)).Text(string.Empty);
                table.Cell().Element(c => TableBodyCellRight(c, even)).Text(string.Empty);
                table.Cell().Element(c => TableBodyCellRight(c, even)).Text(string.Empty);
                break;

            case DutyTripPdfRowKind.Dienstfrei:
                table.Cell().ColumnSpan(6).Element(c => TableBodyCell(c, even).AlignCenter())
                    .Text(row.Direction)
                    .SemiBold()
                    .FontColor(PrimaryBlue)
                    .FontSize(9);
                break;

            case DutyTripPdfRowKind.PartHeader:
                table.Cell().ColumnSpan(6).Element(c => TableBodyCell(c, even))
                    .Text(row.Direction)
                    .SemiBold()
                    .FontColor(PrimaryBlue)
                    .FontSize(9);
                break;

            default:
                table.Cell().Element(c => TableBodyCell(c, even)).Text(row.TripNumber);
                table.Cell().Element(c => TableBodyCell(c, even)).Text(row.LineCourse);
                table.Cell().Element(c => TableBodyCell(c, even)).Text(row.FromTime);
                table.Cell().Element(c => TableBodyCell(c, even)).Text(row.Direction);
                table.Cell().Element(c => TableBodyCellRight(c, even)).Text(row.ToTime);
                table.Cell().Element(c => TableBodyCellRight(c, even)).Text(row.RemarkCode);
                break;
        }
    }

    private static List<DutyTripPdfTableRow> BuildSplitShiftTripRows(
        DutyTemplate template,
        int preparationMinutes,
        int followUpMinutes)
    {
        var rows = new List<DutyTripPdfTableRow>();
        foreach (var row in template.Rows)
        {
            rows.Add(DutyTripPdfTableRow.FromTrip(MapTripRow(row)));
        }

        rows.Add(DutyTripPdfTableRow.Empty());

        var freeFrom = DutyTemplateCalculator.GetServiceEndDisplay(template.Rows, followUpMinutes) ?? "–";
        var freeTo = DutyTemplateCalculator.GetServiceStartDisplay(template.Part2Rows, preparationMinutes) ?? "–";
        rows.Add(DutyTripPdfTableRow.Dienstfrei(
            $"Ab {freeFrom} – DIENSTFREI bis {freeTo}"));

        rows.Add(DutyTripPdfTableRow.Empty());
        rows.Add(DutyTripPdfTableRow.PartHeader("2. Dienstteil"));

        foreach (var row in template.Part2Rows)
        {
            rows.Add(DutyTripPdfTableRow.FromTrip(MapTripRow(row)));
        }

        return rows;
    }

    private static IEnumerable<DutyTripPdfRow> BuildTripRows(IReadOnlyList<DutyTemplateRow> rows)
    {
        foreach (var row in rows)
        {
            yield return MapTripRow(row);
        }
    }

    private static DutyTripPdfRow MapTripRow(DutyTemplateRow row)
    {
        var fromStop = FormatDirectionStop(row.FromStop);
        var toStop = FormatDirectionStop(row.ToStop);
        var direction = string.IsNullOrWhiteSpace(fromStop) && string.IsNullOrWhiteSpace(toStop)
            ? string.Empty
            : $"{fromStop} > {toStop}".Trim(' ', '>').Trim();

        return new DutyTripPdfRow(
            row.TripNumber,
            row.LineCourse,
            FormatTimeDisplay(row.FromTime),
            direction,
            FormatTimeDisplay(row.ToTime),
            DutyTemplateRemarkHelper.GetDisplayCode(row.Remark));
    }

    private static string FormatTimeDisplay(string? time)
    {
        if (string.IsNullOrWhiteSpace(time))
        {
            return string.Empty;
        }

        var minutes = DutyTemplateCalculator.ParseMinutes(time);
        if (minutes is null)
        {
            return time.Trim();
        }

        var hours = minutes.Value / 60;
        var mins = minutes.Value % 60;
        return $"{hours:00}:{mins:00}";
    }

    private static string FormatDirectionStop(string? stop)
    {
        if (string.IsNullOrWhiteSpace(stop))
        {
            return string.Empty;
        }

        return DutyTemplateStopNameHelper.StripHaltestelleMarker(stop).Trim();
    }

    private sealed record DutyTripPdfRow(
        string TripNumber,
        string LineCourse,
        string FromTime,
        string Direction,
        string ToTime,
        string RemarkCode);

    private enum DutyTripPdfRowKind
    {
        Trip,
        Empty,
        Dienstfrei,
        PartHeader
    }

    private sealed record DutyTripPdfTableRow(
        DutyTripPdfRowKind Kind,
        string TripNumber,
        string LineCourse,
        string FromTime,
        string Direction,
        string ToTime,
        string RemarkCode)
    {
        public static DutyTripPdfTableRow FromTrip(DutyTripPdfRow trip) =>
            new(DutyTripPdfRowKind.Trip, trip.TripNumber, trip.LineCourse, trip.FromTime, trip.Direction, trip.ToTime, trip.RemarkCode);

        public static DutyTripPdfTableRow Empty() =>
            new(DutyTripPdfRowKind.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        public static DutyTripPdfTableRow Dienstfrei(string text) =>
            new(DutyTripPdfRowKind.Dienstfrei, string.Empty, string.Empty, string.Empty, text, string.Empty, string.Empty);

        public static DutyTripPdfTableRow PartHeader(string text) =>
            new(DutyTripPdfRowKind.PartHeader, string.Empty, string.Empty, string.Empty, text, string.Empty, string.Empty);
    }
}
