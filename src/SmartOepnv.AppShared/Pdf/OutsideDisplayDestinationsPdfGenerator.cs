using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Pdf;

/// <summary>
/// PDF-Export der Außenanzeige-Zielliste.
/// Spalten: ID, Name, Linie, Ziel/Seite oben-unten und Wechseltexte.
/// Zeilen ohne ITCS-Listen-Flag sind gelb hinterlegt.
/// </summary>
public static class OutsideDisplayDestinationsPdfGenerator
{
    private const string PrimaryBlue = "#0D47A1";
    private const string TextMuted = "#4A5F82";
    private const string RowEven = "#F4F7FB";
    private const string RowOdd = "#FFFFFF";
    /// <summary>Nicht in der ITCS-Auswahlliste.</summary>
    private const string RowNotInItcsList = "#FFF59D";

    static OutsideDisplayDestinationsPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public sealed record DestinationPdfRow(
        string Id,
        string Name,
        string Line,
        string FrontPrimary,
        string SidePrimary,
        string FrontWechsel,
        string SideWechsel,
        bool InItcsList);

    public static string BuildDefaultFileName() =>
        $"Ziele_Aussenanzeige_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

    public static DestinationPdfRow FromProgram(OutsideDisplayProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var frontGoals = CollectGoals(program.FrontCycles, program.FrontLine1, program.FrontLine2);
        var sideGoals = CollectGoals(program.SideCycles, program.SideLine1, program.SideLine2);
        if (sideGoals.Count == 0)
        {
            sideGoals = frontGoals;
        }

        return new DestinationPdfRow(
            Id: OutsideDisplayId.ToDisplayNumber(program.Id),
            Name: string.IsNullOrWhiteSpace(program.Name) ? "(ohne Name)" : program.Name.Trim(),
            Line: FormatLine(program.Ds001Type, program.Ds001Value, program.Ds001Spec),
            FrontPrimary: FormatGoalAt(frontGoals, 0),
            SidePrimary: FormatGoalAt(sideGoals, 0),
            FrontWechsel: FormatGoalAt(frontGoals, 1),
            SideWechsel: FormatGoalAt(sideGoals, 1),
            InItcsList: program.IsListEnabled);
    }

    public static void Generate(string outputPath, IReadOnlyList<OutsideDisplayProgram> programs)
    {
        var stamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
        var groups = programs
            .OrderBy(p => p, Comparer<OutsideDisplayProgram>.Create(OutsideDisplayProgram.CompareForZielliste))
            .GroupBy(p => p.Protocol)
            .Select(g => (
                Protocol: g.Key,
                Label: g.First().ProtocolLabel,
                Rows: g.Select(FromProgram).ToList()))
            .ToList();
        var totalCount = groups.Sum(g => g.Rows.Count);

        Document.Create(document =>
        {
            if (groups.Count == 0)
            {
                document.Page(page =>
                {
                    ConfigurePage(page);
                    page.Header().Element(h => ComposeDocHeader(h, stamp, totalCount, protocolLabel: null));
                    page.Content().PaddingTop(8).Text("Keine Ziele in der Zielliste.").FontColor(TextMuted);
                    page.Footer().Element(ComposeFooter);
                });
                return;
            }

            // Ein Seitenabschnitt (Cut) pro Protokolltyp: DS021T, DS021neu, DS003a, …
            foreach (var group in groups)
            {
                document.Page(page =>
                {
                    ConfigurePage(page);
                    page.Header().Element(h => ComposeDocHeader(h, stamp, group.Rows.Count, group.Label));
                    page.Content().PaddingTop(8).Element(c => ComposeProtocolTable(c, group.Rows));
                    page.Footer().Element(ComposeFooter);
                });
            }
        }).GeneratePdf(outputPath);
    }

    private static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.A4.Landscape());
        page.Margin(28);
        page.DefaultTextStyle(x =>
            PlanerPdfTextStyles.ApplyLight(x).FontSize(9).FontColor(Colors.Black).LineHeight(1.2f));
    }

    private static void ComposeDocHeader(IContainer container, string stamp, int count, string? protocolLabel)
    {
        container.Column(col =>
        {
            col.Item().Text("Außenanzeige – alle Ziele")
                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                .FontSize(14)
                .SemiBold()
                .FontColor(PrimaryBlue);

            if (!string.IsNullOrWhiteSpace(protocolLabel))
            {
                col.Item().PaddingTop(4).Text($"Protokoll: {protocolLabel}")
                    .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                    .FontSize(11)
                    .SemiBold()
                    .FontColor("#1A2B45");
            }

            col.Item().PaddingTop(2).Text(
                    string.IsNullOrWhiteSpace(protocolLabel)
                        ? $"Erstellt: {stamp} · {count} Einträge"
                        : $"Erstellt: {stamp} · {count} Einträge in diesem Protokoll")
                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                .FontSize(8)
                .FontColor(TextMuted);

            col.Item().PaddingTop(2).Text("Gelb = nicht in der ITCS-Auswahlliste")
                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                .FontSize(8)
                .FontColor(TextMuted);
        });
    }

    private static void ComposeFooter(IContainer container) =>
        container.AlignCenter().Text(text =>
        {
            text.Span("Seite ").FontSize(8).FontColor(TextMuted);
            text.CurrentPageNumber().FontSize(8).FontColor(TextMuted);
            text.Span(" / ").FontSize(8).FontColor(TextMuted);
            text.TotalPages().FontSize(8).FontColor(TextMuted);
        });

    private static void ComposeProtocolTable(IContainer content, IReadOnlyList<DestinationPdfRow> rows)
    {
        content.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(36); // 1 ID
                columns.RelativeColumn(1.35f); // 2 Zielname
                columns.RelativeColumn(0.65f); // 3 Linie
                columns.RelativeColumn(1.25f); // 4 Ziel oben/unten
                columns.RelativeColumn(1.25f); // 5 Seite oben/unten
                columns.RelativeColumn(1.1f); // 6 Ziel Wechsel
                columns.RelativeColumn(1.1f); // 7 Seite Wechsel
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("1 ID");
                header.Cell().Element(HeaderCell).Text("2 Zielname");
                header.Cell().Element(HeaderCell).Text("3 Linie");
                header.Cell().Element(HeaderCell).Text("4 Ziel oben/unten");
                header.Cell().Element(HeaderCell).Text("5 Seite oben/unten");
                header.Cell().Element(HeaderCell).Text("6 Ziel Wechsel");
                header.Cell().Element(HeaderCell).Text("7 Seite Wechsel");
            });

            var even = false;
            foreach (var row in rows)
            {
                var bg = row.InItcsList
                    ? (even ? RowEven : RowOdd)
                    : RowNotInItcsList;
                even = !even;
                table.Cell().Element(c => BodyCell(c, bg)).Text(BlankDash(row.Id));
                table.Cell().Element(c => BodyCell(c, bg)).Text(row.Name);
                table.Cell().Element(c => BodyCell(c, bg)).Text(BlankDash(row.Line));
                table.Cell().Element(c => BodyCell(c, bg)).Text(BlankDash(row.FrontPrimary));
                table.Cell().Element(c => BodyCell(c, bg)).Text(BlankDash(row.SidePrimary));
                table.Cell().Element(c => BodyCell(c, bg)).Text(BlankDash(row.FrontWechsel));
                table.Cell().Element(c => BodyCell(c, bg)).Text(BlankDash(row.SideWechsel));
            }
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container
            .BorderBottom(1)
            .BorderColor("#CCD6E4")
            .Background("#E8EEF7")
            .PaddingVertical(5)
            .PaddingHorizontal(4)
            .DefaultTextStyle(x => PlanerPdfTextStyles.ApplyRegular(x)
                .FontSize(8)
                .SemiBold()
                .FontColor("#1A2B45"));

    private static IContainer BodyCell(IContainer container, string background) =>
        container
            .BorderBottom(0.5f)
            .BorderColor("#E2E8F0")
            .Background(background)
            .PaddingVertical(4)
            .PaddingHorizontal(4)
            .DefaultTextStyle(x => PlanerPdfTextStyles.ApplyLight(x).FontSize(8).FontColor(Colors.Black));

    private static IReadOnlyList<(string Line1, string Line2)> CollectGoals(
        IEnumerable<OutsideDisplayTextCycle>? cycles,
        string? legacyLine1,
        string? legacyLine2)
    {
        var fromCycles = (cycles ?? [])
            .Where(c => c is not null && c.HasContent)
            .Select(c => c.ToGoalPair())
            .Select(p => (Line1: p.Line1 ?? string.Empty, Line2: p.Line2 ?? string.Empty))
            .ToList();
        if (fromCycles.Count > 0)
        {
            return fromCycles;
        }

        if (string.IsNullOrWhiteSpace(legacyLine1) && string.IsNullOrWhiteSpace(legacyLine2))
        {
            return [];
        }

        return [(legacyLine1?.Trim() ?? string.Empty, legacyLine2?.Trim() ?? string.Empty)];
    }

    private static string FormatLine(string? type, string? value, string? spec)
    {
        var v = value?.Trim() ?? string.Empty;
        var s = spec?.Trim() ?? string.Empty;
        var isLine = type?.Equals("line", StringComparison.OrdinalIgnoreCase) == true;
        var isSpecial = type?.Equals("special", StringComparison.OrdinalIgnoreCase) == true;

        if (isLine && v.Length > 0 && s.Length > 0)
        {
            return $"{v} / {s}";
        }

        if (isLine && v.Length > 0)
        {
            return v;
        }

        if (isSpecial && v.Length > 0)
        {
            return v;
        }

        if (v.Length > 0 && s.Length > 0)
        {
            return $"{v} / {s}";
        }

        if (v.Length > 0)
        {
            return v;
        }

        return s;
    }

    private static string FormatGoalAt(IReadOnlyList<(string Line1, string Line2)> goals, int index)
    {
        if (index < 0 || index >= goals.Count)
        {
            return string.Empty;
        }

        return FormatGoal(goals[index]);
    }

    private static string FormatGoal((string? Line1, string? Line2) goal)
    {
        var a = goal.Line1?.Trim() ?? string.Empty;
        var b = goal.Line2?.Trim() ?? string.Empty;
        return a.Length > 0 && b.Length > 0
            ? $"{a} / {b}"
            : a.Length > 0
                ? a
                : b;
    }

    private static string BlankDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
