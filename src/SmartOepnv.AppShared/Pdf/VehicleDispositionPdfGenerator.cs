using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Pdf;

public static class VehicleDispositionPdfGenerator
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private const int MaxDaysPerPage = 10;
    private const float DayBarHeight = 18f;
    private static readonly string ActiveBar = "#39FF14";
    private static readonly string ActiveBorder = "#76FF03";
    private static readonly string FreeBar = "#ECEFF1";
    private static readonly string GapBar = "#FFECB3";

    static VehicleDispositionPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public static void Generate(
        string outputPath,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyList<(string VehicleKey, string DisplayName, IReadOnlyList<VehicleDispositionAssignment> Assignments)> sections)
    {
        var created = DateTime.Now;
        var from = fromDate.Date;
        var to = toDate.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var allDays = Enumerable.Range(0, (to - from).Days + 1)
            .Select(i => from.AddDays(i))
            .ToList();
        var dayChunks = ChunkDays(allDays, MaxDaysPerPage);
        var rangeLabel = from == to
            ? from.ToString("dd.MM.yyyy", De)
            : $"{from:dd.MM.yyyy} – {to:dd.MM.yyyy}";

        Document.Create(document =>
        {
            foreach (var days in dayChunks)
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Black));

                    page.Header().Element(header =>
                        PlanerPdfBranding.ComposeHeaderWithSmartLogo(header, left =>
                        {
                            left.Column(column =>
                            {
                                column.Item().Text("Fahrzeugdisposition").FontSize(16).SemiBold();
                                column.Item().Text($"Zeitraum: {rangeLabel}").FontSize(10);
                                column.Item().Text($"Ausgabe: alle Fahrzeuge ({sections.Count}) · Balken = Einsatz, Lücke = Pause").FontSize(9);
                                column.Item().PaddingTop(2).Text($"Erstellt am {created:dd.MM.yyyy HH:mm}");
                            });
                        }));

                    page.Footer().Element(c => PlanerPdfBranding.ComposeStandardFooter(c, created));

                    page.Content().PaddingVertical(8).Column(outer =>
                    {
                        outer.Item().Element(c => ComposeLegend(c));
                        outer.Item().PaddingTop(6).Element(content =>
                        {
                            if (sections.Count == 0)
                            {
                                content.Text("Keine Fahrzeuge im gewählten Zeitraum.");
                                return;
                            }

                            content.Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(110);
                                    foreach (var _ in days)
                                    {
                                        cols.RelativeColumn();
                                    }
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Element(NameHeader).Text("Fahrzeug");
                                    foreach (var day in days)
                                    {
                                        h.Cell().Element(DayHeader).AlignCenter().Text(text =>
                                        {
                                            text.Span(day.ToString("ddd", De)).SemiBold().FontSize(8);
                                            text.Span("\n");
                                            text.Span(day.ToString("dd.MM.", De)).FontSize(8);
                                        });
                                    }
                                });

                                var rowIndex = 0;
                                foreach (var section in sections)
                                {
                                    var zebra = rowIndex % 2 == 1;
                                    rowIndex++;
                                    table.Cell().Element(c => NameBody(c, zebra)).Text(section.DisplayName);
                                    foreach (var day in days)
                                    {
                                        var segments = BuildDaySegments(section.Assignments, day);
                                        table.Cell().Element(c => DayBody(c, zebra, day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday))
                                            .Element(cell => ComposeDayBar(cell, segments));
                                    }
                                }
                            });
                        });
                    });
                });
            }
        }).GeneratePdf(outputPath);
    }

    private static void ComposeLegend(IContainer container)
    {
        container.Row(row =>
        {
            row.ConstantItem(14).Height(10).Background(ActiveBar).Border(0.5f).BorderColor(ActiveBorder);
            row.ConstantItem(4);
            row.AutoItem().AlignMiddle().Text("Einsatz").FontSize(8);
            row.ConstantItem(12);
            row.ConstantItem(14).Height(10).Background(GapBar).Border(0.5f).BorderColor(Colors.Orange.Lighten1);
            row.ConstantItem(4);
            row.AutoItem().AlignMiddle().Text("Pause zwischen Einsätzen").FontSize(8);
            row.ConstantItem(12);
            row.ConstantItem(14).Height(10).Background(FreeBar).Border(0.5f).BorderColor(Colors.Grey.Lighten1);
            row.ConstantItem(4);
            row.AutoItem().AlignMiddle().Text("Frei (kein Einsatz)").FontSize(8);
        });
    }

    private static void ComposeDayBar(IContainer container, IReadOnlyList<DaySegment> segments)
    {
        container.Column(col =>
        {
            col.Item().Height(DayBarHeight).Row(row =>
            {
                foreach (var seg in segments)
                {
                    if (seg.WidthFraction <= 0.0001f)
                    {
                        continue;
                    }

                    var item = row.RelativeItem(seg.WidthFraction).Height(DayBarHeight);
                    if (seg.Kind == SegmentKind.Active)
                    {
                        item.Background(ActiveBar)
                            .Border(0.5f)
                            .BorderColor(ActiveBorder)
                            .AlignMiddle()
                            .AlignCenter()
                            .PaddingHorizontal(1)
                            .Text(seg.Label)
                            .FontSize(5.5f)
                            .FontColor("#0A1628")
                            .WrapAnywhere();
                    }
                    else if (seg.Kind == SegmentKind.Gap)
                    {
                        item.Background(GapBar)
                            .Border(0.4f)
                            .BorderColor(Colors.Orange.Lighten1)
                            .AlignMiddle()
                            .AlignCenter()
                            .Text(seg.Label)
                            .FontSize(5)
                            .FontColor(Colors.Grey.Darken2);
                    }
                    else
                    {
                        item.Background(FreeBar).Border(0.3f).BorderColor(Colors.Grey.Lighten2);
                    }
                }
            });

            var caption = string.Join(" · ", segments
                .Where(s => s.Kind == SegmentKind.Active && !string.IsNullOrWhiteSpace(s.Caption))
                .Select(s => s.Caption));
            if (!string.IsNullOrWhiteSpace(caption))
            {
                col.Item().PaddingTop(1).AlignCenter().Text(caption).FontSize(5.5f).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    private static List<DaySegment> BuildDaySegments(
        IReadOnlyList<VehicleDispositionAssignment> assignments,
        DateTime day)
    {
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);
        var dayStartMs = new DateTimeOffset(dayStart).ToUnixTimeMilliseconds();
        var dayEndMs = new DateTimeOffset(dayEnd).ToUnixTimeMilliseconds();
        var dayMs = (double)(dayEndMs - dayStartMs);

        var trips = assignments
            .Where(a => a.StartEpochMs < dayEndMs && a.EndEpochMs > dayStartMs)
            .Select(a => (
                Start: Math.Max(a.StartEpochMs, dayStartMs),
                End: Math.Min(a.EndEpochMs, dayEndMs),
                Assignment: a))
            .Where(t => t.End > t.Start)
            .OrderBy(t => t.Start)
            .ToList();

        var segments = new List<DaySegment>();
        if (trips.Count == 0)
        {
            segments.Add(new DaySegment(SegmentKind.Free, 1f, string.Empty, string.Empty));
            return segments;
        }

        long cursor = dayStartMs;
        for (var i = 0; i < trips.Count; i++)
        {
            var trip = trips[i];
            if (trip.Start > cursor)
            {
                var freeFrac = (trip.Start - cursor) / dayMs;
                var kind = i == 0 ? SegmentKind.Free : SegmentKind.Gap;
                var gapLabel = kind == SegmentKind.Gap
                    ? FormatDurationShort(trip.Start - cursor)
                    : string.Empty;
                segments.Add(new DaySegment(kind, (float)freeFrac, gapLabel, string.Empty));
            }

            var activeFrac = (trip.End - trip.Start) / dayMs;
            var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(trip.Start).ToLocalTime();
            var endLocal = DateTimeOffset.FromUnixTimeMilliseconds(trip.End).ToLocalTime();
            var barLabel = $"{startLocal:HH:mm}";
            var caption = string.IsNullOrWhiteSpace(trip.Assignment.Label)
                ? $"{startLocal:HH:mm}–{endLocal:HH:mm}"
                : $"{trip.Assignment.Label.Trim()} {startLocal:HH:mm}–{endLocal:HH:mm}";
            segments.Add(new DaySegment(SegmentKind.Active, (float)activeFrac, barLabel, caption));
            cursor = trip.End;
        }

        if (cursor < dayEndMs)
        {
            var freeFrac = (dayEndMs - cursor) / dayMs;
            // Rest des Tages ohne weiteren Einsatz = Frei (nicht Pause zwischen Einsätzen)
            segments.Add(new DaySegment(SegmentKind.Free, (float)freeFrac, string.Empty, string.Empty));
        }

        // Anteile normalisieren (Rundungsfehler)
        var sum = segments.Sum(s => s.WidthFraction);
        if (sum > 0 && Math.Abs(sum - 1f) > 0.001f)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                segments[i] = s with { WidthFraction = s.WidthFraction / sum };
            }
        }

        return segments;
    }

    private static string FormatDurationShort(long durationMs)
    {
        var minutes = Math.Max(0, (int)Math.Round(durationMs / 60000.0));
        if (minutes < 60)
        {
            return $"{minutes}m";
        }

        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h}h" : $"{h}h{m:D2}";
    }

    private static List<List<DateTime>> ChunkDays(IReadOnlyList<DateTime> days, int size)
    {
        var chunks = new List<List<DateTime>>();
        for (var i = 0; i < days.Count; i += size)
        {
            chunks.Add(days.Skip(i).Take(size).ToList());
        }

        return chunks.Count == 0 ? [[DateTime.Today]] : chunks;
    }

    private static IContainer NameHeader(IContainer container) =>
        container
            .DefaultTextStyle(x => x.SemiBold().FontSize(8))
            .Padding(4)
            .Background(Colors.Grey.Lighten3)
            .Border(0.75f)
            .BorderColor(Colors.Grey.Medium);

    private static IContainer DayHeader(IContainer container) =>
        container
            .Padding(3)
            .Background(Colors.Grey.Lighten3)
            .Border(0.75f)
            .BorderColor(Colors.Grey.Medium);

    private static IContainer NameBody(IContainer container, bool zebra) =>
        container
            .Padding(4)
            .Background(zebra ? Colors.Grey.Lighten4 : Colors.White)
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten1)
            .DefaultTextStyle(x => x.SemiBold().FontSize(8));

    private static IContainer DayBody(IContainer container, bool zebra, bool weekend) =>
        container
            .MinHeight(34)
            .Padding(3)
            .Background(weekend
                ? (zebra ? Colors.Red.Lighten5 : Colors.Red.Lighten4)
                : (zebra ? Colors.Grey.Lighten4 : Colors.White))
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten1);

    private enum SegmentKind
    {
        Free,
        Gap,
        Active
    }

    private readonly record struct DaySegment(
        SegmentKind Kind,
        float WidthFraction,
        string Label,
        string Caption);
}
