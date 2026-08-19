using System.Globalization;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Pdf;

/// <summary>Bildfahrplan als PDF (A3 quer) – Diagramm + Legende.</summary>
public static class BildfahrplanPdfGenerator
{
    private const string PageBackground = "#0A1628";
    private const string CardBackground = "#F7F9FC";
    private const string Muted = "#BBDEFB";
    private const string White = "#FFFFFF";
    private const string Accent = "#4FC3F7";

    static BildfahrplanPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public sealed record Model(
        string Corridor,
        string DirectionLabel,
        string TimeWindowLabel,
        string MetaLine,
        BildfahrplanChartModel Chart,
        IReadOnlyList<(string Label, string ColorHex, string Direction)> Legend);

    public static string BuildDefaultFileName(string? corridor)
    {
        var lc = string.IsNullOrWhiteSpace(corridor)
            ? "Linie"
            : Sanitize(corridor);
        return $"Bildfahrplan_{lc}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
    }

    public static void Generate(string outputPath, Model model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(model.Chart);

        var stamp = DateTime.Now;
        var chartPng = RenderChartPng(model.Chart, scale: 2.2f);

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A3.Landscape());
                page.Margin(18);
                page.PageColor(PageBackground);
                page.DefaultTextStyle(x =>
                    PlanerPdfTextStyles.ApplyLight(x)
                        .FontSize(10)
                        .FontColor(White)
                        .LineHeight(1.2f));

                page.Header().Element(h => ComposeHeader(h, model, stamp));
                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Item().Background(CardBackground).Border(1).BorderColor("#90A4AE").Padding(6)
                        .Image(chartPng)
                        .FitArea();

                    if (model.Legend.Count > 0)
                    {
                        col.Item().PaddingTop(10).Element(c => ComposeLegend(c, model));
                    }
                });
                page.Footer().PaddingTop(6).Row(footer =>
                {
                    footer.RelativeItem().AlignMiddle().Text(
                            $"Smart-ÖPNV · Bildfahrplan · {stamp:dd.MM.yyyy HH:mm}")
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
                col.Item().Text("Bildfahrplan").FontSize(18).SemiBold().FontColor(Accent);
                col.Item().PaddingTop(2).Text($"Linie/Kurs {model.Corridor} · {model.DirectionLabel}")
                    .FontSize(12).FontColor(White);
                col.Item().Text($"{model.TimeWindowLabel} · {model.MetaLine}")
                    .FontSize(9).FontColor(Muted);
                col.Item().Text($"Erstellt {stamp:dd.MM.yyyy HH:mm}")
                    .FontSize(8).FontColor(Muted);
            });
        });
    }

    private static void ComposeLegend(IContainer container, Model model)
    {
        container.Column(col =>
        {
            col.Item().Text($"Fahrten ({model.Legend.Count})")
                .FontSize(11).SemiBold().FontColor(Accent);

            const int perRow = 6;
            for (var i = 0; i < model.Legend.Count; i += perRow)
            {
                var slice = model.Legend.Skip(i).Take(perRow).ToList();
                col.Item().PaddingTop(4).Row(row =>
                {
                    foreach (var item in slice)
                    {
                        row.RelativeItem().Row(cell =>
                        {
                            cell.ConstantItem(10).Height(10).Background(ParseQuestColor(item.ColorHex));
                            cell.ConstantItem(4);
                            cell.RelativeItem().AlignMiddle()
                                .Text($"{item.Label} · {item.Direction}")
                                .FontSize(8)
                                .FontColor(White);
                        });
                    }

                    for (var pad = slice.Count; pad < perRow; pad++)
                    {
                        row.RelativeItem();
                    }
                });
            }
        });
    }

    private static byte[] RenderChartPng(BildfahrplanChartModel chart, float scale)
    {
        const float leftPad = 150f;
        const float rightPad = 28f;
        const float topPad = 20f;
        const float bottomPad = 40f;
        const float basePixelsPerHour = 96f;

        var windowMinutes = Math.Max(60, chart.WindowEndMinutes - chart.WindowStartMinutes);
        var plotW = Math.Max(500f, windowMinutes / 60f * basePixelsPerHour);
        var plotH = Math.Max(480f, chart.AxisStops.Count * 26f);
        var width = (int)Math.Ceiling((leftPad + plotW + rightPad) * scale);
        var height = (int)Math.Ceiling((topPad + plotH + bottomPad) * scale);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);

        var totalMeters = Math.Max(1, chart.TotalMeters);

        float X(int minutes) =>
            leftPad + (minutes - chart.WindowStartMinutes) / (float)windowMinutes * plotW;

        float Y(double meters) =>
            topPad + (1f - (float)(meters / totalMeters)) * plotH;

        using var gridHour = new SKPaint
        {
            Color = new SKColor(220, 225, 232),
            StrokeWidth = 1,
            IsAntialias = true
        };
        using var gridTen = new SKPaint
        {
            Color = new SKColor(236, 240, 244),
            StrokeWidth = 1,
            IsAntialias = true
        };
        using var stopLine = new SKPaint
        {
            Color = new SKColor(210, 216, 224),
            StrokeWidth = 1,
            IsAntialias = true
        };
        using var framePaint = new SKPaint
        {
            Color = new SKColor(160, 170, 185),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        using var textPaint = new SKPaint
        {
            Color = new SKColor(30, 40, 55),
            TextSize = 11,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        };
        using var kmPaint = new SKPaint
        {
            Color = new SKColor(120, 130, 140),
            TextSize = 9,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI")
        };
        using var timePaint = new SKPaint
        {
            Color = new SKColor(90, 100, 110),
            TextSize = 11,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI")
        };

        for (var m = chart.WindowStartMinutes; m <= chart.WindowEndMinutes; m += 10)
        {
            var x = X(m);
            canvas.DrawLine(x, topPad, x, topPad + plotH, m % 60 == 0 ? gridHour : gridTen);
            if (m % 60 == 0)
            {
                var label = $"{m / 60:00}:00";
                canvas.DrawText(label, x - 14, topPad + plotH + 18, timePaint);
            }
        }

        foreach (var stop in chart.AxisStops)
        {
            var y = Y(stop.DistanceMeters);
            canvas.DrawLine(leftPad, y, leftPad + plotW, y, stopLine);
            var name = stop.Name ?? "";
            if (name.Length > 28)
            {
                name = name[..27] + "…";
            }

            canvas.DrawText(name, 4, y - 2, textPaint);
            var km = (stop.DistanceMeters / 1000.0).ToString("0.##", CultureInfo.GetCultureInfo("de-DE"));
            canvas.DrawText(km, 4, y + 12, kmPaint);
        }

        canvas.DrawRect(leftPad, topPad, plotW, plotH, framePaint);

        foreach (var trip in chart.Trips)
        {
            if (trip.Points.Count < 2)
            {
                continue;
            }

            using var tripPaint = new SKPaint
            {
                Color = ParseSkColor(trip.ColorHex),
                StrokeWidth = 2.6f,
                Style = SKPaintStyle.Stroke,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };

            // Keine Segmente rückwärts in der Zeit (Mitternacht / Wendefahrt sonst quer durchs Blatt)
            foreach (var run in SplitForwardTimeRuns(trip.Points))
            {
                if (run.Count < 2)
                {
                    continue;
                }

                using var path = new SKPath();
                path.MoveTo(X(run[0].TimeMinutes), Y(run[0].DistanceMeters));
                for (var i = 1; i < run.Count; i++)
                {
                    path.LineTo(X(run[i].TimeMinutes), Y(run[i].DistanceMeters));
                }

                canvas.DrawPath(path, tripPaint);
            }

            var mid = trip.Points[trip.Points.Count / 2];
            using var labelBg = new SKPaint
            {
                Color = new SKColor(250, 251, 252, 220),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            using var labelPaint = new SKPaint
            {
                Color = ParseSkColor(trip.ColorHex),
                TextSize = 10,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
            };
            var lx = X(mid.TimeMinutes) + 4;
            var ly = Y(mid.DistanceMeters) - 4;
            var tw = labelPaint.MeasureText(trip.Label);
            canvas.DrawRect(lx - 2, ly - 11, tw + 4, 14, labelBg);
            canvas.DrawText(trip.Label, lx, ly, labelPaint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        return data.ToArray();
    }

    private static SKColor ParseSkColor(string? hex)
    {
        try
        {
            var h = (hex ?? "#2196f3").Trim();
            if (!h.StartsWith('#'))
            {
                h = "#" + h;
            }

            return SKColor.Parse(h);
        }
        catch
        {
            return new SKColor(33, 150, 243);
        }
    }

    /// <summary>
    /// Teilt eine Fahrt in zeitlich vorwärts laufende Stücke.
    /// Rückwärts-Segmente (rechts→links, z. B. über Mitternacht) werden nicht gezeichnet.
    /// </summary>
    private static List<List<BildfahrplanPoint>> SplitForwardTimeRuns(IReadOnlyList<BildfahrplanPoint> points)
    {
        var runs = new List<List<BildfahrplanPoint>>();
        List<BildfahrplanPoint>? current = null;
        BildfahrplanPoint? prev = null;
        foreach (var p in points)
        {
            if (prev is null || p.TimeMinutes >= prev.TimeMinutes)
            {
                current ??= [];
                current.Add(p);
            }
            else
            {
                if (current is { Count: > 0 })
                {
                    runs.Add(current);
                }

                current = [p];
            }

            prev = p;
        }

        if (current is { Count: > 0 })
        {
            runs.Add(current);
        }

        return runs;
    }

    private static string ParseQuestColor(string? hex)
    {
        var h = (hex ?? "#2196f3").Trim();
        if (!h.StartsWith('#'))
        {
            h = "#" + h;
        }

        return h;
    }

    private static string Sanitize(string value)
    {
        var chars = value.Trim().Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }
}
