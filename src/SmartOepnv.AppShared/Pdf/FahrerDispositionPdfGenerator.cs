using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Pdf;

public static class FahrerDispositionPdfGenerator
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private const int MaxDaysPerPage = 10;
    private const float DayBarHeight = 16f;
    private const float CaptionSlotHeight = 16f;
    private const float ConnectSlotWidth = 18f;
    private const float ConnectLineHeight = 3f;
    private const float ConnectArrowFontSize = 11f;
    private static readonly string ActiveBar = "#39FF14";
    private static readonly string ActiveBorder = "#76FF03";
    private static readonly string FreeBar = "#ECEFF1";
    private static readonly string GapBar = "#FFECB3";
    private static readonly string OvernightBar = "#29B6F6";
    private static readonly string OvernightBorder = "#0288D1";
    private static readonly string StandbyBar = "#9C27B0";
    private static readonly string StandbyBorder = "#7B1FA2";

    static FahrerDispositionPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public static void Generate(
        string outputPath,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyList<(string DriverKey, string DisplayName, IReadOnlyList<DriverDispositionAssignment> Assignments)> sections)
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
        var scopeLabel = sections.Count == 1
            ? sections[0].DisplayName
            : $"{sections.Count} Fahrer";

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
                                column.Item().Text("Personaldisposition").FontSize(16).SemiBold();
                                column.Item().Text($"Zeitraum: {rangeLabel}").FontSize(10);
                                column.Item().Text($"Ausgabe: {scopeLabel} · Balken mittig (blau = über Mitternacht) · Dienstnr. oben/unten je Dienst").FontSize(9);
                                column.Item().PaddingTop(2).Text($"Erstellt am {created:dd.MM.yyyy HH:mm}");
                            });
                        }));

                    page.Footer().Element(c => PlanerPdfBranding.ComposeStandardFooter(c, created));

                    page.Content().PaddingVertical(8).Column(outer =>
                    {
                        outer.Item().Element(ComposeLegend);
                        outer.Item().PaddingTop(6).Element(content =>
                        {
                            if (sections.Count == 0)
                            {
                                content.Text("Keine Fahrer im gewählten Zeitraum.");
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
                                    h.Cell().Element(NameHeader).Text("Fahrer");
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
                                    var captionById = AssignStableCaptionPlacements(section.Assignments, days);
                                    table.Cell().Element(c => NameBody(c, zebra)).Text(section.DisplayName);
                                    foreach (var day in days)
                                    {
                                        var segments = BuildDaySegments(section.Assignments, day, captionById);
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
            row.AutoItem().AlignMiddle().Text("Dienst").FontSize(8);
            row.ConstantItem(10);
            row.ConstantItem(14).Height(10).Background(OvernightBar).Border(0.5f).BorderColor(OvernightBorder);
            row.ConstantItem(4);
            row.AutoItem().AlignMiddle().Text("Über Mitternacht").FontSize(8);
            row.ConstantItem(10);
            row.ConstantItem(14).Height(10).Background(StandbyBar).Border(0.5f).BorderColor(StandbyBorder);
            row.ConstantItem(4);
            row.AutoItem().AlignMiddle().Text("Bereitschaft").FontSize(8);
            row.ConstantItem(10);
            row.ConstantItem(14).Height(10).Background(GapBar).Border(0.5f).BorderColor(Colors.Orange.Lighten1);
            row.ConstantItem(4);
            row.AutoItem().AlignMiddle().Text("Pause").FontSize(8);
            row.ConstantItem(10);
            row.ConstantItem(14).Height(10).Background(FreeBar).Border(0.5f).BorderColor(Colors.Grey.Lighten1);
            row.ConstantItem(4);
            row.AutoItem().AlignMiddle().Text("Frei").FontSize(8);
        });
    }

    private static void ComposeDayBar(IContainer container, IReadOnlyList<DaySegment> segments)
    {
        var captionsAbove = segments
            .Where(s => s.CaptionPlacement == CaptionPlacement.Above && !string.IsNullOrWhiteSpace(s.Caption))
            .Select(s => s.Caption)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var captionsBelow = segments
            .Where(s => s.CaptionPlacement == CaptionPlacement.Below && !string.IsNullOrWhiteSpace(s.Caption))
            .Select(s => s.Caption)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var connectOut = segments.Any(s => s.ConnectOutbound);
        var connectIn = segments.Any(s => s.ConnectInbound);
        var aboveText = captionsAbove.Count > 0 ? string.Join(" · ", captionsAbove) : " ";
        var belowText = captionsBelow.Count > 0 ? string.Join(" · ", captionsBelow) : " ";

        // Feste Caption-Slots oben/unten → Balken bleibt immer in der Mitte
        container.Column(col =>
        {
            col.Item().Height(CaptionSlotHeight).AlignMiddle().AlignCenter()
                .Text(aboveText)
                .FontSize(6.5f)
                .FontColor("#0A1628")
                .AlignCenter();

            col.Item().Height(DayBarHeight).Row(barRow =>
            {
                if (connectIn)
                {
                    barRow.ConstantItem(ConnectSlotWidth).Element(c => ComposeOvernightConnector(c, arrowFirst: false));
                }

                barRow.RelativeItem().Height(DayBarHeight).Row(row =>
                {
                    foreach (var seg in segments)
                    {
                        if (seg.WidthFraction <= 0.0001f)
                        {
                            continue;
                        }

                        var item = row.RelativeItem(seg.WidthFraction).Height(DayBarHeight);
                        if (seg.Kind == SegmentKind.Standby)
                        {
                            item.Background(StandbyBar).Border(0.4f).BorderColor(StandbyBorder);
                        }
                        else if (seg.Kind == SegmentKind.Overnight)
                        {
                            item.Background(OvernightBar).Border(0.4f).BorderColor(OvernightBorder);
                        }
                        else if (seg.Kind == SegmentKind.Active)
                        {
                            item.Background(ActiveBar).Border(0.4f).BorderColor(ActiveBorder);
                        }
                        else if (seg.Kind == SegmentKind.Gap)
                        {
                            item.Background(GapBar).Border(0.3f).BorderColor(Colors.Orange.Lighten1);
                        }
                        else
                        {
                            item.Background(FreeBar).Border(0.25f).BorderColor(Colors.Grey.Lighten2);
                        }
                    }
                });

                if (connectOut)
                {
                    barRow.ConstantItem(ConnectSlotWidth).Element(c => ComposeOvernightConnector(c, arrowFirst: true));
                }
            });

            col.Item().Height(CaptionSlotHeight).AlignMiddle().AlignCenter()
                .Text(belowText)
                .FontSize(6.5f)
                .FontColor("#0A1628")
                .AlignCenter();
        });
    }

    private static void ComposeOvernightConnector(IContainer container, bool arrowFirst)
    {
        // Linie per Padding zentrieren – kein Height(line) in Height(bar) (QuestPDF-Konflikt)
        var vPad = (DayBarHeight - ConnectLineHeight) / 2f;
        container.Height(DayBarHeight).Row(conn =>
        {
            if (arrowFirst)
            {
                conn.ConstantItem(12).AlignMiddle().AlignCenter()
                    .Text("▶").FontSize(ConnectArrowFontSize).FontColor(OvernightBorder).SemiBold();
                conn.RelativeItem().PaddingVertical(vPad).Background(OvernightBorder);
            }
            else
            {
                conn.RelativeItem().PaddingVertical(vPad).Background(OvernightBorder);
                conn.ConstantItem(12).AlignMiddle().AlignCenter()
                    .Text("▶").FontSize(ConnectArrowFontSize).FontColor(OvernightBorder).SemiBold();
            }
        });
    }

    /// <summary>
    /// Pro Dienst eine stabile Caption-Lage (oben/unten) über alle Tage hinweg.
    /// Dienste, die denselben Kalendertag teilen, bekommen unterschiedliche Lagen
    /// (früher Start → oben), damit z. B. 313 immer oben und 315 immer unten steht.
    /// </summary>
    private static Dictionary<string, CaptionPlacement> AssignStableCaptionPlacements(
        IReadOnlyList<DriverDispositionAssignment> assignments,
        IReadOnlyList<DateTime> days)
    {
        var result = new Dictionary<string, CaptionPlacement>(StringComparer.Ordinal);
        if (assignments.Count == 0 || days.Count == 0)
        {
            return result;
        }

        var rangeStart = days[0].Date;
        var rangeEndExclusive = days[^1].Date.AddDays(1);
        var rangeStartMs = new DateTimeOffset(rangeStart).ToUnixTimeMilliseconds();
        var rangeEndMs = new DateTimeOffset(rangeEndExclusive).ToUnixTimeMilliseconds();

        var visible = assignments
            .Where(a => a.StartEpochMs < rangeEndMs && a.EndEpochMs > rangeStartMs)
            .OrderBy(a => a.StartEpochMs)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();

        // Lane = Kalendertag-Kollision (nicht nur Zeitüberlappung), damit Morgen- und Abenddienst
        // am selben Tag getrennte Caption-Slots bekommen.
        var dayOccupancy = new Dictionary<DateTime, List<int>>();
        var laneById = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var assignment in visible)
        {
            var startLocal = DateTimeOffset.FromUnixTimeMilliseconds(assignment.StartEpochMs).LocalDateTime.Date;
            var endLocal = DateTimeOffset.FromUnixTimeMilliseconds(assignment.EndEpochMs).LocalDateTime;
            // Endet genau um Mitternacht → kein Folgetag
            var lastDay = endLocal.TimeOfDay == TimeSpan.Zero && endLocal > startLocal
                ? endLocal.Date.AddDays(-1)
                : endLocal.Date;
            if (lastDay < startLocal)
            {
                lastDay = startLocal;
            }

            var usedLanes = new HashSet<int>();
            for (var d = startLocal; d <= lastDay; d = d.AddDays(1))
            {
                if (d < rangeStart || d >= rangeEndExclusive)
                {
                    continue;
                }

                if (dayOccupancy.TryGetValue(d, out var lanes))
                {
                    foreach (var occupied in lanes)
                    {
                        usedLanes.Add(occupied);
                    }
                }
            }

            var lane = 0;
            while (usedLanes.Contains(lane))
            {
                lane++;
            }

            laneById[assignment.Id] = lane;
            for (var d = startLocal; d <= lastDay; d = d.AddDays(1))
            {
                if (d < rangeStart || d >= rangeEndExclusive)
                {
                    continue;
                }

                if (!dayOccupancy.TryGetValue(d, out var lanes))
                {
                    lanes = [];
                    dayOccupancy[d] = lanes;
                }

                lanes.Add(lane);
            }
        }

        foreach (var (id, lane) in laneById)
        {
            result[id] = lane == 0 ? CaptionPlacement.Above : CaptionPlacement.Below;
        }

        return result;
    }

    private static List<DaySegment> BuildDaySegments(
        IReadOnlyList<DriverDispositionAssignment> assignments,
        DateTime day,
        IReadOnlyDictionary<string, CaptionPlacement> captionById)
    {
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);
        var dayStartMs = new DateTimeOffset(dayStart).ToUnixTimeMilliseconds();
        var dayEndMs = new DateTimeOffset(dayEnd).ToUnixTimeMilliseconds();
        var dayMs = (double)(dayEndMs - dayStartMs);

        var workPieces = new List<(long Start, long End, DriverDispositionAssignment Assignment)>();
        foreach (var assignment in assignments.Where(a => a.StartEpochMs < dayEndMs && a.EndEpochMs > dayStartMs))
        {
            foreach (var (segStart, segEnd) in assignment.EnumerateWorkSegments())
            {
                var start = Math.Max(segStart, dayStartMs);
                var end = Math.Min(segEnd, dayEndMs);
                if (end <= start)
                {
                    continue;
                }

                workPieces.Add((start, end, assignment));
            }
        }

        workPieces = workPieces.OrderBy(p => p.Start).ToList();
        var segments = new List<DaySegment>();
        if (workPieces.Count == 0)
        {
            segments.Add(DaySegment.Empty(SegmentKind.Free, 1f));
            return segments;
        }

        long cursor = dayStartMs;
        for (var i = 0; i < workPieces.Count; i++)
        {
            var piece = workPieces[i];
            if (piece.Start > cursor)
            {
                var freeFrac = (piece.Start - cursor) / dayMs;
                // Pause nur innerhalb eines geteilten Dienstes; zwischen zwei Diensten = Frei
                var prev = i > 0 ? workPieces[i - 1] : default;
                var isSplitDutyPause = i > 0 &&
                                       prev.Assignment.Id == piece.Assignment.Id &&
                                       piece.Assignment.IsSplitShift;
                var kind = isSplitDutyPause ? SegmentKind.Gap : SegmentKind.Free;
                segments.Add(DaySegment.Empty(kind, (float)freeFrac));
            }

            var activeFrac = (piece.End - piece.Start) / dayMs;
            var fullStart = DateTimeOffset.FromUnixTimeMilliseconds(piece.Assignment.StartEpochMs).ToLocalTime();
            var fullEnd = DateTimeOffset.FromUnixTimeMilliseconds(piece.Assignment.EndEpochMs).ToLocalTime();
            var duty = !string.IsNullOrWhiteSpace(piece.Assignment.DutyNumber)
                ? piece.Assignment.DutyNumber.Trim()
                : !string.IsNullOrWhiteSpace(piece.Assignment.Label)
                    ? piece.Assignment.Label.Trim()
                    : string.Empty;

            var continuesToNextDay = piece.Assignment.EndEpochMs > dayEndMs;
            var continuedFromPrevDay = piece.Assignment.StartEpochMs < dayStartMs;
            var isStandby = DriverDispositionDutyNumberRules.IsStandbyAssignment(piece.Assignment);
            var overnight = !isStandby && (continuesToNextDay || continuedFromPrevDay ||
                            fullStart.Date != fullEnd.Date);

            var timePart = piece.Assignment.IsSplitShift
                ? FormatSplitFull(piece.Assignment)
                : $"{fullStart:HH:mm}–{fullEnd:HH:mm}";
            var caption = string.IsNullOrEmpty(duty) ? timePart : $"{duty} {timePart}";

            var placement = captionById.TryGetValue(piece.Assignment.Id, out var fixedPlacement)
                ? fixedPlacement
                : CaptionPlacement.Above;

            var segmentKind = isStandby
                ? SegmentKind.Standby
                : overnight
                    ? SegmentKind.Overnight
                    : SegmentKind.Active;

            segments.Add(new DaySegment(
                segmentKind,
                (float)activeFrac,
                caption,
                placement,
                ConnectOutbound: continuesToNextDay,
                ConnectInbound: continuedFromPrevDay));
            cursor = piece.End;
        }

        if (cursor < dayEndMs)
        {
            var freeFrac = (dayEndMs - cursor) / dayMs;
            segments.Add(DaySegment.Empty(SegmentKind.Free, (float)freeFrac));
        }

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

    private static string FormatSplitFull(DriverDispositionAssignment a)
    {
        var p1Start = DateTimeOffset.FromUnixTimeMilliseconds(a.StartEpochMs).ToLocalTime();
        var p1End = DateTimeOffset.FromUnixTimeMilliseconds(a.Part1EndEpochMs).ToLocalTime();
        var p2Start = DateTimeOffset.FromUnixTimeMilliseconds(a.Part2StartEpochMs).ToLocalTime();
        var p2End = DateTimeOffset.FromUnixTimeMilliseconds(a.EndEpochMs).ToLocalTime();
        return $"{p1Start:HH:mm}–{p1End:HH:mm} / {p2Start:HH:mm}–{p2End:HH:mm}";
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
            .MinHeight(CaptionSlotHeight + DayBarHeight + CaptionSlotHeight + 8)
            .PaddingVertical(3)
            .PaddingHorizontal(2)
            .Background(weekend
                ? (zebra ? Colors.Red.Lighten5 : Colors.Red.Lighten4)
                : (zebra ? Colors.Grey.Lighten4 : Colors.White))
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten1);

    private enum SegmentKind
    {
        Free,
        Gap,
        Active,
        Overnight,
        Standby
    }

    private enum CaptionPlacement
    {
        None,
        Above,
        Below
    }

    private readonly record struct DaySegment(
        SegmentKind Kind,
        float WidthFraction,
        string Caption,
        CaptionPlacement CaptionPlacement,
        bool ConnectOutbound,
        bool ConnectInbound)
    {
        public static DaySegment Empty(SegmentKind kind, float width) =>
            new(kind, width, string.Empty, CaptionPlacement.None, false, false);
    }
}
