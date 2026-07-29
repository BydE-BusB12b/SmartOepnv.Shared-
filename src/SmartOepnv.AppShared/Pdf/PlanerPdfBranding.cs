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
        ComposeHeaderWithOptionalSmartLogo(container, showSmartLogo: true, leftContent);
    }

    public static void ComposeHeaderWithOptionalSmartLogo(
        IContainer container,
        bool showSmartLogo,
        Action<IContainer> leftContent)
    {
        container.AlignMiddle().Row(row =>
        {
            row.RelativeItem().AlignMiddle().Element(leftContent);
            if (showSmartLogo)
            {
                row.ConstantItem(HeaderLogoSize).Height(HeaderLogoSize).AlignRight().AlignMiddle()
                    .Element(DrawSmartOepnvLogo);
            }
        });
    }

    public static void ComposeStandardFooter(IContainer container, DateTime? timestamp = null)
    {
        ComposeFooter(container, ResolveDefaultCompanyLogoPath(), timestamp);
    }

    public static void ComposeFooter(
        IContainer container,
        string? companyLogoPath,
        DateTime? timestamp = null,
        string? leftText = null)
    {
        var stamp = timestamp ?? DateTime.Now;
        var left = string.IsNullOrWhiteSpace(leftText)
            ? $"Smart-ÖPNV · {stamp:dd.MM.yyyy HH:mm}"
            : leftText.Trim();

        container.PaddingTop(4).Row(footerRow =>
        {
            footerRow.RelativeItem().AlignMiddle().Text(text =>
            {
                text.DefaultTextStyle(PlanerPdfTextStyles.Body(8).FontColor("#4A5F82"));
                text.Span(left);
            });

            footerRow.ConstantItem(FooterLogoWidth).Height(FooterLogoHeight).AlignRight().AlignMiddle()
                .Element(c => DrawCompanyLogo(c, companyLogoPath));
        });
    }

    public static void DrawSmartOepnvLogo(IContainer container)
    {
        DrawImageIfExists(container, ResolveSmartOepnvLogoPath());
    }

    public static void DrawCompanyLogo(IContainer container, string? logoPath) =>
        DrawImageIfExists(container, logoPath);

    public static void DrawImage(IContainer container, string? path) =>
        DrawImageIfExists(container, path);

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

    public static string? ResolveCompanyLogoPathById(string? logoId)
    {
        if (!AppServices.IsInitialized || string.IsNullOrWhiteSpace(logoId))
        {
            return null;
        }

        return PlanerBrandingWorkspace.TryGetLogoPath(AppServices.SettingsSubfolder, logoId);
    }
}
