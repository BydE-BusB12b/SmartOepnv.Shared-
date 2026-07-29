using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.AppShared.Pdf;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Mitteilungen;

public sealed class MitteilungPdfModel
{
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string ValidFrom { get; init; } = string.Empty;
    public string ValidTo { get; init; } = string.Empty;
    public bool UntilRevoked { get; init; }
    public bool ShowSmartOepnvLogo { get; init; } = true;
    public string? CompanyLogoId { get; init; }
    public string SignerNameAndDate { get; init; } = string.Empty;
    public string? SignatureId { get; init; }
}

public static class MitteilungPdfGenerator
{
    private const string PrimaryBlue = "#0D47A1";
    private const string TextMuted = "#4A5F82";
    private const float SignatureMaxWidth = 160f;
    private const float SignatureMaxHeight = 56f;
    private const float CompanyLogoWidth = 150f;
    private const float CompanyLogoHeight = 54f;

    static MitteilungPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public static void Generate(string outputPath, MitteilungPdfModel model)
    {
        var created = DateTime.Now;
        var companyLogoPath = PlanerPdfBranding.ResolveCompanyLogoPathById(model.CompanyLogoId);
        var signaturePath = ResolveSignaturePath(model.SignatureId);
        var validityText = BuildValidityText(model);
        var body = model.Body.Trim().IfEmpty("");

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x =>
                    PlanerPdfTextStyles.ApplyLight(x).FontSize(11).FontColor(Colors.Black).LineHeight(1.35f));

                // Oben: Überschrift + Smart-ÖPNV-Logo
                page.Header().Element(header =>
                    PlanerPdfBranding.ComposeHeaderWithOptionalSmartLogo(
                        header,
                        model.ShowSmartOepnvLogo,
                        left => left.Column(col =>
                        {
                            col.Item().Text("Mitteilung")
                                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                                .FontSize(11)
                                .FontColor(TextMuted);
                            col.Item().PaddingTop(2).Text(model.Title.Trim().IfEmpty("Ohne Überschrift"))
                                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                                .FontSize(18)
                                .SemiBold()
                                .FontColor(PrimaryBlue);
                        })));

                // Mitte: Gültigkeit oben, Hinweistext vertikal mittig in der Restfläche
                page.Content().PaddingTop(18).Column(column =>
                {
                    column.Item().Text(validityText)
                        .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                        .FontSize(10)
                        .FontColor(TextMuted);

                    column.Item().ExtendVertical().AlignMiddle().Text(body)
                        .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                        .FontSize(11)
                        .LineHeight(1.45f)
                        .AlignLeft();
                });

                // Unten (Footer = immer Seitenende, kein Umbruch): Unterschrift | Firmenlogo, darunter Zeitstempel
                page.Footer().Column(footer =>
                {
                    footer.Item().Row(row =>
                    {
                        row.RelativeItem().AlignBottom().Column(left =>
                        {
                            if (!string.IsNullOrWhiteSpace(signaturePath) && File.Exists(signaturePath))
                            {
                                left.Item()
                                    .MaxWidth(SignatureMaxWidth)
                                    .MaxHeight(SignatureMaxHeight)
                                    .Element(c => PlanerPdfBranding.DrawImage(c, signaturePath));
                                left.Item().PaddingTop(4);
                            }

                            left.Item().Text(model.SignerNameAndDate.Trim())
                                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                                .FontSize(10)
                                .FontColor(Colors.Black);
                        });

                        row.ConstantItem(CompanyLogoWidth).AlignBottom().AlignRight()
                            .Height(CompanyLogoHeight)
                            .Element(c => PlanerPdfBranding.DrawCompanyLogo(c, companyLogoPath));
                    });

                    footer.Item().PaddingTop(10).Text($"Smart-ÖPNV · erstellt {created:dd.MM.yyyy HH:mm}")
                        .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                        .FontSize(8)
                        .FontColor(TextMuted);
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static string BuildValidityText(MitteilungPdfModel model)
    {
        var from = model.ValidFrom.Trim();
        if (model.UntilRevoked)
        {
            return string.IsNullOrWhiteSpace(from)
                ? "Gültig: bis auf Widerruf"
                : $"Gültig ab {from} – bis auf Widerruf";
        }

        var to = model.ValidTo.Trim();
        return (from, to) switch
        {
            ("", "") => "Gültigkeit: nicht angegeben",
            (_, "") => $"Gültig ab {from}",
            ("", _) => $"Gültig bis {to}",
            _ => $"Gültig ab {from} bis {to}"
        };
    }

    private static string? ResolveSignaturePath(string? signatureId)
    {
        if (!AppServices.IsInitialized || string.IsNullOrWhiteSpace(signatureId))
        {
            return null;
        }

        return PlanerMitteilungSignaturesWorkspace.TryGetSignaturePath(
            AppServices.SettingsSubfolder,
            signatureId);
    }
}

file static class MitteilungPdfTextExtensions
{
    public static string IfEmpty(this string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
