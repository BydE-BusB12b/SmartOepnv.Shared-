using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Pdf;

/// <summary>Gemeinsame Logos für Planer-PDFs (Smart-ÖPNV + Firmenlogo aus Einstellungen).</summary>
public static class PlanerPdfBranding
{
    public const string SmartOepnvLogoFileName = "smart_oepnv_logo.png";

    private const float HeaderLogoSize = 48f;
    private const float FooterLogoWidth = 144f;
    private const float FooterLogoHeight = 54f;

    public static void ComposeHeaderWithSmartLogo(IContainer container, Action<IContainer> leftContent)
    {
        container.AlignMiddle().Row(row =>
        {
            row.RelativeItem().AlignMiddle().Element(leftContent);
            row.ConstantItem(HeaderLogoSize).Height(HeaderLogoSize).AlignRight().AlignMiddle()
                .Element(DrawSmartOepnvLogo);
        });
    }

    public static void ComposeStandardFooter(IContainer container, DateTime? timestamp = null)
    {
        var companyLogoPath = ResolveDefaultCompanyLogoPath();
        var stamp = timestamp ?? DateTime.Now;

        container.PaddingTop(4).Row(footerRow =>
        {
            footerRow.RelativeItem().AlignMiddle().Text(text =>
            {
                text.DefaultTextStyle(PlanerPdfTextStyles.Body(8).FontColor("#4A5F82"));
                text.Span("Smart-ÖPNV · ");
                text.Span(stamp.ToString("dd.MM.yyyy HH:mm")).FontColor("#5472D3");
            });

            footerRow.ConstantItem(FooterLogoWidth).Height(FooterLogoHeight).AlignRight().AlignMiddle()
                .Element(c => DrawCompanyLogo(c, companyLogoPath));
        });
    }

    public static void DrawSmartOepnvLogo(IContainer container)
    {
        DrawImageIfExists(container, ResolveSmartOepnvLogoPath());
    }

    private static void DrawCompanyLogo(IContainer container, string? logoPath) =>
        DrawImageIfExists(container, logoPath);

    private static void DrawImageIfExists(IContainer container, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            container.Image(File.ReadAllBytes(path)).FitArea();
        }
        catch
        {
            // Logo optional
        }
    }

    public static string? ResolveSmartOepnvLogoPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", SmartOepnvLogoFileName),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", SmartOepnvLogoFileName))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? ResolveDefaultCompanyLogoPath()
    {
        if (!AppServices.IsInitialized || !AppServices.IsPlannerApp)
        {
            return null;
        }

        var logos = PlanerBrandingWorkspace.GetLogos(AppServices.SettingsSubfolder);
        var first = logos.FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        return PlanerBrandingWorkspace.TryGetLogoPath(AppServices.SettingsSubfolder, first.Id);
    }
}
