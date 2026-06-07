using System.Globalization;
using SkiaSharp;
using System.IO;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.Core.Sev;

namespace SmartOepnv.AppShared.Sev;

/// <summary>NRW-SEV-Schild A3 quer – Maße aus offiziellen Vorlagen (RE10/RE13/S8).</summary>
public static class SevSignPdfGenerator
{
    private const string FontFamily = "Bahnschrift";
    private const string ExpressBusFontFamily = "Bahnschrift SemiLight";
    /// <summary>NRW-Vorlage: schmale Serifenlose (nicht Bahnschrift SemiLight).</summary>
    private const string DisclaimerFontFamily = "Arial Narrow";
    private const float DisclaimerFontSize = 12f;
    private const float DisclaimerLineHeight = 0.92f;
    private const float DisclaimerLetterSpacing = -0.02f;
    private const Unit Mm = Unit.Millimetre;

    private const float DestinationFontSizeDefault = 100f;
    private const float DestinationFontSizeCompact = 80f;
    private const int DestinationFontSizeCompactFromLength = 14;
    private const float ExpressBusFontSize = 80f;
    private const float StopLabelFontSize = 22f;

    private static readonly string HeaderBlue = "#001F5B";
    private static readonly string ExpressBusMagenta = "#9c1c62";
    private static readonly string StopMagenta = "#8B0045";
    private static readonly string BorderRed = "#CC0000";
    private static readonly string RouteBlue = "#001F5B";

    // Original RE13 Venlo (PDF-Analyse)
    private const float BorderInsetMm = 4.8f;
    private const float BorderStrokePt = 9f;
    private const float PageWidthMm = 420f;
    private const float PageHeightMm = 297f;
    private const float ContentInsetHorizontalMm = 11.4f;
    private const float ContentRightMm = 408.6f;
    private const float ContentAreaRightPaddingMm = PageWidthMm - ContentRightMm;
    private const float HeaderBarWidthMm = ContentRightMm - ContentInsetHorizontalMm;
    private const float HeaderBarTopMm = 12.7f;
    private const float HeaderHeightMm = 52.7f;
    private const float FooterHeightMm = 52.2f;

    private const float ChevronPaddingLeftMm = 8.4f;
    private const float DestinationLineHeightFactor = 0.92f;
    /// <summary>SVG-Pfadrahmen (viewBox 122 × 166).</summary>
    private const float ChevronSvgAspectWidthOverHeight = 122f / 166f;
    private const float ChevronLeftMm = ContentInsetHorizontalMm + ChevronPaddingLeftMm;
    private const string DestinationChevronAsset = "destination_chevron.svg";
    private const string DestinationChevronPngFallback = "destination_chevron.png";
    private const float ChevronArmThicknessMm = 12f;
    private const float HeaderSevSubtitleScaleX = 0.87f;
    private const float DestinationBandHeightDefaultMm = 75f;
    private const float DestinationBandHeightTwoLinesMm = 88f;
    private const float DestinationBandHeightThreeLinesMm = 98f;
    /// <summary>SEV-Piktogramm (Zug→Bus): quadratisch, RE13-Vorlage.</summary>
    private const float SevIconSizeMm = 82f;
    private const float SevIconGapBelowBlueHeaderMm = 4f;
    private const float SevIconTopMm = HeaderHeightMm + SevIconGapBelowBlueHeaderMm;
    private const float SevIconLeftMm = ContentRightMm - SevIconSizeMm;

    private const float RouteLineThicknessMm = 5.4f;
    private const string PerlschnurStopAsset = "perlschnur_stop.png";
    /// <summary>Referenz-Oval 78×139 px.</summary>
    private const float StopCapsuleWidthMm = 6.5f;
    private const float StopCapsuleHeightMm = 11.6f;
    private static float StopCapsuleHalfWidthMm => StopCapsuleWidthMm / 2f;
    private static float StopCapsuleHalfHeightMm => StopCapsuleHeightMm / 2f;
    private const float StopCapsuleStrokeMm = 0.85f;
    private const float StopLabelGapAboveCapsuleMm = 2f;
    private const float StopLabelOffsetRightMm = 1.8f;
    /// <summary>Perlschnur links bündig mit Footer-Trennlinie (relativ zum Inhaltsbereich).</summary>
    private const float RouteLineLeftMm = 0f;
    private const float RouteDiagramBandHeightMm = 42f;
    private const float PerlschnurGapAboveFooterLineMm = 3f;

    // Footer (RE13/S28 Original-PDF)
    private const float FooterLineTopMm = 242.5f;
    private const float RouteDiagramTopMm = FooterLineTopMm - RouteDiagramBandHeightMm - PerlschnurGapAboveFooterLineMm;
    private const float RouteDiagramWidthMm = RouteLineRightMm - ContentInsetHorizontalMm;
    private const float FooterLineHeightMm = 1.4f;
    private const float FooterLineLeftMm = 11.4f;
    private const float FooterCommissionTopMm = 245f;
    private const float FooterLogoRowLeftMm = 11.7f;
    private const float FooterLogoRowTopMm = 252.4f;
    private const float FooterLogoRowHeightMm = 30.6f;
    private const float FooterLogoSlotGapMm = 2f;
    private const float VrrLogoWidthMm = 62.5f;
    private const float VrrLogoHeightMm = 30.6f;
    private const float ZuginfoServiceLeftMm = 213.1f;
    private const float ZuginfoServiceTopMm = 248.4f;
    private const float ZuginfoServiceWidthMm = 85.2f;
    private const float ZuginfoServiceHeightMm = 29.9f;
    /// <summary>Perlschnur endet hinter dem Zuginfo-QR, nicht über Logo/Kontakt.</summary>
    private const float RouteLineRightMm = ZuginfoServiceLeftMm + ZuginfoServiceWidthMm;
    private const float FooterLineWidthMm = ContentRightMm - FooterLineLeftMm;
    private const float ZuginfoLogoLeftMm = 299.3f;
    private const float ZuginfoLogoTopMm = 249.7f;
    private const float ZuginfoLogoWidthMm = 54.8f;
    private const float ZuginfoLogoHeightMm = 28.2f;
    private const float ContactQrLeftMm = 356.3f;
    private const float ContactPhoneQrTopMm = 254.9f;
    private const float ContactWebQrTopMm = 274.4f;
    private const float ContactQrSizeMm = 10f;
    private const float ContactTextLeftMm = 367.4f;
    private const float ContactPhoneTextTopMm = 256.3f;
    private const float ContactWebTextTopMm = 274.1f;
    private const float DisclaimerTopMm = 279.0f;
    private const float DisclaimerLeftMm = 213.1f;
    private const float DisclaimerWidthMm = ZuginfoLogoLeftMm + ZuginfoLogoWidthMm - ZuginfoServiceLeftMm;

    static SevSignPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public static void Generate(SevSignData data, string outputPath, string? assetsDirectory = null)
    {
        var assets = assetsDirectory ?? SevAssetPaths.RootDirectory;
        var selectedOperators = SevOperatorCatalog.GetMany(data.Operators);
        var destinationBandHeightMm = ResolveDestinationBandHeightMm(data.DestinationLayout);
        var (chevronHeightMm, chevronWidthMm) = ResolveChevronSizeMm(data.DestinationLayout);
        var middleHeightMm = PageHeightMm - HeaderHeightMm - FooterHeightMm - destinationBandHeightMm;
        var chevronTopMm = HeaderHeightMm + (destinationBandHeightMm - chevronHeightMm) / 2f;

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A3.Landscape());
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontColor(Colors.Black));
                page.Foreground().Element(DrawDashedBorder);
                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Column(column =>
                    {
                        column.Item().Height(HeaderHeightMm, Mm);
                        column.Item().Height(middleHeightMm, Mm)
                            .Element(c => DrawMiddle(c, data, assets, destinationBandHeightMm, chevronWidthMm));
                        column.Item().Height(FooterHeightMm, Mm)
                            .Element(DrawFooter);
                    });

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(HeaderBarTopMm, Mm)
                        .PaddingLeft(ContentInsetHorizontalMm, Mm)
                        .Width(HeaderBarWidthMm, Mm)
                        .Height(HeaderHeightMm - HeaderBarTopMm, Mm)
                        .Element(c => DrawHeaderBar(c, data));

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(chevronTopMm, Mm)
                        .PaddingLeft(ChevronLeftMm, Mm)
                        .Width(chevronWidthMm, Mm)
                        .Height(chevronHeightMm, Mm)
                        .Element(c => DrawChevron(c, assets));

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(SevIconTopMm, Mm)
                        .PaddingLeft(SevIconLeftMm, Mm)
                        .Width(SevIconSizeMm, Mm)
                        .Height(SevIconSizeMm, Mm)
                        .Element(c => DrawSevIcon(c, assets));

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(RouteDiagramTopMm, Mm)
                        .PaddingLeft(ContentInsetHorizontalMm, Mm)
                        .Width(RouteDiagramWidthMm, Mm)
                        .Height(RouteDiagramBandHeightMm, Mm)
                        .Element(c => DrawRouteDiagram(c, data, assets));

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(FooterLineTopMm, Mm)
                        .PaddingLeft(FooterLineLeftMm, Mm)
                        .Width(FooterLineWidthMm, Mm)
                        .Height(FooterLineHeightMm, Mm)
                        .Background(HeaderBlue);

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(FooterCommissionTopMm, Mm)
                        .PaddingLeft(ContentInsetHorizontalMm + 0.3f, Mm)
                        .Text("Im Auftrag von:")
                        .FontSize(14)
                        .FontColor(HeaderBlue);

                    DrawOperatorLogoLayers(layers, selectedOperators, assets);

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(ZuginfoServiceTopMm, Mm)
                        .PaddingLeft(ZuginfoServiceLeftMm, Mm)
                        .Width(ZuginfoServiceWidthMm, Mm)
                        .Height(ZuginfoServiceHeightMm, Mm)
                        .Element(c => DrawImageIfExists(c, assets, "footer_zuginfo_service.jpeg", ZuginfoServiceWidthMm, ZuginfoServiceHeightMm));

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(ZuginfoLogoTopMm, Mm)
                        .PaddingLeft(ZuginfoLogoLeftMm, Mm)
                        .Width(ZuginfoLogoWidthMm, Mm)
                        .Height(ZuginfoLogoHeightMm, Mm)
                        .Element(c => DrawImageIfExists(c, assets, "footer_zuginfo_logo.png", ZuginfoLogoWidthMm, ZuginfoLogoHeightMm));

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(DisclaimerTopMm, Mm)
                        .PaddingLeft(DisclaimerLeftMm, Mm)
                        .Width(DisclaimerWidthMm, Mm)
                        .Element(DrawDisclaimer);

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(ContactPhoneQrTopMm, Mm)
                        .PaddingLeft(ContactQrLeftMm, Mm)
                        .Width(ContactQrSizeMm, Mm)
                        .Height(ContactQrSizeMm, Mm)
                        .Element(c => DrawImageIfExists(c, assets, "qr_phone.png", ContactQrSizeMm, ContactQrSizeMm));

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(ContactWebQrTopMm, Mm)
                        .PaddingLeft(ContactQrLeftMm, Mm)
                        .Width(ContactQrSizeMm, Mm)
                        .Height(ContactQrSizeMm, Mm)
                        .Element(c => DrawImageIfExists(c, assets, "qr_web.png", ContactQrSizeMm, ContactQrSizeMm));

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(ContactPhoneTextTopMm, Mm)
                        .PaddingLeft(ContactTextLeftMm, Mm)
                        .Column(phone =>
                        {
                            phone.Item().Text("0202 515 62 515").FontSize(14).Bold().FontColor(HeaderBlue);
                            phone.Item().Text("(Ortstarif)").FontSize(14).FontColor(HeaderBlue);
                        });

                    layers.Layer()
                        .Unconstrained()
                        .AlignTop()
                        .AlignLeft()
                        .PaddingTop(ContactWebTextTopMm, Mm)
                        .PaddingLeft(ContactTextLeftMm - 2.8f, Mm)
                        .Text("www.zuginfo.nrw")
                        .FontSize(14)
                        .Bold()
                        .FontColor(HeaderBlue);
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static void DrawChevron(IContainer container, string assetsDirectory)
    {
        var svgPath = Path.Combine(assetsDirectory, DestinationChevronAsset);
        if (File.Exists(svgPath))
        {
            var svg = File.ReadAllText(svgPath)
                .Replace("#29235c", HeaderBlue, StringComparison.OrdinalIgnoreCase);
            container.Svg(svg).FitArea();
            return;
        }

        var chevronImage = TryLoadRasterPngBytes(assetsDirectory, DestinationChevronPngFallback, removeDarkBackground: true);
        container.Svg(size =>
        {
            using var stream = new MemoryStream();
            using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
            {
                if (chevronImage is not null)
                {
                    using var bitmap = SKBitmap.Decode(chevronImage);
                    if (bitmap is not null)
                    {
                        using var paint = new SKPaint
                        {
                            IsAntialias = true,
                            FilterQuality = SKFilterQuality.High,
                        };
                        canvas.DrawBitmap(bitmap, new SKRect(0, 0, size.Width, size.Height), paint);
                    }
                    else
                    {
                        DrawChevronVector(canvas, size.Width, size.Height);
                    }
                }
                else
                {
                    DrawChevronVector(canvas, size.Width, size.Height);
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        });
    }

    private static byte[]? TryLoadRasterPngBytes(string assetsDirectory, string fileName, bool removeDarkBackground)
    {
        var path = Path.Combine(assetsDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(path);
        if (decoded is null)
        {
            return null;
        }

        using var bitmap = decoded.ColorType == SKColorType.Rgba8888
            ? decoded.Copy()
            : decoded.Copy(SKColorType.Rgba8888);
        if (bitmap is null)
        {
            return null;
        }

        if (removeDarkBackground)
        {
            MakeNearBlackTransparent(bitmap);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }

    /// <summary>Schwarzer PNG-Hintergrund (Referenz-Assets) → transparent, farbige Pfeilflächen bleiben.</summary>
    private static void MakeNearBlackTransparent(SKBitmap bitmap)
    {
        const byte channelThreshold = 52;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!IsNearBlackBackgroundPixel(bitmap.GetPixel(x, y), channelThreshold))
                {
                    continue;
                }

                bitmap.SetPixel(x, y, SKColors.Transparent);
            }
        }
    }

    private static bool IsNearBlackBackgroundPixel(SKColor color, byte channelThreshold)
    {
        if (color.Alpha < 8)
        {
            return true;
        }

        if (color.Red > channelThreshold || color.Green > channelThreshold || color.Blue > channelThreshold)
        {
            return false;
        }

        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        return max - min <= 12;
    }

    /// <summary>Fallback, falls Referenz-PNG fehlt.</summary>
    private static void DrawChevronVector(SKCanvas canvas, float width, float height)
    {
        using var fill = new SKPaint
        {
            Color = SKColor.Parse(HeaderBlue),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        var halfT = MmToPt(ChevronArmThicknessMm, Mm) / 2f;
        var margin = MmToPt(1f, Mm);
        const float cos45 = 0.70710678f;
        const float sin45 = 0.70710678f;

        var tipX = width - margin - halfT;
        var tipY = height / 2f;
        var armLen = Math.Min(tipX - margin - halfT, tipY - margin - halfT);

        var anchorX = tipX - armLen * cos45;
        var topAy = tipY - armLen * sin45;
        var botAy = tipY + armLen * sin45;

        using var path = new SKPath();
        path.MoveTo(anchorX - halfT * sin45, topAy + halfT * cos45);
        path.LineTo(anchorX + halfT * sin45, topAy - halfT * cos45);
        path.LineTo(tipX, tipY);
        path.LineTo(anchorX - halfT * sin45, botAy - halfT * cos45);
        path.LineTo(anchorX + halfT * sin45, botAy + halfT * cos45);
        path.Close();
        canvas.DrawPath(path, fill);
    }

    private static void DrawHeaderBar(IContainer container, SevSignData data)
    {
        container
            .Background(HeaderBlue)
            .PaddingHorizontal(8, Mm)
            .PaddingTop(3, Mm)
            .PaddingBottom(2, Mm)
            .Row(row =>
            {
                row.AutoItem().AlignMiddle()
                    .Text(data.FormattedLine)
                    .FontSize(90)
                    .LineHeight(0.88f)
                    .Bold()
                    .FontColor(Colors.White);

                row.RelativeItem()
                    .AlignRight()
                    .AlignMiddle()
                    .PaddingRight(2, Mm)
                    .ScaleHorizontal(HeaderSevSubtitleScaleX)
                    .AlignRight()
                    .AlignMiddle()
                    .Text("Ersatzverkehr mit Bussen (SEV)")
                    .FontSize(60)
                    .LineHeight(0.92f)
                    .LetterSpacing(-0.04f)
                    .FontColor(Colors.White);
            });
    }

    private static (float HeightMm, float WidthMm) ResolveChevronSizeMm(SevDestinationLayout layout)
    {
        var totalLineHeightPt = 0f;
        if (layout.HasExpressBus)
        {
            totalLineHeightPt += ExpressBusFontSize * DestinationLineHeightFactor;
        }

        if (!string.IsNullOrWhiteSpace(layout.PrimaryLine))
        {
            totalLineHeightPt += ResolveDestinationLineFontSize(layout.PrimaryLine) * DestinationLineHeightFactor;
        }

        if (!string.IsNullOrWhiteSpace(layout.SecondaryLine))
        {
            totalLineHeightPt += ResolveDestinationLineFontSize(layout.SecondaryLine) * DestinationLineHeightFactor;
        }

        if (totalLineHeightPt <= 0f)
        {
            totalLineHeightPt = DestinationFontSizeDefault * DestinationLineHeightFactor;
        }

        var heightMm = totalLineHeightPt * 25.4f / 72f;
        var widthMm = heightMm * ChevronSvgAspectWidthOverHeight;
        return (heightMm, widthMm);
    }

    private static float ResolveDestinationLineFontSize(string line) =>
        line.Trim().Length >= DestinationFontSizeCompactFromLength
            ? DestinationFontSizeCompact
            : DestinationFontSizeDefault;

    private static float ResolveDestinationBandHeightMm(SevDestinationLayout layout)
    {
        var lineCount = 0;
        if (layout.HasExpressBus)
        {
            lineCount++;
        }

        if (!string.IsNullOrWhiteSpace(layout.PrimaryLine))
        {
            lineCount++;
        }

        if (!string.IsNullOrWhiteSpace(layout.SecondaryLine))
        {
            lineCount++;
        }

        return lineCount switch
        {
            >= 3 => DestinationBandHeightThreeLinesMm,
            2 => DestinationBandHeightTwoLinesMm,
            _ => DestinationBandHeightDefaultMm,
        };
    }

    private static void DrawMiddle(
        IContainer container,
        SevSignData data,
        string assetsDirectory,
        float destinationBandHeightMm,
        float chevronWidthMm)
    {
        container
            .PaddingLeft(ContentInsetHorizontalMm, Mm)
            .PaddingRight(ContentAreaRightPaddingMm, Mm)
            .Column(column =>
        {
            column.Item().Height(destinationBandHeightMm, Mm).Row(row =>
            {
                row.ConstantItem(chevronWidthMm, Mm);

                row.RelativeItem().AlignCenter().AlignMiddle().Column(dest =>
                {
                    var layout = data.DestinationLayout;
                    if (layout.HasExpressBus)
                    {
                        dest.Item().AlignCenter()
                            .Text(layout.ExpressBusLine)
                            .FontFamily(ExpressBusFontFamily)
                            .FontSize(ExpressBusFontSize)
                            .LineHeight(0.92f)
                            .FontColor(ExpressBusMagenta);
                    }

                    if (!string.IsNullOrWhiteSpace(layout.PrimaryLine))
                    {
                        dest.Item().AlignCenter()
                            .Text(layout.PrimaryLine)
                            .FontFamily(FontFamily)
                            .FontSize(ResolveDestinationLineFontSize(layout.PrimaryLine))
                            .LineHeight(0.92f)
                            .Bold()
                            .FontColor(HeaderBlue);
                    }

                    if (!string.IsNullOrWhiteSpace(layout.SecondaryLine))
                    {
                        dest.Item().AlignCenter()
                            .Text(layout.SecondaryLine)
                            .FontFamily(FontFamily)
                            .FontSize(ResolveDestinationLineFontSize(layout.SecondaryLine))
                            .LineHeight(0.92f)
                            .Bold()
                            .FontColor(HeaderBlue);
                    }
                });

                row.ConstantItem(SevIconSizeMm, Mm);
            });
        });
    }

    private static void DrawRouteDiagram(IContainer container, SevSignData data, string assetsDirectory)
    {
        var stops = data.Stops
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (stops.Count == 0)
        {
            container.AlignCenter().AlignMiddle()
                .Text("Haltestellen über „+“ hinzufügen")
                .FontSize(18)
                .FontColor(Colors.Grey.Medium);
            return;
        }

        var stopImageBytes = TryLoadRasterPngBytes(assetsDirectory, PerlschnurStopAsset, removeDarkBackground: true);

        container.Svg(size =>
        {
            using var stream = new MemoryStream();
            using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
            {
                using var stopBitmap = stopImageBytes is not null ? SKBitmap.Decode(stopImageBytes) : null;

                var width = size.Width;
                var height = size.Height;
                var lineLeft = MmToPt(RouteLineLeftMm, Mm);
                var lineRight = MmToPt(RouteLineRightMm - ContentInsetHorizontalMm, Mm);
                var capsuleHalfH = MmToPt(StopCapsuleHalfHeightMm, Mm);
                var lineY = height - MmToPt(2f, Mm) - capsuleHalfH;
                var lineThickness = MmToPt(RouteLineThicknessMm, Mm);
                var capsuleHalfW = MmToPt(StopCapsuleHalfWidthMm, Mm);
                var capsuleW = MmToPt(StopCapsuleWidthMm, Mm);
                var capsuleH = MmToPt(StopCapsuleHeightMm, Mm);
                var capsuleStroke = MmToPt(StopCapsuleStrokeMm, Mm);
                var labelGapAbove = MmToPt(StopLabelGapAboveCapsuleMm, Mm);
                var labelOffsetRight = MmToPt(StopLabelOffsetRightMm, Mm);
                var useStopAsset = stopBitmap is not null;

                using var routePaint = new SKPaint
                {
                    Color = SKColor.Parse(RouteBlue),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                };

                var firstStopX = lineLeft + capsuleHalfW;
                var lastStopX = lineRight - capsuleHalfW;

                var stopX = new float[stops.Count];
                for (var i = 0; i < stops.Count; i++)
                {
                    var t = stops.Count == 1 ? 0.5f : i / (float)(stops.Count - 1);
                    stopX[i] = firstStopX + t * (lastStopX - firstStopX);
                }

                DrawRouteSegment(canvas, routePaint, lineLeft, lineRight, lineY, lineThickness);

                using var stopStrokePaint = new SKPaint
                {
                    Color = SKColor.Parse(StopMagenta),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = capsuleStroke,
                    IsAntialias = true,
                };
                using var stopFillPaint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                };
                using var bitmapPaint = new SKPaint
                {
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.High,
                };

                var labelTypeface = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Bold)
                    ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);

                for (var i = 0; i < stops.Count; i++)
                {
                    var x = stopX[i];
                    var ovalX = x - capsuleHalfW;
                    var ovalY = lineY - capsuleHalfH;
                    var labelX = x + labelOffsetRight;
                    var labelY = ovalY - labelGapAbove;
                    var ovalRect = new SKRect(ovalX, ovalY, ovalX + capsuleW, ovalY + capsuleH);

                    if (useStopAsset)
                    {
                        canvas.DrawBitmap(stopBitmap, ovalRect, bitmapPaint);
                    }
                    else
                    {
                        canvas.DrawRoundRect(ovalRect, capsuleHalfW, capsuleHalfW, stopFillPaint);
                        canvas.DrawRoundRect(ovalRect, capsuleHalfW, capsuleHalfW, stopStrokePaint);
                    }

                    using var textPaint = new SKPaint
                    {
                        Color = SKColor.Parse(StopMagenta),
                        TextSize = StopLabelFontSize,
                        Typeface = labelTypeface,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Left,
                    };

                    canvas.Save();
                    canvas.Translate(labelX, labelY);
                    canvas.RotateDegrees(-45f);
                    canvas.DrawText(stops[i], 0, 0, textPaint);
                    canvas.Restore();
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        });
    }

    private static void DrawRouteSegment(
        SKCanvas canvas,
        SKPaint paint,
        float x1,
        float x2,
        float lineY,
        float lineThickness)
    {
        if (x2 <= x1)
        {
            return;
        }

        canvas.DrawRect(x1, lineY - lineThickness / 2f, x2 - x1, lineThickness, paint);
    }

    private static void DrawFooter(IContainer container) =>
        container.Height(FooterHeightMm, Mm);

    private static void DrawDisclaimer(IContainer container)
    {
        container.AlignCenter().AlignMiddle()
            .Text(text =>
            {
                text.DefaultTextStyle(style => style
                    .FontFamily(DisclaimerFontFamily)
                    .FontSize(DisclaimerFontSize)
                    .LineHeight(DisclaimerLineHeight)
                    .LetterSpacing(DisclaimerLetterSpacing)
                    .FontColor(HeaderBlue));

                text.Span("zuginfo.nrw").Bold();
                text.Span(" ist ein Serviceangebot für Kunden");
                text.Span("\n");
                text.Span("im Regionalverkehr in Nordrhein-Westfalen");
            });
    }

    private sealed record FooterLogoSlot(
        string AssetFileName,
        float WidthMm,
        float HeightMm,
        bool AlignBottom);

    private sealed record FooterLogoPlacement(
        float LeftMm,
        float TopMm,
        float WidthMm,
        float HeightMm,
        string AssetFileName,
        bool AlignBottom);

    private const float FooterLogoRowRightMm = ZuginfoServiceLeftMm - 2f;

    private static float ResolveFooterRowLeftMm(IReadOnlyList<SevOperatorOption> selectedOperators)
    {
        var largeRight = selectedOperators
            .Where(o => o.UseLargeOperatorLogo)
            .Select(o => o.OperatorLogoLeftMm + o.OperatorLogoWidthMm)
            .DefaultIfEmpty(0f)
            .Max();

        return largeRight > 0f ? largeRight + FooterLogoSlotGapMm : FooterLogoRowLeftMm;
    }

    private static void DrawOperatorLogoLayers(
        LayersDescriptor layers,
        IReadOnlyList<SevOperatorOption> selectedOperators,
        string assets)
    {
        foreach (var op in selectedOperators.Where(o => o.UseLargeOperatorLogo))
        {
            var alignToFooterRow = op.OperatorLogoHeightMm <= FooterLogoRowHeightMm + 4f;
            var logoTopMm = alignToFooterRow ? FooterLogoRowTopMm : op.OperatorLogoTopMm;
            var logoHeightMm = alignToFooterRow ? FooterLogoRowHeightMm : op.OperatorLogoHeightMm;

            foreach (var asset in op.PdfLogoAssetFileNames)
            {
                layers.Layer()
                    .Unconstrained()
                    .AlignTop()
                    .AlignLeft()
                    .PaddingTop(logoTopMm, Mm)
                    .PaddingLeft(op.OperatorLogoLeftMm, Mm)
                    .Width(op.OperatorLogoWidthMm, Mm)
                    .Height(logoHeightMm, Mm)
                    .Element(c => DrawOperatorLogo(
                        c,
                        assets,
                        asset,
                        op.OperatorLogoWidthMm,
                        logoHeightMm,
                        alignBottom: !alignToFooterRow,
                        alignMiddle: alignToFooterRow));
            }
        }

        var rowLeft = ResolveFooterRowLeftMm(selectedOperators);
        var rowSlots = BuildFooterLogoRowSlots(selectedOperators);
        foreach (var placement in LayoutFooterLogoRow(rowSlots, rowLeft))
        {
            layers.Layer()
                .Unconstrained()
                .AlignTop()
                .AlignLeft()
                .PaddingTop(placement.TopMm, Mm)
                .PaddingLeft(placement.LeftMm, Mm)
                .Width(placement.WidthMm, Mm)
                .Height(placement.HeightMm, Mm)
                .Element(c => DrawOperatorLogo(
                    c,
                    assets,
                    placement.AssetFileName,
                    placement.WidthMm,
                    placement.HeightMm,
                    placement.AlignBottom));
        }
    }

    private static List<FooterLogoSlot> BuildFooterLogoRowSlots(IReadOnlyList<SevOperatorOption> selectedOperators)
    {
        var slots = new List<FooterLogoSlot>();

        var rowOperators = selectedOperators
            .Where(o => !o.UseLargeOperatorLogo && o.IncludeInFooterLogoRow)
            .Select((op, index) => (op, index))
            .OrderBy(x => x.op.FooterSortOrder)
            .ThenBy(x => x.index)
            .Select(x => x.op);

        foreach (var op in rowOperators)
        {
            foreach (var asset in op.PdfLogoAssetFileNames)
            {
                slots.Add(new FooterLogoSlot(
                    asset,
                    op.OperatorLogoWidthMm,
                    op.OperatorLogoHeightMm,
                    AlignBottom: false));
            }
        }

        return slots;
    }

    private static IReadOnlyList<FooterLogoPlacement> LayoutFooterLogoRow(
        IReadOnlyList<FooterLogoSlot> slots,
        float rowLeftMm)
    {
        if (slots.Count == 0)
        {
            return [];
        }

        var left = rowLeftMm > FooterLogoRowLeftMm + 0.1f ? rowLeftMm : FooterLineLeftMm;
        var right = FooterLogoRowRightMm;
        var available = right - left;
        if (available <= 0f)
        {
            return [];
        }

        var nominalWidths = slots.Select(s => s.WidthMm).ToList();
        var totalNominal = nominalWidths.Sum();
        var scale = totalNominal > available ? available / totalNominal : 1f;
        scale = Math.Min(scale, 1f);

        var widths = nominalWidths.Select(w => w * scale).ToList();

        var placements = new List<FooterLogoPlacement>();

        if (slots.Count == 1)
        {
            var x = left + (available - widths[0]) / 2f;
            AddFooterLogoPlacement(placements, slots[0], x, widths[0], scale);
            return placements;
        }

        var slotWidth = available / slots.Count;
        for (var i = 0; i < slots.Count; i++)
        {
            var slotLeft = left + i * slotWidth;
            var x = slotLeft + (slotWidth - widths[i]) / 2f;
            AddFooterLogoPlacement(placements, slots[i], x, widths[i], scale);
        }

        return placements;
    }

    private static void AddFooterLogoPlacement(
        List<FooterLogoPlacement> placements,
        FooterLogoSlot slot,
        float leftMm,
        float widthMm,
        float scale)
    {
        placements.Add(new FooterLogoPlacement(
            leftMm,
            FooterLogoRowTopMm,
            widthMm,
            slot.HeightMm * scale,
            slot.AssetFileName,
            slot.AlignBottom));
    }

    private static void DrawOperatorLogo(
        IContainer container,
        string assetsDirectory,
        string fileName,
        float maxWidthMm,
        float maxHeightMm,
        bool alignBottom,
        bool alignMiddle = false)
    {
        foreach (var candidate in ResolveOperatorLogoCandidates(fileName))
        {
            var path = Path.Combine(assetsDirectory, candidate);
            if (!File.Exists(path))
            {
                continue;
            }

            var image = TryLoadLogoImage(path);
            if (image is null)
            {
                continue;
            }

            if (alignMiddle)
            {
                container.AlignMiddle().Image(image).FitArea();
            }
            else if (alignBottom)
            {
                container.AlignBottom().Image(image).FitArea();
            }
            else
            {
                container.AlignTop().Image(image).FitArea();
            }

            return;
        }
    }

    private static byte[]? TryLoadLogoImage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var fileName = Path.GetFileName(path);
        if (!ShouldStripWhiteBackground(fileName))
        {
            return File.ReadAllBytes(path);
        }

        return LoadImageStrippingNearWhiteBackground(path) ?? File.ReadAllBytes(path);
    }

    private static bool ShouldStripWhiteBackground(string fileName) =>
        fileName.Contains("deutsche_bahn", StringComparison.OrdinalIgnoreCase)
        || fileName.Contains("re13_db", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("operator_db.png", StringComparison.OrdinalIgnoreCase);

    private static byte[]? LoadImageStrippingNearWhiteBackground(string path)
    {
        using var decoded = SKBitmap.Decode(path);
        if (decoded is null)
        {
            return null;
        }

        using var bitmap = decoded.ColorType == SKColorType.Rgba8888
            ? decoded.Copy()
            : decoded.Copy(SKColorType.Rgba8888);

        if (bitmap is null)
        {
            return null;
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.Red >= 245 && color.Green >= 245 && color.Blue >= 245)
                {
                    bitmap.SetPixel(x, y, SKColors.Transparent);
                }
            }
        }

        using var encoded = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }

    private static IEnumerable<string> ResolveOperatorLogoCandidates(string fileName)
    {
        yield return fileName;

        if (fileName.Equals("operator_re13_eurobahn.png", StringComparison.Ordinal))
        {
            yield break;
        }

        if (fileName.StartsWith("operator_deutsche_bahn", StringComparison.Ordinal))
        {
            yield return "operator_deutsche_bahn.png";
            yield return "operator_deutsche_bahn_310x163.png";
            yield return "operator_re13_db_329x159.png";
            yield return "operator_re13_db_310x163.png";
            yield return "operator_db.png";
            yield break;
        }

        if (fileName.StartsWith("operator_re13", StringComparison.Ordinal))
        {
            yield return "operator_deutsche_bahn.png";
            yield return "operator_deutsche_bahn_310x163.png";
            yield return "operator_re13_db_329x159.png";
            yield return "operator_re13_db_310x163.png";
            yield return "operator_db.png";
        }

        if (fileName.StartsWith("operator_regiobahn", StringComparison.Ordinal))
        {
            yield return "operator_regiobahn_475x106.png";
        }
    }

    private static void DrawDashedBorder(IContainer container)
    {
        container.Svg(size =>
        {
            using var stream = new MemoryStream();
            using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
            {
                using var fill = new SKPaint
                {
                    Color = SKColor.Parse(BorderRed),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                };

                var top = MmToPt(BorderInsetMm, Mm);
                var bottom = MmToPt(292.1f, Mm);
                var left = MmToPt(BorderInsetMm, Mm);
                var right = MmToPt(414.9f, Mm);
                var corner = BorderStrokePt;

                DrawDashedHorizontal(canvas, fill, MmToPt(14f, Mm), MmToPt(408.3f, Mm), top, corner);
                DrawDashedHorizontal(canvas, fill, MmToPt(11.3f, Mm), MmToPt(405.6f, Mm), bottom, corner);
                DrawDashedVertical(canvas, fill, left, MmToPt(11.1f, Mm), MmToPt(282.9f, Mm), corner);
                DrawDashedVertical(canvas, fill, right, MmToPt(13.8f, Mm), MmToPt(285.6f, Mm), corner);

                DrawChamferedBorderCorner(canvas, fill, BorderCorner.TopLeft, left, top, right, bottom, corner);
                DrawChamferedBorderCorner(canvas, fill, BorderCorner.TopRight, left, top, right, bottom, corner);
                DrawChamferedBorderCorner(canvas, fill, BorderCorner.BottomLeft, left, top, right, bottom, corner);
                DrawChamferedBorderCorner(canvas, fill, BorderCorner.BottomRight, left, top, right, bottom, corner);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        });
    }

    private enum BorderCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>L-förmige Eckstriche mit abgeschrägter Außenkante (NRW-Vorlage).</summary>
    private static void DrawChamferedBorderCorner(
        SKCanvas canvas,
        SKPaint paint,
        BorderCorner corner,
        float left,
        float top,
        float right,
        float bottom,
        float legLength)
    {
        var thickness = legLength;
        var bevel = thickness;
        var half = thickness / 2f;

        using var path = new SKPath();
        switch (corner)
        {
            case BorderCorner.TopLeft:
                path.MoveTo(left + legLength, top - half);
                path.LineTo(left + bevel, top - half);
                path.LineTo(left - half, top + bevel);
                path.LineTo(left - half, top + legLength);
                path.LineTo(left + half, top + legLength);
                path.LineTo(left + half, top + half);
                path.LineTo(left + legLength, top + half);
                break;
            case BorderCorner.TopRight:
                path.MoveTo(right - legLength, top - half);
                path.LineTo(right - bevel, top - half);
                path.LineTo(right + half, top + bevel);
                path.LineTo(right + half, top + legLength);
                path.LineTo(right - half, top + legLength);
                path.LineTo(right - half, top + half);
                path.LineTo(right - legLength, top + half);
                break;
            case BorderCorner.BottomLeft:
                path.MoveTo(left + legLength, bottom + half);
                path.LineTo(left + bevel, bottom + half);
                path.LineTo(left - half, bottom - bevel);
                path.LineTo(left - half, bottom - legLength);
                path.LineTo(left + half, bottom - legLength);
                path.LineTo(left + half, bottom - half);
                path.LineTo(left + legLength, bottom - half);
                break;
            case BorderCorner.BottomRight:
                path.MoveTo(right - legLength, bottom + half);
                path.LineTo(right - bevel, bottom + half);
                path.LineTo(right + half, bottom - bevel);
                path.LineTo(right + half, bottom - legLength);
                path.LineTo(right - half, bottom - legLength);
                path.LineTo(right - half, bottom - half);
                path.LineTo(right - legLength, bottom - half);
                break;
        }

        path.Close();
        canvas.DrawPath(path, paint);
    }

    private static void DrawDashedHorizontal(
        SKCanvas canvas,
        SKPaint paint,
        float xStart,
        float xEnd,
        float y,
        float thickness)
    {
        const float dash = 22f;
        const float gap = 15f;
        var x = xStart;

        while (x < xEnd)
        {
            var dashEnd = Math.Min(x + dash, xEnd);
            canvas.DrawRect(x, y - thickness / 2f, dashEnd - x, thickness, paint);
            x += dash + gap;
        }
    }

    private static void DrawDashedVertical(
        SKCanvas canvas,
        SKPaint paint,
        float x,
        float yStart,
        float yEnd,
        float thickness)
    {
        const float dash = 22f;
        const float gap = 15f;
        var y = yStart;

        while (y < yEnd)
        {
            var dashEnd = Math.Min(y + dash, yEnd);
            canvas.DrawRect(x - thickness / 2f, y, thickness, dashEnd - y, paint);
            y += dash + gap;
        }
    }

    private static void DrawSevIcon(IContainer container, string assetsDirectory)
    {
        foreach (var fileName in new[] { "sev_icon.jpeg", "sev_icon.png" })
        {
            var path = Path.Combine(assetsDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            container.Image(File.ReadAllBytes(path)).FitArea();
            return;
        }
    }

    private static void DrawImageIfExists(
        IContainer container,
        string assetsDirectory,
        string fileName,
        float maxWidthMm,
        float maxHeightMm)
    {
        var path = Path.Combine(assetsDirectory, fileName);
        var image = TryLoadLogoImage(path);
        if (image is null)
        {
            return;
        }

        container.Image(image).FitArea();
    }

    private static float MmToPt(float mm, Unit _) => mm * 72f / 25.4f;

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
