using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var mdPath = args.Length > 1
    ? args[1]
    : Path.Combine(repo, "docs", "manual", "SmartOepnv_Appbeschreibung.md");
var outPath = args.Length > 0
    ? args[0]
    : Path.Combine(repo, "docs", "manual", "SmartOepnv_Appbeschreibung.pdf");

if (!File.Exists(mdPath))
{
    Console.Error.WriteLine("Markdown fehlt: " + mdPath);
    return 1;
}

var baseDir = Path.GetDirectoryName(mdPath)!;
var blocks = Parse(File.ReadAllLines(mdPath));
var created = DateTime.Now;

Document.Create(document =>
{
    document.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(32);
        page.MarginVertical(28);
        page.DefaultTextStyle(x => x.FontSize(10.5f).FontColor(Colors.Grey.Darken3).LineHeight(1.3f));

        page.Header().Column(h =>
        {
            h.Item().Text("Smart ÖPNV · GPS Ansagen").FontSize(9).SemiBold().FontColor(Colors.Blue.Darken2);
            h.Item().Text("Bedienung der Tablet-App").FontSize(14).SemiBold();
            h.Item().PaddingTop(3).LineHorizontal(1.2f).LineColor(Colors.Blue.Darken2);
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.Span($"Stand {created:dd.MM.yyyy}  ·  ").FontSize(8).FontColor(Colors.Grey.Medium);
            t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
            t.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
            t.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
        });

        page.Content().PaddingTop(8).Column(col =>
        {
            col.Spacing(6);
            foreach (var block in blocks)
                Render(col, block, baseDir);
        });
    });
}).GeneratePdf(outPath);

Console.WriteLine(outPath);
return 0;

static void Render(ColumnDescriptor col, Block block, string baseDir)
{
    switch (block)
    {
        case TitleBlock title:
            col.Item().PaddingBottom(2).Text(title.Text).FontSize(18).SemiBold().FontColor(Colors.Blue.Darken3);
            break;
        case HeadingBlock h:
            col.Item().PaddingTop(8).Text(h.Text).FontSize(h.Level == 2 ? 13 : 12).SemiBold().FontColor(Colors.Blue.Darken2);
            break;
        case ParagraphBlock p:
            col.Item().Text(t => Rich(t, p.Text));
            break;
        case BulletBlock b:
            col.Item().PaddingLeft(8).Row(row =>
            {
                row.ConstantItem(12).Text("•").FontColor(Colors.Blue.Darken2);
                row.RelativeItem().Text(t => Rich(t, b.Text));
            });
            break;
        case NumberedBlock n:
            col.Item().PaddingLeft(8).Row(row =>
            {
                row.ConstantItem(18).Text(n.Number + ".").SemiBold().FontColor(Colors.Blue.Darken2);
                row.RelativeItem().Text(t => Rich(t, n.Text));
            });
            break;
        case ImageBlock image:
            var path = Path.GetFullPath(Path.Combine(baseDir, image.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(path))
            {
                col.Item().Text("Bild fehlt: " + image.RelativePath).Italic().FontColor(Colors.Red.Darken1);
                break;
            }
            col.Item().ShowEntire().Column(img =>
            {
                img.Spacing(2);
                img.Item().Border(0.6f).BorderColor(Colors.Grey.Lighten1).Image(path).FitWidth();
                if (!string.IsNullOrWhiteSpace(image.Alt))
                    img.Item().Text(image.Alt).FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
            });
            break;
        case TableBlock table:
            col.Item().Table(q =>
            {
                q.ColumnsDefinition(c =>
                {
                    for (var i = 0; i < table.Columns; i++)
                        c.RelativeColumn(i == 0 ? 1.1f : 1.6f);
                });
                for (var r = 0; r < table.Rows.Count; r++)
                {
                    var header = r == 0;
                    foreach (var cell in table.Rows[r])
                    {
                        q.Cell().Background(header ? Colors.Blue.Darken2 : (r % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White))
                            .BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2)
                            .Padding(4)
                            .Text(t =>
                            {
                                t.DefaultTextStyle(s => header
                                    ? s.FontSize(8.5f).FontColor(Colors.White).SemiBold()
                                    : s.FontSize(8.5f).FontColor(Colors.Grey.Darken3));
                                Rich(t, cell);
                            });
                    }
                }
            });
            break;
    }
}

static void Rich(TextDescriptor text, string value)
{
    foreach (Match match in Regex.Matches(value, @"\*\*(.+?)\*\*|`([^`]+)`|([^*`]+)"))
    {
        if (match.Groups[1].Success)
            text.Span(match.Groups[1].Value).SemiBold();
        else if (match.Groups[2].Success)
            text.Span(match.Groups[2].Value).FontFamily(Fonts.CourierNew);
        else if (match.Groups[3].Success && match.Groups[3].Value.Length > 0)
            text.Span(match.Groups[3].Value);
    }
}

static List<Block> Parse(string[] lines)
{
    var blocks = new List<Block>();
    var i = 0;
    while (i < lines.Length)
    {
        var line = lines[i];
        if (string.IsNullOrWhiteSpace(line))
        {
            i++;
            continue;
        }

        if (line.StartsWith("# "))
        {
            blocks.Add(new TitleBlock(line[2..].Trim()));
            i++;
            continue;
        }

        if (line.StartsWith("## "))
        {
            blocks.Add(new HeadingBlock(2, line[3..].Trim()));
            i++;
            continue;
        }

        if (line.StartsWith("### "))
        {
            blocks.Add(new HeadingBlock(3, line[4..].Trim()));
            i++;
            continue;
        }

        var image = Regex.Match(line.Trim(), @"^!\[([^\]]*)\]\(([^)]+)\)$");
        if (image.Success)
        {
            blocks.Add(new ImageBlock(image.Groups[1].Value, image.Groups[2].Value));
            i++;
            continue;
        }

        if (line.StartsWith("|"))
        {
            var rows = new List<string[]>();
            while (i < lines.Length && lines[i].StartsWith("|"))
            {
                var raw = lines[i].Trim();
                if (!Regex.IsMatch(raw, @"^\|?\s*:?-{3,}"))
                {
                    var cells = raw.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    if (!cells.All(c => Regex.IsMatch(c, @"^:?-{3,}:?$")))
                        rows.Add(cells);
                }
                i++;
            }
            if (rows.Count > 0)
            {
                var cols = rows.Max(r => r.Length);
                blocks.Add(new TableBlock(rows.Select(r =>
                {
                    var padded = new string[cols];
                    for (var c = 0; c < cols; c++)
                        padded[c] = c < r.Length ? r[c] : "";
                    return padded;
                }).ToList(), cols));
            }
            continue;
        }

        if (line.StartsWith("- "))
        {
            blocks.Add(new BulletBlock(line[2..].Trim()));
            i++;
            continue;
        }

        var numbered = Regex.Match(line, @"^(\d+)\.\s+(.*)$");
        if (numbered.Success)
        {
            blocks.Add(new NumberedBlock(int.Parse(numbered.Groups[1].Value), numbered.Groups[2].Value.Trim()));
            i++;
            continue;
        }

        var paragraph = line.Trim();
        i++;
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines[i]))
        {
            paragraph += " " + lines[i].Trim();
            i++;
        }
        blocks.Add(new ParagraphBlock(paragraph));
    }
    return blocks;
}

static bool StartsBlock(string line) =>
    line.StartsWith("#") || line.StartsWith("|") || line.StartsWith("- ") ||
    Regex.IsMatch(line, @"^\d+\.\s+") ||
    Regex.IsMatch(line.Trim(), @"^!\[");

abstract record Block;
record TitleBlock(string Text) : Block;
record HeadingBlock(int Level, string Text) : Block;
record ParagraphBlock(string Text) : Block;
record BulletBlock(string Text) : Block;
record NumberedBlock(int Number, string Text) : Block;
record ImageBlock(string Alt, string RelativePath) : Block;
record TableBlock(List<string[]> Rows, int Columns) : Block;
